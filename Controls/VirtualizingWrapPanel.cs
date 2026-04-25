using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ComicReader.Controls
{
    // A lightweight VirtualizingWrapPanel implementation adapted for WPF apps.
    // Supports virtualization with IScrollInfo and recycling. This is a simplified
    // version intended for card-like layouts. For heavy-duty scenarios consider
    // a more complete implementation.
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        private ItemsControl _itemsControl;
        private IItemContainerGenerator _generator;
        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset;
        private bool _canHScroll = false;
        private bool _canVScroll = true;

        public double ItemWidth { get; set; } = 280;
        public double ItemHeight { get; set; } = 160;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            _itemsControl = ItemsControl.GetItemsOwner(this);
            _generator = ItemContainerGenerator;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_itemsControl == null) _itemsControl = ItemsControl.GetItemsOwner(this);
            if (_generator == null) _generator = ItemContainerGenerator;

            int itemCount = _itemsControl.HasItems ? _itemsControl.Items.Count : 0;
            if (itemCount == 0) return new Size(0, 0);

            // Handle infinite available size (e.g., inside unconstrained ScrollViewer)
            double effectiveWidth = availableSize.Width;
            if (double.IsInfinity(effectiveWidth) || effectiveWidth <= 0)
            {
                // Fallback: if unconstrained, lay out items in a single row
                effectiveWidth = itemCount * ItemWidth;
            }

            int itemsPerRow = Math.Max(1, (int)Math.Floor(effectiveWidth / ItemWidth));
            int rowCount = (int)Math.Ceiling((double)itemCount / itemsPerRow);

            _extent = new Size(itemsPerRow * ItemWidth, rowCount * ItemHeight);
            _viewport = new Size(double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
                                 double.IsInfinity(availableSize.Height) ? Math.Min(_extent.Height, SystemParameters.PrimaryScreenHeight) : availableSize.Height);

            UpdateScrollInfo();

            // Determine visible range
            int firstVisibleRow = (int)Math.Floor(_offset.Y / ItemHeight);
            int visibleRows = (int)Math.Ceiling(_viewport.Height / ItemHeight) + 1;
            int startIndex = firstVisibleRow * itemsPerRow;
            int endIndex = Math.Min(itemCount - 1, (firstVisibleRow + visibleRows) * itemsPerRow - 1);

            // Generate children for visible range
            var children = InternalChildren;
            var startPos = _generator.GeneratorPositionFromIndex(startIndex);
            int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

            using (_generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int i = startIndex; i <= endIndex; ++i, ++childIndex)
                {
                    bool newlyRealized;
                    var child = (UIElement)_generator.GenerateNext(out newlyRealized);
                    if (newlyRealized)
                    {
                        if (childIndex >= children.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);
                        _generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(ItemWidth, ItemHeight));
                }
            }

            // Remove children that are not in the realized range
            for (int i = children.Count - 1; i >= 0; --i)
            {
                var genPos = _generator.GeneratorPositionFromIndex(i);
                int itemIndex = _generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (itemIndex < startIndex || itemIndex > endIndex)
                {
                    _generator.Remove(new GeneratorPosition(i, 0), 1);
                    RemoveInternalChildRange(i, 1);
                }
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_itemsControl == null) _itemsControl = ItemsControl.GetItemsOwner(this);
            int itemCount = _itemsControl.HasItems ? _itemsControl.Items.Count : 0;
            if (itemCount == 0) return finalSize;

            int itemsPerRow = Math.Max(1, (int)Math.Floor(finalSize.Width / ItemWidth));

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                // Mapeo correcto: el hijo realizado en la posicion i del
                // panel corresponde al item de datos resuelto via
                // GeneratorPosition(i, 0). Antes el codigo hacia
                // GeneratorPositionFromIndex(childIndex) tratando childIndex
                // (0,1,2,...) como item index, y al hacer round-trip via
                // IndexFromGeneratorPosition obtenia siempre itemIndex==i.
                // Cuando el usuario hacia scroll, InternalChildren[0] ya no
                // era el item 0 sino el item N (segun startIndex computado
                // en MeasureOverride), pero el arrange los posicionaba como
                // si fueran 0..N-1 — quedando arriba del viewport y dejando
                // un area vacia visible. Mismo patron que la limpieza al
                // final de MeasureOverride.
                int itemIndex = _generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (itemIndex < 0) itemIndex = i;

                int row = itemIndex / itemsPerRow;
                int col = itemIndex % itemsPerRow;
                double x = col * ItemWidth - _offset.X;
                double y = row * ItemHeight - _offset.Y;
                child.Arrange(new Rect(new Point(x, y), new Size(ItemWidth, ItemHeight)));
            }

            return finalSize;
        }

        #region IScrollInfo implementation (basic)
        public bool CanHorizontallyScroll { get => _canHScroll; set => _canHScroll = value; }
        public bool CanVerticallyScroll { get => _canVScroll; set => _canVScroll = value; }
        public double ExtentHeight => _extent.Height;
        public double ExtentWidth => _extent.Width;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;
        public double ViewportHeight => _viewport.Height;
        public double ViewportWidth => _viewport.Width;
        public ScrollViewer ScrollOwner { get; set; }

        public void LineDown() => SetVerticalOffset(VerticalOffset + 20);
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 20);
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + 20);
        public void LineUp() => SetVerticalOffset(VerticalOffset - 20);
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 48);
        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 48);
        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 48);
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 48);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

        public void SetHorizontalOffset(double offset)
        {
            if (offset < 0) offset = 0;
            if (offset + ViewportWidth > ExtentWidth) offset = ExtentWidth - ViewportWidth;
            _offset.X = offset;
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        public void SetVerticalOffset(double offset)
        {
            if (offset < 0) offset = 0;
            if (offset + ViewportHeight > ExtentHeight) offset = ExtentHeight - ViewportHeight;
            _offset.Y = offset;
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }

        private void UpdateScrollInfo()
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
        #endregion
    }
}
