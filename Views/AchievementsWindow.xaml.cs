using System.Linq;
using System.Windows;
using ComicReader.Services;

namespace ComicReader.Views
{
    /// <summary>
    /// Vista discreta del catálogo de logros. Refresca contra
    /// AchievementService cada vez que se abre, para que los cambios de
    /// stats que ocurrieron desde la ultima visita aparezcan reflejados.
    /// </summary>
    public partial class AchievementsWindow : Window
    {
        public AchievementsWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => RefreshUI();
        }

        private void RefreshUI()
        {
            try
            {
                AchievementService.Instance.Refresh();
                var all = AchievementService.Instance.GetAll().ToList();
                AchievementsList.ItemsSource = all;

                int unlocked = all.Count(a => a.Unlocked);
                int total = all.Count;
                HeaderSubtitle.Text = total == 0
                    ? "Sin logros disponibles"
                    : $"Has desbloqueado {unlocked} de {total}.";
                OverallProgress.Value = total == 0 ? 0 : (100.0 * unlocked / total);
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
