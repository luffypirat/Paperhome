using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Paperhome.Data;
using Paperhome.Messages;
using Paperhome.Models;
using Paperhome.Services;

namespace Paperhome.Views
{
    public partial class MetadataView
    {
        private int _currentDocId;
        private bool _suppressChange;
        private CancellationTokenSource _analyzeCts = new();
        private AppSettings _settings = AppSettings.Load();

        public MetadataView()
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<DocumentSelectedMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(() => LoadDocument(m.DocumentId));
            });
        }
        
        // ── Загрузка документа ───────────────────────────────────────────────

        private void LoadDocument(int id)
        {
            _currentDocId = id;

            if (id == 0)
            {
                ClearFields();
                BtnAnalyze.IsEnabled = false;
                BtnSave.IsEnabled    = false;
                return;
            }

            try
            {
                DocumentRecord? doc;
                using (var db = new PaperworkDbContext())
                    doc = db.Documents.Include(d => d.Tags).FirstOrDefault(d => d.Id == id);

                if (doc == null)
                {
                    ClearFields();
                    SetStatus("> ДОКУМЕНТ НЕ НАЙДЕН.");
                    return;
                }

                _suppressChange = true;
                TxtDocName.Text  = doc.OriginalFileName;
                TxtTitle.Text    = doc.AiGeneratedName == "ОЖИДАЕТ АНАЛИЗА..." ? "" : doc.AiGeneratedName;
                TxtTags.Text     = string.Join(", ", doc.Tags.Select(t => t.Name));
                TxtSummary.Text  = doc.Summary;
                _suppressChange  = false;

                BtnAnalyze.IsEnabled = true;
                BtnSave.IsEnabled    = false;

                SetStatus("> ГОТОВ.");
            }
            catch (Exception ex)
            {
                SetStatus($"> ОШИБКА ЗАГРУЗКИ: {ex.Message}");
            }
        }

        private void ClearFields()
        {
            _suppressChange = true;
            TxtDocName.Text = "—";
            TxtTitle.Text   = "";
            TxtTags.Text    = "";
            TxtSummary.Text = "";
            _suppressChange = false;
            SetStatus("> ОЖИДАНИЕ...");
        }

        // ── Изменение полей ──────────────────────────────────────────────────

        private void Fields_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressChange || _currentDocId == 0) return;
            BtnSave.IsEnabled = true;
        }

        // ── Анализ ИИ (Ollama) ───────────────────────────────────────────────

        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocId == 0) return;

            DocumentRecord? doc;
            using (var db = new PaperworkDbContext())
                doc = db.Documents.Include(d => d.Tags).FirstOrDefault(d => d.Id == _currentDocId);
            if (doc == null) return;

            var storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "Files");
            var filePath    = Path.Combine(storagePath, doc.RelativePath);

            _analyzeCts.Cancel();
            _analyzeCts.Dispose();
            _analyzeCts = new CancellationTokenSource();
            var ct = _analyzeCts.Token;

            BtnAnalyze.IsEnabled = false;
            BtnSave.IsEnabled    = false;

            var modeLabel = $"> АНАЛИЗ [{_settings.LocalModel.ToUpper()}]";

            using var typewriterCts = new CancellationTokenSource();
            using var linkedCts    = CancellationTokenSource.CreateLinkedTokenSource(ct, typewriterCts.Token);
            var typewriterTask     = RunTypewriterAsync(modeLabel, linkedCts.Token);

            try
            {
                _settings = AppSettings.Load();
            var service = new LocalAnalysisService(_settings.OllamaUrl, _settings.LocalModel);
                var (title, tags, summary) =
                    await service.AnalyzeAsync(doc, filePath, ct);

                if (ct.IsCancellationRequested) return;

                _suppressChange = true;
                TxtTitle.Text   = title;
                TxtTags.Text    = string.Join(", ", tags);
                TxtSummary.Text = summary;
                _suppressChange = false;

                BtnSave.IsEnabled = true;
                SetStatus("> АНАЛИЗ ЗАВЕРШЕН.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("> ОТМЕНЕНО.");
            }
            catch (Exception ex)
            {
                SetStatus($"> ОШИБКА: {ex.Message}");
            }
            finally
            {
                typewriterCts.Cancel();
                try { await typewriterTask; } catch { }
                BtnAnalyze.IsEnabled = _currentDocId != 0;
            }
        }

        private async Task RunTypewriterAsync(string baseText, CancellationToken ct)
        {
            var dots = new[] { "", ".", "..", "..." };
            int i = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    SetStatus(baseText + dots[i % dots.Length]);
                    i++;
                    await Task.Delay(400, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        // ── Сохранение ───────────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocId == 0) return;

            try
            {
                using var db = new PaperworkDbContext();
                var doc = db.Documents.Include(d => d.Tags).FirstOrDefault(d => d.Id == _currentDocId);
                if (doc == null) return;

                doc.AiGeneratedName = TxtTitle.Text.Trim();
                doc.Summary         = TxtSummary.Text.Trim();

                var newTagNames = TxtTags.Text
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                doc.Tags.Clear();

                foreach (var name in newTagNames)
                {
                    var tag = db.Tags.FirstOrDefault(t => t.Name == name)
                              ?? new TagRecord { Name = name };
                    doc.Tags.Add(tag);
                }

                db.SaveChanges();

                BtnSave.IsEnabled = false;
                SetStatus("> СОХРАНЕНО.");

                WeakReferenceMessenger.Default.Send(new ArchiveUpdatedMessage());
            }
            catch (Exception ex)
            {
                SetStatus($"> ОШИБКА СОХРАНЕНИЯ: {ex.Message}");
            }
        }

        // ── Вспомогательные ──────────────────────────────────────────────────

        private void SetStatus(string text) => TxtStatus.Text = text;
    }
}
