using MapInfoApp.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MapInfoApp
{
    public class IrelandMapViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Place> Places { get; } = new();

        private Place _selectedPlace;
        public Place SelectedPlace
        {
            get => _selectedPlace;
            set { _selectedPlace = value; OnPropertyChanged(); }
        }

        public ICommand HideOverlayCommand { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public Action<bool> OverlayVisibleChanged { get; set; }

        public IrelandMapViewModel()
        {
            HideOverlayCommand = new Command(() => OverlayVisibleChanged?.Invoke(false));
        }

        void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
