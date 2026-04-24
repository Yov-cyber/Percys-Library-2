using System.Windows;
using System.Windows.Controls;

namespace ComicReader.Views.Controls
{
    public partial class SliderComic : UserControl
    {
        public SliderComic()
        {
            InitializeComponent();
            Inner.ValueChanged += (s, e) => { Value = Inner.Value; };
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(SliderComic), new PropertyMetadata(0.0, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (SliderComic)d;
            if (ctl.Inner != null)
                ctl.Inner.Value = (double)e.NewValue;
        }
    }
}
