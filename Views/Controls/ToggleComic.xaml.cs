using System.Windows;
using System.Windows.Controls;

namespace ComicReader.Views.Controls
{
    public partial class ToggleComic : UserControl
    {
        public ToggleComic()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
            nameof(IsChecked), typeof(bool), typeof(ToggleComic), new PropertyMetadata(false, OnIsCheckedChanged));

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (ToggleComic)d;
            var isChecked = (bool)e.NewValue;
            // Update visual: move thumb and change track background
            ctl.Thumb.HorizontalAlignment = isChecked ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            try
            {
                if (ctl.Track != null)
                {
                    ctl.Track.Background = isChecked ? (System.Windows.Media.Brush)ctl.FindResource("PrimaryBrush") ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xED,0x6A,0x00)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray);
                }
            }
            catch { }
        }
    }
}
