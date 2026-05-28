using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using Paperhome.Data;
using Paperhome.Messages;
using PdfiumViewer;
using Spire.Doc; // FreeSpire.Doc
namespace Paperhome.Views
{
    public partial class InspectorView : System.Windows.Controls.UserControl
    {
        private PdfDocument _pdfDocument;
        private int _currentPdfPage = 0;
        private double _pdfZoom = 1.0;
        private string _currentFilePath = string.Empty;
        public InspectorView()
        {
            InitializeComponent();
            // Подписка на выделение файла в дереве архива
            WeakReferenceMessenger.Default.Register<DocumentSelectedMessage>(this, (r, m) => 
            {
                LoadDocument(m.DocumentId);
            });
        }
        private void LoadDocument(int documentId)
        {
            ResetView();
            if (documentId <= 0) return;
            using var db = new PaperworkDbContext();
            var doc = db.Documents.Find(documentId);
            if (doc == null) return;
            string storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "Files");
            string fullPath = Path.Combine(storagePath, doc.RelativePath);
            if (!File.Exists(fullPath))
            {
                TxtPlaceholder.Text = "[ФАЙЛ НЕ НАЙДЕН НА ДИСКЕ]";
                TxtPlaceholder.Visibility = Visibility.Visible;
                return;
            }
            _currentFilePath = fullPath;
            string ext = Path.GetExtension(doc.OriginalFileName).ToLower();
            try
            {
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    ShowImage(fullPath);
                }
                else if (ext == ".txt" || ext == ".md")
                {
                    ShowText(fullPath);
                }
                else if (ext == ".pdf")
                {
                    ShowPdf(fullPath);
                }
                else if (ext == ".doc" || ext == ".docx")
                {
                    ShowWordAsPdf(fullPath);
                }
                else
                {
                    TxtPlaceholder.Text = "[ФОРМАТ НЕ ПОДДЕРЖИВАЕТСЯ]";
                    TxtPlaceholder.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                TxtPlaceholder.Text = $"[ОШИБКА ЧТЕНИЯ: {ex.Message}]";
                TxtPlaceholder.Visibility = Visibility.Visible;
            }
        }
        private void ResetView()
        {
            _currentFilePath = string.Empty;
            TxtPlaceholder.Visibility = Visibility.Collapsed;
            ImgScrollViewer.Visibility = Visibility.Collapsed;
            TxtViewer.Visibility = Visibility.Collapsed;
            PdfToolbar.Visibility = Visibility.Collapsed;
            
            ImgViewer.Source = null;
            TxtViewer.Text = string.Empty;
            
            if (_pdfDocument != null)
            {
                _pdfDocument.Dispose();
                _pdfDocument = null;
            }
        }
        private void ShowImage(string path)
        {
            ImgScrollViewer.Visibility = Visibility.Visible;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Чтобы файл не блокировался
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            ImgViewer.Source = bitmap;
        }
        private void ShowText(string path)
        {
            TxtViewer.Visibility = Visibility.Visible;
            TxtViewer.Text = File.ReadAllText(path);
        }
        private void ShowPdf(string path)
        {
            PdfToolbar.Visibility = Visibility.Visible;
            ImgScrollViewer.Visibility = Visibility.Visible;
            
            _pdfDocument = PdfDocument.Load(path);
            _currentPdfPage = 0;
            _pdfZoom = CalculateFitWidthZoom();
            RenderPdfPage();
            UpdatePageCounter();
        }
        private void ShowWordAsPdf(string path)
        {
            PdfToolbar.Visibility = Visibility.Visible;
            ImgScrollViewer.Visibility = Visibility.Visible;

            string tempPdfPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            Spire.Doc.Document document = new Spire.Doc.Document();
            document.LoadFromFile(path);
            document.SaveToFile(tempPdfPath, FileFormat.PDF);
            _pdfDocument = PdfDocument.Load(tempPdfPath);
            _currentPdfPage = 0;
            _pdfZoom = CalculateFitWidthZoom();
            RenderPdfPage();
            UpdatePageCounter();
        }
        
        private double CalculateFitWidthZoom()
        {
            if (_pdfDocument == null) return 1.0;
            double w = ImgScrollViewer.ActualWidth;
            if (w == 0) w = this.ActualWidth;
            if (w == 0) w = 600; // Fallback
            
            var pageSize = _pdfDocument.PageSizes[_currentPdfPage];
            double pageWidthWpf = (pageSize.Width / 72.0) * 96.0;
            
            double zoom = (w - 40) / pageWidthWpf; // 40 for scrollbar and margin
            return Math.Max(zoom, 0.1);
        }

        private void RenderPdfPage()
        {
            if (_pdfDocument == null) return;
            
            var pageSize = _pdfDocument.PageSizes[_currentPdfPage];
            double logicalWidth = (pageSize.Width / 72.0) * 96.0 * _pdfZoom;
            double logicalHeight = (pageSize.Height / 72.0) * 96.0 * _pdfZoom;

            int renderWidth = Math.Max(1, (int)(logicalWidth * 2.0)); // 2x for retina crispness
            int renderHeight = Math.Max(1, (int)(logicalHeight * 2.0));

            using var image = _pdfDocument.Render(_currentPdfPage, renderWidth, renderHeight, PdfRenderFlags.CorrectFromDpi);
            using var ms = new MemoryStream();
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            
            ImgViewer.Source = bitmapImage;
            ImgViewer.Width = logicalWidth;
            ImgViewer.Height = logicalHeight;
        }
        private void UpdatePageCounter()
        {
            if (_pdfDocument != null)
            {
                TxtPageCounter.Text = $"СТРАНИЦА: {(_currentPdfPage + 1):D2} / {_pdfDocument.PageCount:D2}";
            }
        }
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null)
            {
                _pdfZoom += 0.2;
                RenderPdfPage();
            }
        }
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null && _pdfZoom > 0.3)
            {
                _pdfZoom -= 0.2;
                RenderPdfPage();
            }
        }
        private void BtnFitPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null)
            {
                _pdfZoom = CalculateFitWidthZoom();
                RenderPdfPage();
            }
        }
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null && _currentPdfPage > 0)
            {
                _currentPdfPage--;
                RenderPdfPage();
                UpdatePageCounter();
            }
        }
        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pdfDocument != null && _currentPdfPage < _pdfDocument.PageCount - 1)
            {
                _currentPdfPage++;
                RenderPdfPage();
                UpdatePageCounter();
            }
        }
        private void BtnOpenNative_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath)) return;
            try
            {
                var p = new Process();
                p.StartInfo = new ProcessStartInfo(_currentFilePath)
                {
                    UseShellExecute = true
                };
                p.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка вызова системного приложения: {ex.Message}", "СИСТЕМНЫЙ СБОЙ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
