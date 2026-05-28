using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Paperhome.Models;
using PdfiumViewer;
using DocumentFormat.OpenXml.Packaging;

namespace Paperhome.Services
{
    public class LocalAnalysisService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
        private readonly string _model;
        private readonly string _endpoint;

        public LocalAnalysisService(string ollamaUrl = "http://localhost:11434", string model = "qwen2.5:0.5b")
        {
            _model    = model;
            _endpoint = ollamaUrl.TrimEnd('/') + "/api/chat";
        }

        private const string SystemPrompt =
            "Ты — ассистент для архивирования документов. " +
            "Анализируй текст и возвращай ТОЛЬКО валидный JSON без пояснений, " +
            "без markdown-блоков (```), без лишних слов. Только чистый JSON.";

        private const string UserPrompt =
            "Проанализируй документ и верни JSON строго в таком формате:\n" +
            "{\"title\": \"...\", \"tags\": [\"...\", \"...\", \"...\"], \"summary\": \"...\"}\n\n" +
            "Требования к полям:\n" +
            "- title: конкретное название документа, отражающее его суть (до 60 символов)\n" +
            "- tags: 3–5 ключевых тегов для классификации и поиска (одно-два слова каждый)\n" +
            "- summary: краткое содержание документа — что в нём, о чём он (100–200 символов)\n\n" +
            "Текст документа:\n";


        // ── Основной метод ───────────────────────────────────────────────────

        public async Task<(string Title, string[] Tags, string Summary)> AnalyzeAsync(
            DocumentRecord doc, string filePath, CancellationToken ct)
        {
            string ext      = Path.GetExtension(doc.OriginalFileName).ToLower();
            bool   isEnc    = filePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase)
                              && EncryptionService.Current.IsUnlocked;
            string workPath = isEnc
                ? await Task.Run(() => EncryptionService.Current.DecryptToTemp(filePath, ext), ct)
                : filePath;

            try
            {
                string text;
                if (ext is ".txt" or ".md")
                    text = await ExtractTextFileAsync(workPath, ct);
                else if (ext is ".doc" or ".docx")
                    text = await ExtractWordTextAsync(workPath, ct);
                else if (ext == ".pdf")
                    text = await ExtractPdfTextWithOcrFallbackAsync(workPath, ct);
                else if (ext is ".png" or ".jpg" or ".jpeg")
                    text = await ExtractImageTextAsync(workPath, ct);
                else
                    throw new NotSupportedException($"Формат {ext} не поддерживается.");

                if (string.IsNullOrWhiteSpace(text))
                    text = "[документ без распознанного текста]";

                return await CallAsync(text, ct);
            }
            finally
            {
                if (isEnc)
                    try { File.Delete(workPath); } catch { }
            }
        }

        // ── Извлечение текста ────────────────────────────────────────────────

        private static async Task<string> ExtractTextFileAsync(string path, CancellationToken ct)
        {
            var text = await Task.Run(() => File.ReadAllText(path), ct);
            return Truncate(text);
        }

        private static async Task<string> ExtractWordTextAsync(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                if (Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = WordprocessingDocument.Open(path, false);
                    var body = doc.MainDocumentPart?.Document?.Body;
                    if (body == null) return "";
                    var sb = new StringBuilder();
                    foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        sb.Append(para.InnerText);
                        sb.Append(' ');
                        if (sb.Length > 1200) break;
                    }
                    return Truncate(sb.ToString());
                }
                else // .doc — старый бинарный формат, не поддерживается без COM
                    throw new NotSupportedException("Формат .doc не поддерживается. Сохраните файл как .docx.");
            }, ct);
        }

        private static async Task<string> ExtractPdfTextWithOcrFallbackAsync(string path, CancellationToken ct)
        {
            // Try embedded text first
            var text = await Task.Run(() =>
            {
                using var pdfDoc = PdfDocument.Load(path);
                var sb = new StringBuilder();
                for (int i = 0; i < Math.Min(pdfDoc.PageCount, 3); i++)
                {
                    sb.Append(pdfDoc.GetPdfText(i));
                    if (sb.Length > 1200) break;
                }
                return Truncate(sb.ToString());
            }, ct);

            // If meaningful text found — use it
            if (text.Length >= 50) return text;

            // Scanned PDF — OCR the first page
            System.Drawing.Bitmap? bmp = null;
            try
            {
                bmp = await Task.Run(() =>
                {
                    using var pdfDoc = PdfDocument.Load(path);
                    // Render at ~150 DPI (good balance of speed vs quality for OCR)
                    return (System.Drawing.Bitmap)pdfDoc.Render(0, 1200, 1600, PdfRenderFlags.CorrectFromDpi);
                }, ct);

                return Truncate(await OcrService.RecognizeAsync(bmp, ct));
            }
            finally
            {
                bmp?.Dispose();
            }
        }

        private static async Task<string> ExtractImageTextAsync(string path, CancellationToken ct)
        {
            System.Drawing.Bitmap? bmp = null;
            try
            {
                bmp = await Task.Run(() => new System.Drawing.Bitmap(path), ct);
                return Truncate(await OcrService.RecognizeAsync(bmp, ct));
            }
            finally
            {
                bmp?.Dispose();
            }
        }

        private static string Truncate(string text)
        {
            text = string.Join(" ",
                text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return text.Length > 1000 ? text[..1000] : text;
        }

        // ── Запрос к Ollama ──────────────────────────────────────────────────

        private async Task<(string Title, string[] Tags, string Summary)> CallAsync(
            string text, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["model"]  = _model,
                ["stream"] = false,
                ["options"] = new JsonObject { ["temperature"] = 0, ["num_gpu"] = 0 },
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                    new JsonObject { ["role"] = "user",   ["content"] = UserPrompt + text }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var response = await _http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Ollama вернул {(int)response.StatusCode}: {raw}");

            var apiDoc  = JsonDocument.Parse(raw);
            var content = apiDoc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            return ParseResult(content);
        }

        // ── Разбор ответа ────────────────────────────────────────────────────

        private static (string Title, string[] Tags, string Summary) ParseResult(string text)
        {
            var start = text.IndexOf('{');
            var end   = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return ("ОШИБКА РАЗБОРА", Array.Empty<string>(), text.Trim());

            var json = text[start..(end + 1)];
            try
            {
                var doc     = JsonDocument.Parse(json);
                var title   = GetStr(doc, "title");
                var summary = GetStr(doc, "summary");
                var tags    = doc.RootElement.TryGetProperty("tags", out var tagsEl)
                    ? tagsEl.EnumerateArray()
                             .Select(e => e.GetString()?.Trim() ?? "")
                             .Where(s => s.Length > 0)
                             .ToArray()
                    : Array.Empty<string>();
                return (title, tags, summary);
            }
            catch
            {
                return ("ОШИБКА РАЗБОРА", Array.Empty<string>(), json);
            }
        }

        private static string GetStr(JsonDocument doc, string key) =>
            doc.RootElement.TryGetProperty(key, out var v) ? v.GetString()?.Trim() ?? "" : "";
    }
}
