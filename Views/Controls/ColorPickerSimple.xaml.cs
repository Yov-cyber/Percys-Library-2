using System.Windows;
using System.Windows.Controls;

namespace ComicReader.Views.Controls
{
    public partial class ColorPickerSimple : UserControl
    {
        public ColorPickerSimple()
        {
            InitializeComponent();
            HexBox.TextChanged += (s, e) => { ColorHex = HexBox.Text; };
            // Attach simple pick behavior
            try
            {
                var btn = this.FindName("") as Button; // noop - keep safe if name not assigned
            }
            catch { }
        }

        public static readonly DependencyProperty ColorHexProperty = DependencyProperty.Register(
            nameof(ColorHex), typeof(string), typeof(ColorPickerSimple), new PropertyMetadata("#FFAA00", OnColorHexChanged));

        public string ColorHex
        {
            get => (string)GetValue(ColorHexProperty);
            set => SetValue(ColorHexProperty, value);
        }

        private static void OnColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (ColorPickerSimple)d;
            if (ctl.HexBox != null)
                ctl.HexBox.Text = e.NewValue as string;
        }

        private void PickClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use WinForms ColorDialog for quick color picking
                System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog();
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = dlg.Color;
                    var hex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                    ColorHex = hex;
                }
            }
            catch { }
        }
    }
}
