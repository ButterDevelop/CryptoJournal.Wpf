using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.ViewModels;

namespace CryptoJournal.Wpf.Views.Dialogs;

public partial class ImageViewerWindow : Window
{
    private readonly ViewerViewModel _vm;

    public ImageViewerWindow(IReadOnlyList<AttachmentVm> items, AttachmentVm initial)
    {
        InitializeComponent();
        _vm         = new ViewerViewModel(items, initial);
        DataContext = _vm;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Left)   _vm.GoPrev();
        if (e.Key == Key.Right)  _vm.GoNext();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _vm.GoPrev();
    private void Next_Click(object sender, RoutedEventArgs e) => _vm.GoNext();
}

public partial class ViewerViewModel : ObservableObject
{
    private readonly IReadOnlyList<AttachmentVm> _items;
    private int _currentIndex;

    [ObservableProperty] private AttachmentVm currentImage;

    public bool HasMultiple => _items.Count > 1;
    public bool CanGoPrev => _currentIndex > 0;
    public bool CanGoNext => _currentIndex < _items.Count - 1;

    public ViewerViewModel(IReadOnlyList<AttachmentVm> items, AttachmentVm initial)
    {
        _items        = items;
        _currentIndex = Math.Max(0, items.ToList().IndexOf(initial));
        currentImage  = _items[_currentIndex];
    }

    public void GoPrev()
    {
        if (!CanGoPrev) return;
        _currentIndex--;
        UpdateState();
    }

    public void GoNext()
    {
        if (!CanGoNext) return;
        _currentIndex++;
        UpdateState();
    }

    private void UpdateState()
    {
        CurrentImage = _items[_currentIndex];
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }
}