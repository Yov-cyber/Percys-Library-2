using System.Windows;

namespace ComicReader.Views
{
    public partial class RatingWindow : Window
    {
        public int Stars { get; private set; }
        public string Comment { get; private set; }

        public RatingWindow()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (StarCombo.SelectedIndex >= 0)
                Stars = StarCombo.SelectedIndex + 1;
            Comment = CommentBox.Text;
            this.DialogResult = true;
            this.Close();
        }
    }
}
