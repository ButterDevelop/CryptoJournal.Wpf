using CryptoJournal.Wpf.ViewModels;
using System.Windows;

namespace CryptoJournal.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}