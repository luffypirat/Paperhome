using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Paperhome.Services
{
    internal static class OcrService
    {
        public static async Task<string> RecognizeAsync(Bitmap sourceBitmap, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                      ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
            if (engine == null) return string.Empty;

            // Resize to OCR engine limit (2048px max per side)
            int maxDim = (int)OcrEngine.MaxImageDimension;
            int w = sourceBitmap.Width, h = sourceBitmap.Height;
            if (w > maxDim || h > maxDim)
            {
                double scale = Math.Min((double)maxDim / w, (double)maxDim / h);
                w = Math.Max(1, (int)(w * scale));
                h = Math.Max(1, (int)(h * scale));
            }

            // Convert System.Drawing.Bitmap → PNG bytes (background thread)
            byte[] pngBytes = await Task.Run(() =>
            {
                using var resized = new Bitmap(w, h);
                using var g = Graphics.FromImage(resized);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(sourceBitmap, 0, 0, w, h);
                using var ms = new MemoryStream();
                resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }, ct);

            ct.ThrowIfCancellationRequested();

            // PNG bytes → WinRT SoftwareBitmap
            using var ims = new InMemoryRandomAccessStream();
            using (var dw = new DataWriter(ims.GetOutputStreamAt(0)))
            {
                dw.WriteBytes(pngBytes);
                await dw.StoreAsync();
                dw.DetachStream();
            }
            ims.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ims);
            using var soft = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            ct.ThrowIfCancellationRequested();

            var result = await engine.RecognizeAsync(soft);
            return result.Text;
        }
    }
}
