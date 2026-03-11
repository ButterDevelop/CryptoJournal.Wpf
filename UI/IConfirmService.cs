using CryptoJournal.Wpf.ViewModels.Dialogs;

namespace CryptoJournal.Wpf.UI;

public interface IConfirmService
{
    Task<bool> ConfirmAsync(
        string header,
        string message,
        string confirmText = "OK",
        string cancelText  = "Cancel",
        bool   destructive = false);

    Task InfoAsync(string header, string message, IEnumerable<ConfirmLine>? lines, string okText = "OK");
}