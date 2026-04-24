using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ComicReader.Views
{
    public partial class ReopenCompletedDialog : Window
    {
        public enum OpenChoice { Start, Continue, Unmark }

        public OpenChoice Choice { get; private set; } = OpenChoice.Start;

        public ReopenCompletedDialog()
        {
            InitializeComponent();
            BtnCancel.Click += (_, __) => { this.DialogResult = false; this.Close(); };
            BtnOk.Click += (_, __) => { OnOk(); };
        }

        private void OnOk()
        {
            if (RbStart.IsChecked == true) Choice = OpenChoice.Start;
            else if (RbContinue.IsChecked == true) Choice = OpenChoice.Continue;
            else Choice = OpenChoice.Unmark;
            this.DialogResult = true;
            this.Close();
        }

        public void SetInfo(string title, string dateCompleted, BitmapImage cover)
        {
            TitleText.Text = title ?? "(sin título)";
            MetaText.Text = string.IsNullOrWhiteSpace(dateCompleted) ? "Completado" : $"Completado el: {dateCompleted}";
            try { if (cover != null) CoverImage.Source = cover; } catch { }
        }
    }
}
