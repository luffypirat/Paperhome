using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Paperhome.Data;
using Paperhome.Messages;
using Paperhome.Services;
using PdfiumViewer;

namespace Paperhome.Views
{
    public partial class InspectorView : System.Windows.Controls.UserControl
    {
        private PdfDocument? _pdfDocument;
        private int    _currentPdfPage;
        private double _pdfZoom = 1.0;
        private string _currentFilePath = string.Empty;  // путь для "открыть в системе"
        private string? _currentTempPath;                // расшифрованный временный файл
        private CancellationTokenSource _loadCts = new();

        private BitmapImage? _currentImageBitmap;
        private double _imageZoom = 1.0;

        public InspectorView()
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<DocumentSelectedMessage>(this, (_, m) =>
            {
                Dispatcher.InvokeAsync(() => LoadDocumentAsync(m.DocumentId));
            });
            Unloaded += (_, _) =>
            {
                _loadCts.Cancel();
                _loadCts.Dispose();
                if (_currentTempPath != null) TryDeleteTemp(_currentTempPath);
                _pdfDocument?.Dispose();
            };
        }

        private async void LoadDocumentAsync(int documentId)
        {
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            ResetView();
            if (documentId <= 0) return;

            using var db = new PaperworkDbContext();
            var doc = db.Documents.Find(documentId);
            if (doc == null) return;

            string storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "Files");
            string encPath     = Path.Combine(storagePath, doc.RelativePath);
            string ext         = Path.GetExtension(doc.OriginalFileName).ToLower();

            if (!File.Exists(encPath))
            {
                TxtPlaceholder.Text       = "[ФАЙЛ НЕ НАЙДЕН НА ДИСКЕ]";
                TxtPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            // Расшифровать во временный файл
            string tempPath;
            try
            {
                tempPath = await System.Threading.Tasks.Task.Run(
                    () => EncryptionService.Current.DecryptToTemp(encPath, ext), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                TxtPlaceholder.Text       = $"[ОШИБКА РАСШИФРОВКИ: {ex.Message}]";
                TxtPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            if (ct.IsCancellationRequested)
            {
                TryDeleteTemp(tempPath);
                return;
            }

            _currentTempPath = tempPath;
            _currentFilePath = tempPath;

            try
            {
                if (ext is ".png" or ".jpg" or ".jpeg")
                    await ShowImageAsync(tempPath, ct);
                else if (ext is ".txt" or ".md")
                    await ShowTextAsync(tempPath, ct);
                else if (ext == ".pdf")
                    await ShowPdfAsync(tempPath, ct);
                else if (ext is ".doc" or ".docx")
                    await ShowWordAsPdfAsync(tempPath, ct);
                else
                {
                    TxtPlaceholder.Text       = "[ФОРМАТ НЕ ПОДДЕРЖИВАЕТСЯ]";
                    TxtPlaceholder.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    TxtPlaceholder.Text       = $"[ОШИБКА ЧТЕНИЯ: {ex.Message}]";
                    TxtPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void ResetView()
        {
            _currentFilePath = string.Empty;

            // Удалить предыдущий временный файл
            if (_currentTempPath != null)
            {
                TryDeleteTemp(_currentTempPath);
                _currentTempPath = null;
            }

            _currentImageBitmap = null;
            _imageZoom = 1.0;

            TxtPlaceholder.Text       = "[ВЫВОД ДОКУМЕНТА]";
            TxtPlaceholder.Visibility = Visibility.Visible;
            ImgScrollViewer.Visibility = Visibility.Collapsed;
            TxtViewer.Visibility       = Visibility.Collapsed;
            DocToolbar.Visibility      = Visibility.Collapsed;
            PageNavPanel.Visibility    = Visibility.Collapsed;

            ImgViewer.Source = null;
            ImgViewer.LayoutTransform = Transform.Identity;
            ImgViewer.ClearValue(WidthProperty);
            ImgViewer.ClearValue(HeightProperty);
            ImgViewer.Stretch = Stretch.None;
            TxtViewer.Text = string.Empty;

            _pdfDocument?.Dispose();
            _pdfDocument = null;
        }

        private static void TryDeleteTemp(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private async System.Threading.Tasks.Task ShowImageAsync(string path, CancellationToken ct)
        {
            var bitmap = await System.Threading.Tasks.Task.Run(() =>
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource   = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }, ct);

            if (ct.IsCancellationRequested) return;

            _currentImageBitmap = bitmap;
            _imageZoom = 1.0;

            ImgViewer.Stretch           = Stretch.None;
            ImgViewer.Source            = bitmap;
            ImgScrollViewer.Visibility  = Visibility.Visible;
            DocToolbar.Visibility       = Visibility.Visible;

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (ct.IsCancellationRequested) return;

            _imageZoom = CalculateImageFitZoom();
            ApplyImageZoom();
        }

        private async System.Threading.Tasks.Task ShowTextAsync(string path, CancellationToken ct)
        {
            var text = await System.Threading.Tasks.Task.Run(() => File.ReadAllText(path), ct);
            if (ct.IsCancellationRequested) return;
            TxtViewer.Visibility = Visibility.Visible;
            TxtViewer.Text       = text;
        }

        private async System.Threading.Tasks.Task ShowPdfAsync(string path, CancellationToken ct)
        {
            DocToolbar.Visibility      = Visibility.Visible;
            PageNavPanel.Visibility    = Visibility.Visible;
            ImgScrollViewer.Visibility = Visibility.Visible;

            var doc = await System.Threading.Tasks.Task.Run(() => PdfDocument.Load(path), ct);

            if (ct.IsCancellationRequested) { doc.Dispose(); return; }

            _pdfDocument    = doc;
            _currentPdfPage = 0;

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (ct.IsCancellationRequested) return;

            _pdfZoom = CalculateFitWidthZoom();
            RenderPdfPage();
            UpdatePageCounter();
        }

        private async System.Threading.Tasks.Task ShowWordAsPdfAsync(string path, CancellationToken ct)
        {
            if (Type.GetTypeFromProgID("Word.Application") == null)
            {
                TxtPlaceholder.Text       = "[MICROSOFT WORD НЕ УСТАНОВЛЕН]";
                TxtPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            DocToolbar.Visibility      = Visibility.Visible;
            PageNavPanel.Visibility    = Visibility.Visible;
            ImgScrollViewer.Visibility = Visibility.Visible;
            TxtPlaceholder.Text        = "[КОНВЕРТАЦИЯ...]";
            TxtPlaceholder.Visibility  = Visibility.Visible;

            byte[] pdfBytes;
            try   { pdfBytes = await ConvertWordToPdfAsync(path, ct); }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;

            var doc = await System.Threading.Tasks.Task.Run(() =>
            {
                var ms = new MemoryStream(pdfBytes);
                return PdfDocument.Load(ms);
            }, ct);

            if (ct.IsCancellationRequested) { doc.Dispose(); return; }

            _pdfDocument    = doc;
            _currentPdfPage = 0;
            TxtPlaceholder.Visibility = Visibility.Collapsed;

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (ct.IsCancellationRequested) return;

            _pdfZoom = CalculateFitWidthZoom();
            RenderPdfPage();
            UpdatePageCounter();
        }

        private static System.Threading.Tasks.Task<byte[]> ConvertWordToPdfAsync(
            string path, CancellationToken ct)
        {
            var tcs     = new TaskCompletionSource<byte[]>();
            var tempPdf = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");

            var thread = new Thread(() =>
            {
                dynamic? word = null;
                dynamic? doc  = null;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
                    word.Visible       = false;
                    word.DisplayAlerts = 0;
                    doc = word.Documents.Open(
                        FileName: path, ReadOnly: true,
                        AddToRecentFiles: false, Visible: false);
                    doc.ExportAsFixedFormat(OutputFileName: tempPdf, ExportFormat: 17);
                    doc.Close(SaveChanges: false);  doc  = null;
                    word.Quit(SaveChanges: false);  word = null;
                    tcs.TrySetResult(File.ReadAllBytes(tempPdf));
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception ex)               { tcs.TrySetException(ex); }
                finally
                {
                    try { doc?.Close(false);  } catch { }
                    try { word?.Quit(false);  } catch { }
                    try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        // ── PDF ─────────────────────────────────────────────────────────────

        private double CalculateFitWidthZoom()
        {
            if (_pdfDocument == null) return 1.0;
            double w = ImgScrollViewer.ActualWidth > 0 ? ImgScrollViewer.ActualWidth : ActualWidth;
            if (w <= 0) w = 600;
            var pageSize    = _pdfDocument.PageSizes[_currentPdfPage];
            double pageWpf  = pageSize.Width / 72.0 * 96.0;
            return Math.Max((w - 40) / pageWpf, 0.1);
        }

        private void RenderPdfPage()
        {
            if (_pdfDocument == null) return;

            ImgViewer.Stretch        = Stretch.Fill;
            ImgViewer.LayoutTransform = Transform.Identity;

            var pageSize      = _pdfDocument.PageSizes[_currentPdfPage];
            double logW       = pageSize.Width  / 72.0 * 96.0 * _pdfZoom;
            double logH       = pageSize.Height / 72.0 * 96.0 * _pdfZoom;
            int renderW       = Math.Max(1, (int)logW);
            int renderH       = Math.Max(1, (int)logH);

            using var image = _pdfDocument.Render(_currentPdfPage, renderW, renderH, PdfRenderFlags.CorrectFromDpi);
            using var ms    = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            ImgViewer.Source = bitmap;
            ImgViewer.Width  = logW;
            ImgViewer.Height = logH;
        }

        private void UpdatePageCounter()
        {
            if (_pdfDocument != null)
                TxtPageCounter.Text = $"{_currentPdfPage + 1:D2} / {_pdfDocument.PageCount:D2}";
        }

        // ── Изображения ─────────────────────────────────────────────────────

        private double CalculateImageFitZoom()
        {
            if (_currentImageBitmap == null) return 1.0;
            double w = ImgScrollViewer.ActualWidth > 0 ? ImgScrollViewer.ActualWidth : ActualWidth;
            if (w <= 0) w = 600;
            return Math.Max((w - 20) / _currentImageBitmap.PixelWidth, 0.05);
        }

        private void ApplyImageZoom()
        {
            if (_currentImageBitmap == null) return;
            ImgViewer.LayoutTransform = new ScaleTransform(_imageZoom, _imageZoom);
        }

        // ── Кнопки ──────────────────────────────────────────────────────────

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null)
            {
                _pdfZoom = Math.Min(_pdfZoom + 0.2, 10.0);
                RenderPdfPage();
            }
            else if (_currentImageBitmap != null)
            {
                _imageZoom = Math.Min(_imageZoom + 0.2, 10.0);
                ApplyImageZoom();
            }
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null && _pdfZoom > 0.3)
            {
                _pdfZoom = Math.Max(_pdfZoom - 0.2, 0.1);
                RenderPdfPage();
            }
            else if (_currentImageBitmap != null && _imageZoom > 0.15)
            {
                _imageZoom = Math.Max(_imageZoom - 0.2, 0.1);
                ApplyImageZoom();
            }
        }

        private void BtnFitPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null)
            {
                _pdfZoom = CalculateFitWidthZoom();
                RenderPdfPage();
            }
            else if (_currentImageBitmap != null)
            {
                _imageZoom = CalculateImageFitZoom();
                ApplyImageZoom();
            }
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument == null || _currentPdfPage <= 0) return;
            _currentPdfPage--;
            RenderPdfPage();
            UpdatePageCounter();
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument == null || _currentPdfPage >= _pdfDocument.PageCount - 1) return;
            _currentPdfPage++;
            RenderPdfPage();
            UpdatePageCounter();
        }

        private void BtnOpenNative_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath)) return;
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo(_currentFilePath) { UseShellExecute = true }
                };
                p.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Ошибка вызова системного приложения: {ex.Message}",
                    "СИСТЕМНЫЙ СБОЙ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
