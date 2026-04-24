using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ComicReader.Views.Controls
{
    public partial class StatCard : UserControl
    {
        public StatCard()
        {
                try
                {
                    var mi = this.GetType().GetMethod("InitializeComponent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    mi?.Invoke(this, null);
                }
                catch { }
            // Respect user's reduce-motion preference
            if (SystemParameters.ClientAreaAnimation)
            {
                var rb = this.FindName("RootBorder") as System.Windows.FrameworkElement;
                if (rb != null)
                {
                    rb.MouseEnter += RootBorder_MouseEnter;
                    rb.MouseLeave += RootBorder_MouseLeave;
                }
            }

            // Accessibility: set an accessible name combining label and value when available
            try
            {
                var label = (string)GetValue(LabelProperty) ?? string.Empty;
                var value = (string)GetValue(ValueProperty) ?? string.Empty;
                var name = string.IsNullOrWhiteSpace(label) ? value : (label + ": " + value);
                System.Windows.Automation.AutomationProperties.SetName(this, name);
            }
            catch { }
        }

        private void RootBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                var daX = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
                var daY = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
                var scale = this.FindName("CardScale") as ScaleTransform;
                if (scale != null)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, daX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, daY);
                }
            }
            catch { }
        }

        private void RootBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                var daX = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
                var daY = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
                var scale = this.FindName("CardScale") as ScaleTransform;
                if (scale != null)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, daX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, daY);
                }
            }
            catch { }
        }

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(ImageSource), typeof(StatCard), new PropertyMetadata(null));

        public ImageSource Icon
        {
            get => (ImageSource)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(StatCard), new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
    }
}
