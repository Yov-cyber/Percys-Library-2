using System;
using System.Windows.Input;
using ComicReader.Models;
using ComicReader.Commands;
using System.ComponentModel;

namespace ComicReader.ViewModels
{
    public class FavoriteComicViewModel : INotifyPropertyChanged
    {
        public FavoriteComic Model { get; }

        public FavoriteComicViewModel(FavoriteComic model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            OpenCommand = new RelayCommand(() => OnOpen?.Invoke(this, EventArgs.Empty));
            RemoveCommand = new RelayCommand(() => OnRemove?.Invoke(this, EventArgs.Empty));
        }

        public string Title => Model.Title;
        public string Thumbnail => Model.Thumbnail;
        public double Progress => Model.Progress;
        public string RatingStars => Model.RatingStars;

        public ICommand OpenCommand { get; }
        public ICommand RemoveCommand { get; }

        public event EventHandler OnOpen;
        public event EventHandler OnRemove;

        #pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
        #pragma warning restore CS0067
    }
}
