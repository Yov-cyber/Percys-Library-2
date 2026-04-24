using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComicReader.Utils
{
    public static class ImageAdjuster
    {
        // Aplica brillo (multiplicativo) y contraste sobre una fuente BitmapSource y devuelve una nueva imagen BGRA32.
        // brightness: 0..2 (1 = sin cambio)
        // contrast:   0..2 (1 = sin cambio)
        public static BitmapSource ApplyBrightnessContrast(BitmapSource source, double brightness, double contrast)
        {
            if (source == null) return null;
            try
            {
                // Convertir a BGRA32 para acceso a bytes fácil
                var fmt = PixelFormats.Bgra32;
                var formatted = source.Format == fmt ? source : new FormatConvertedBitmap(source, fmt, null, 0);
                int width = formatted.PixelWidth;
                int height = formatted.PixelHeight;
                int stride = (width * fmt.BitsPerPixel + 7) / 8;
                var pixels = new byte[height * stride];
                formatted.CopyPixels(pixels, stride, 0);

                // Preparar factores
                float b = (float)Math.Max(0.0, brightness); // multiplicativo
                float c = (float)Math.Max(0.0, contrast);

                // Para contraste, fórmula: out = ((in - 0.5) * c) + 0.5 ; después brillo multiplicativo
                // Procesar en BGRA
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int i = row + x * 4;
                        byte B = pixels[i + 0];
                        byte G = pixels[i + 1];
                        byte R = pixels[i + 2];
                        byte A = pixels[i + 3];

                        pixels[i + 0] = Adjust(B, b, c);
                        pixels[i + 1] = Adjust(G, b, c);
                        pixels[i + 2] = Adjust(R, b, c);
                        pixels[i + 3] = A; // preservar alfa
                    }
                }

                var wb = BitmapSource.Create(width, height, formatted.DpiX, formatted.DpiY, fmt, null, pixels, stride);
                try { wb.Freeze(); } catch { }
                return wb;
            }
            catch
            {
                return source; // fallback seguro
            }
        }

        private static byte Adjust(byte value, float brightness, float contrast)
        {
            float f = value / 255f;
            // contraste alrededor de 0.5
            f = ((f - 0.5f) * contrast) + 0.5f;
            // brillo multiplicativo
            f = f * brightness;
            if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
            return (byte)(f * 255f + 0.5f);
        }
    }
}
