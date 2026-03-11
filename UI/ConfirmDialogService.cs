using CryptoJournal.Wpf.ViewModels.Dialogs;
using CryptoJournal.Wpf.Views.Dialogs;
using System.Windows;

namespace CryptoJournal.Wpf.UI;

public interface IConfirmDialogService
{
    bool? Show(ConfirmDialogViewModel vm);
}

public sealed class ConfirmDialogService : IConfirmDialogService
{
    public bool? Show(ConfirmDialogViewModel vm)
    {
        var dlg = new ConfirmDialog
        {
            DataContext           = vm,
            Owner                 = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return dlg.ShowDialog(); // Result mapping: true = OK, false = Cancel, null = Dismissed
    }
}