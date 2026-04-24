using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ComicReader.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ComicReader.Services
{
    /// <summary>
    /// Servicio de zoom inteligente con detección de paneles/viñetas
    /// Detecta automáticamente las viñetas y permite hacer zoom en ellas
    /// </summary>
    public sealed class SmartZoomService
    {
        private static readonly Lazy<SmartZoomService> _instance = 
            new Lazy<SmartZoomService>(() => new SmartZoomService());
        
        public static SmartZoomService Instance => _instance.Value;

        // Configuración
        public int MinPanelWidthPercent { get; set; } = 15;  // % del ancho total
        public int MinPanelHeightPercent { get; set; } = 15; // % del alto total
        public double EdgeDetectionThreshold { get; set; } = 0.3; // Sensibilidad

        // Cache de paneles detectados
        private readonly Dictionary<string, List<PanelRect>> _panelCache = 
            new Dictionary<string, List<PanelRect>>();

        private SmartZoomService()
        {
            ModernLogger.Info("✓ SmartZoomService inicializado");
        }

        /// <summary>
        /// Detecta paneles/viñetas en una imagen de cómic
        /// </summary>
        public async Task<List<PanelRect>> DetectPanelsAsync(string imagePath)
        {
            try
            {
                // Verificar caché
                if (_panelCache.TryGetValue(imagePath, out var cachedPanels))
                {
                    ModernLogger.Info($"📦 Paneles cargados desde caché: {cachedPanels.Count}");
                    return cachedPanels;
                }

                ModernLogger.Info($"🔍 Detectando paneles en: {System.IO.Path.GetFileName(imagePath)}");

                var panels = await Task.Run(() =>
                {
                    using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath))
                    {
                        return DetectPanelsInternal(image);
                    }
                });

                // Guardar en caché
                _panelCache[imagePath] = panels;
                
                ModernLogger.Info($"✓ Paneles detectados: {panels.Count}");
                return panels;
            }
            catch (Exception ex)
            {
                ModernLogger.Error($"Error detectando paneles: {ex.Message}");
                return new List<PanelRect>();
            }
        }

        /// <summary>
        /// Detecta paneles usando análisis de bordes y contraste
        /// </summary>
        private List<PanelRect> DetectPanelsInternal(SixLabors.ImageSharp.Image<Rgba32> image)
        {
            var panels = new List<PanelRect>();
            var width = image.Width;
            var height = image.Height;

            // Convertir a escala de grises para análisis
            using (var grayImage = image.Clone(ctx => ctx.Grayscale()))
            {
                // Detectar bordes (algoritmo simplificado)
                var edges = DetectEdges(grayImage);

                // Encontrar regiones rectangulares
                var regions = FindRectangularRegions(edges, width, height);

                // Filtrar y validar paneles
                var minWidth = width * MinPanelWidthPercent / 100;
                var minHeight = height * MinPanelHeightPercent / 100;

                foreach (var region in regions)
                {
                    if (region.Width >= minWidth && region.Height >= minHeight)
                    {
                        panels.Add(region);
                    }
                }

                // Ordenar paneles por posición (lectura occidental)
                panels = panels.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();

                // Si no se detectaron paneles, crear uno que cubra toda la imagen
                if (panels.Count == 0)
                {
                    panels.Add(new PanelRect
                    {
                        X = 0,
                        Y = 0,
                        Width = width,
                        Height = height,
                        IsFullPage = true
                    });
                }
            }

            return panels;
        }

        /// <summary>
        /// Detecta bordes en la imagen usando gradientes
        /// </summary>
        private bool[,] DetectEdges(SixLabors.ImageSharp.Image<Rgba32> grayImage)
        {
            var width = grayImage.Width;
            var height = grayImage.Height;
            var edges = new bool[width, height];

            // Aplicar filtro Sobel simplificado
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    var gx = CalculateGradientX(grayImage, x, y);
                    var gy = CalculateGradientY(grayImage, x, y);
                    var magnitude = Math.Sqrt(gx * gx + gy * gy);

                    edges[x, y] = magnitude > (EdgeDetectionThreshold * 255);
                }
            }

            return edges;
        }

        private double CalculateGradientX(SixLabors.ImageSharp.Image<Rgba32> image, int x, int y)
        {
            var p1 = image[x - 1, y - 1].R;
            var p2 = image[x - 1, y].R;
            var p3 = image[x - 1, y + 1].R;
            var p4 = image[x + 1, y - 1].R;
            var p5 = image[x + 1, y].R;
            var p6 = image[x + 1, y + 1].R;

            return (-p1 - 2 * p2 - p3 + p4 + 2 * p5 + p6) / 4.0;
        }

        private double CalculateGradientY(SixLabors.ImageSharp.Image<Rgba32> image, int x, int y)
        {
            var p1 = image[x - 1, y - 1].R;
            var p2 = image[x, y - 1].R;
            var p3 = image[x + 1, y - 1].R;
            var p4 = image[x - 1, y + 1].R;
            var p5 = image[x, y + 1].R;
            var p6 = image[x + 1, y + 1].R;

            return (-p1 - 2 * p2 - p3 + p4 + 2 * p5 + p6) / 4.0;
        }

        /// <summary>
        /// Encuentra regiones rectangulares en el mapa de bordes
        /// </summary>
        private List<PanelRect> FindRectangularRegions(bool[,] edges, int width, int height)
        {
            var regions = new List<PanelRect>();

            // Algoritmo simplificado: dividir en grid y buscar áreas vacías
            var gridSize = 50; // Tamaño de celda del grid
            var cols = width / gridSize;
            var rows = height / gridSize;

            var visited = new bool[cols, rows];

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    if (visited[col, row]) continue;

                    // Verificar si esta celda tiene muchos bordes (es un separador)
                    var cellHasEdges = HasSignificantEdges(edges, 
                        col * gridSize, row * gridSize, gridSize, gridSize);

                    if (!cellHasEdges)
                    {
                        // Expandir región desde esta celda
                        var region = ExpandRegion(edges, visited, col, row, cols, rows, gridSize);
                        if (region != null)
                        {
                            regions.Add(region);
                        }
                    }
                }
            }

            return regions;
        }

        private bool HasSignificantEdges(bool[,] edges, int startX, int startY, int w, int h)
        {
            var edgeCount = 0;
            var totalPixels = w * h;

            for (int y = startY; y < Math.Min(startY + h, edges.GetLength(1)); y++)
            {
                for (int x = startX; x < Math.Min(startX + w, edges.GetLength(0)); x++)
                {
                    if (edges[x, y]) edgeCount++;
                }
            }

            return (double)edgeCount / totalPixels > 0.2; // 20% de bordes
        }

        private PanelRect ExpandRegion(bool[,] edges, bool[,] visited, 
            int startCol, int startRow, int maxCols, int maxRows, int gridSize)
        {
            var minCol = startCol;
            var maxCol = startCol;
            var minRow = startRow;
            var maxRow = startRow;

            // Expandir horizontalmente
            while (maxCol < maxCols - 1)
            {
                var nextCol = maxCol + 1;
                var hasEdges = false;

                for (int row = minRow; row <= maxRow; row++)
                {
                    if (HasSignificantEdges(edges, nextCol * gridSize, row * gridSize, 
                        gridSize, gridSize))
                    {
                        hasEdges = true;
                        break;
                    }
                }

                if (hasEdges) break;
                maxCol = nextCol;
            }

            // Expandir verticalmente
            while (maxRow < maxRows - 1)
            {
                var nextRow = maxRow + 1;
                var hasEdges = false;

                for (int col = minCol; col <= maxCol; col++)
                {
                    if (HasSignificantEdges(edges, col * gridSize, nextRow * gridSize, 
                        gridSize, gridSize))
                    {
                        hasEdges = true;
                        break;
                    }
                }

                if (hasEdges) break;
                maxRow = nextRow;
            }

            // Marcar como visitado
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    visited[col, row] = true;
                }
            }

            return new PanelRect
            {
                X = minCol * gridSize,
                Y = minRow * gridSize,
                Width = (maxCol - minCol + 1) * gridSize,
                Height = (maxRow - minRow + 1) * gridSize,
                IsFullPage = false
            };
        }

        /// <summary>
        /// Calcula el área de zoom óptima para un panel
        /// </summary>
        public Rect CalculateZoomArea(PanelRect panel, double imageWidth, double imageHeight, 
            double viewportWidth, double viewportHeight)
        {
            // Agregar padding
            var padding = 20;
            var x = Math.Max(0, panel.X - padding);
            var y = Math.Max(0, panel.Y - padding);
            var width = Math.Min(imageWidth - x, panel.Width + padding * 2);
            var height = Math.Min(imageHeight - y, panel.Height + padding * 2);

            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Obtiene el siguiente panel en orden de lectura
        /// </summary>
        public PanelRect GetNextPanel(List<PanelRect> panels, PanelRect currentPanel)
        {
            var currentIndex = panels.IndexOf(currentPanel);
            if (currentIndex >= 0 && currentIndex < panels.Count - 1)
            {
                return panels[currentIndex + 1];
            }
            return null;
        }

        /// <summary>
        /// Obtiene el panel anterior en orden de lectura
        /// </summary>
        public PanelRect GetPreviousPanel(List<PanelRect> panels, PanelRect currentPanel)
        {
            var currentIndex = panels.IndexOf(currentPanel);
            if (currentIndex > 0)
            {
                return panels[currentIndex - 1];
            }
            return null;
        }

        /// <summary>
        /// Limpia la caché de paneles
        /// </summary>
        public void ClearCache()
        {
            _panelCache.Clear();
            ModernLogger.Info("🧹 Caché de paneles limpiada");
        }

        /// <summary>
        /// Clase para representar un panel/viñeta
        /// </summary>
        public class PanelRect
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool IsFullPage { get; set; }

            public Rect ToRect() => new Rect(X, Y, Width, Height);

            public override string ToString() => 
                $"Panel({X},{Y},{Width}x{Height})";
        }
    }
}
