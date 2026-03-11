using CryptoJournal.Wpf.ViewModels.Dialogs;
using System.Windows;

namespace CryptoJournal.Wpf.Views.Dialogs;

public partial class EnvironmentManagerDialog : Window
{
    public EnvironmentManagerDialog(EnvironmentManagerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Owner       = Application.Current.MainWindow;
    }
}