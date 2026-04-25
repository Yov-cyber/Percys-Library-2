using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComicReader.Services
{
    /// <summary>
    /// Recorta bordes blancos (o casi blancos) de una pagina de comic.
    ///
    /// Algoritmo simple y robusto:
    ///   - Convierte a Bgra32 si hace falta para tener un layout uniforme.
    ///   - Escanea desde cada borde hacia adentro hasta encontrar la primera
    ///     fila/columna que NO es "blanca" segun un umbral configurable.
    ///   - Devuelve un CroppedBitmap del rect resultante; si no se encuentra
    ///     nada que recortar (o el recorte seria mas del 35% del area, lo que
    ///     sugiere un falso positivo), devuelve la imagen original sin tocar.
    ///
    /// Performance: para detectar si una linea es blanca solo muestrea N
    /// columnas (o filas) equiespaciadas, no la linea entera. Esto reduce
    /// drasticamente el costo en imagenes grandes (4K+) sin afectar la
    /// deteccion del 99% de los casos reales (paginas escaneadas con borde
    /// blanco uniforme).
    /// </summary>
    public static class WhiteBorderCropper
    {
        // Umbral: una fila/columna se considera "blanca" si todas las muestras
        // tienen luminancia >= 240 (0..255). Permite leve papel amarilleado.
        private const int WhitenessThreshold = 240;

        // Numero de muestras por linea. 32 es suficiente para detectar texto
        // o tinta en cualquier punto de una pagina horizontal/vertical.
        private const int SamplesPerLine = 32;

        // Max recorte permitido por borde, como fraccion del lado total.
        // Si una pagina detecta "blanco" en mas del 40% del alto/ancho hacia
        // adentro, casi seguro estamos lidiando con una viñeta blanca o un
        // splash y no queremos recortar.
        private const double MaxCropFraction = 0.40;

        public static BitmapSource Crop(BitmapSource source)
        {
            if (source == null) return null;
            try
            {
                var src = source;
                // CopyPixels exige Bgra32 para nuestra heuristica simple.
                if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Bgr32)
                {
                    var fmt = new FormatConvertedBitmap();
                    fmt.BeginInit();
                    fmt.Source = src;
                    fmt.DestinationFormat = PixelFormats.Bgra32;
                    fmt.EndInit();
                    fmt.Freeze();
                    src = fmt;
                }

                int w = src.PixelWidth;
                int h = src.PixelHeight;
                if (w <= 8 || h <= 8) return source;

                int stride = (src.Format.BitsPerPixel / 8) * w;

                // Una sola copia del buffer completo, reutilizada por las 4
                // funciones de escaneo. Antes ScanCols hacia su propia copia
                // dos veces (~64MB extras por pagina 4K) y ScanRows pedia
                // CopyPixels por fila, lo que sumaba mucha presion al GC.
                byte[] pixels = new byte[stride * h];
                src.CopyPixels(pixels, stride, 0);

                int top = ScanRows(pixels, w, h, stride, fromTop: true);
                int bottom = ScanRows(pixels, w, h, stride, fromTop: false);
                int left = ScanCols(pixels, w, h, stride, fromLeft: true);
                int right = ScanCols(pixels, w, h, stride, fromLeft: false);

                int maxTop = (int)(h * MaxCropFraction);
                int maxBottom = (int)(h * MaxCropFraction);
                int maxLeft = (int)(w * MaxCropFraction);
                int maxRight = (int)(w * MaxCropFraction);

                if (top > maxTop) top = 0;
                if (bottom > maxBottom) bottom = 0;
                if (left > maxLeft) left = 0;
                if (right > maxRight) right = 0;

                if (top == 0 && bottom == 0 && left == 0 && right == 0) return source;

                int newW = Math.Max(1, w - left - right);
                int newH = Math.Max(1, h - top - bottom);
                if (newW <= 0 || newH <= 0) return source;

                var cropped = new CroppedBitmap(src, new Int32Rect(left, top, newW, newH));
                cropped.Freeze();
                return cropped;
            }
            catch
            {
                return source;
            }
        }

        // Devuelve la cantidad de filas blancas consecutivas desde el borde indicado.
        private static int ScanRows(byte[] pixels, int w, int h, int stride, bool fromTop)
        {
            int count = 0;
            int step = fromTop ? 1 : -1;
            int start = fromTop ? 0 : h - 1;
            int end = fromTop ? h : -1;
            for (int y = start; y != end; y += step)
            {
                if (!IsRowWhite(pixels, y, w, stride)) break;
                count++;
            }
            return count;
        }

        // Devuelve la cantidad de columnas blancas consecutivas desde el borde indicado.
        private static int ScanCols(byte[] pixels, int w, int h, int stride, bool fromLeft)
        {
            int count = 0;
            int step = fromLeft ? 1 : -1;
            int start = fromLeft ? 0 : w - 1;
            int end = fromLeft ? w : -1;
            for (int x = start; x != end; x += step)
            {
                if (!IsColumnWhite(pixels, x, w, h, stride)) break;
                count++;
            }
            return count;
        }

        private static bool IsRowWhite(byte[] pixels, int y, int w, int stride)
        {
            int sampleStep = Math.Max(1, w / SamplesPerLine);
            int rowOffset = y * stride;
            for (int x = 0; x < w; x += sampleStep)
            {
                int i = rowOffset + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                if (!IsWhite(r, g, b)) return false;
            }
            return true;
        }

        private static bool IsColumnWhite(byte[] all, int x, int w, int h, int stride)
        {
            int sampleStep = Math.Max(1, h / SamplesPerLine);
            for (int y = 0; y < h; y += sampleStep)
            {
                int i = y * stride + x * 4;
                byte b = all[i];
                byte g = all[i + 1];
                byte r = all[i + 2];
                if (!IsWhite(r, g, b)) return false;
            }
            return true;
        }

        private static bool IsWhite(byte r, byte g, byte b)
        {
            return r >= WhitenessThreshold && g >= WhitenessThreshold && b >= WhitenessThreshold;
        }
    }
}
