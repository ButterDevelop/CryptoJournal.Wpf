using CryptoJournal.Wpf.UI;
using CryptoJournal.Wpf.ViewModels.Dialogs;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CryptoJournal.Wpf.Views.Dialogs;

public partial class AddTradeDialog : Window
{
    private readonly IConfirmService _confirm;

    public AddTradeDialog(AddTradeDialogViewModel vm, IConfirmService confirm)
    {
        InitializeComponent();
        DataContext = vm;
        _confirm    = confirm;
        Owner       = Application.Current.MainWindow;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AddTradeDialogViewModel vm)
            return;

        // Perform comprehensive transaction validation (time, balances, etc.)
        if (!vm.TryValidate(out var message))
        {
            // Display a user-friendly error dialog for unparseable time inputs
            if (message == "Invalid Time (UTC).")
            {
                await _confirm.InfoAsync(
                    header:  "Invalid time",
                    message: "Cannot parse the Time (UTC) value.",
                    lines:
                    [
                        new ConfirmLine("Example", "2026-01-12 14:35:00"),
                        new ConfirmLine("Example", "12.01.2026 14:35"),
                        new ConfirmLine("Example", "2026-01-12T14:35:00Z"),
                    ],
                    okText: "OK");

                return;
            }

            // Display standard error dialog for domain or balance validation failures
            await _confirm.InfoAsync(
                header:  "Invalid trade",
                message: message,
                lines:   [],
                okText:  "OK");

            return;
        }

        // Format the confirmed UTC time canonically before closing
        vm.TryGetTimeUtc(out var utc);
        vm.TimeUtcText = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        DialogResult = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Clipboard.ContainsImage() && DataContext is AddTradeDialogViewModel dvm)
            {
                if (dvm.PasteAttachmentCommand.CanExecute(null))
                {
                    dvm.PasteAttachmentCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key != Key.Escape) return;

        if (DataContext is AddTradeDialogViewModel vm && vm.IsTimePopupOpen)
        {
            vm.IsTimePopupOpen = false;
            e.Handled          = true; // Consume the key event to close the popup without dismissing the entire dialog
        }
    }

    private void TimePopupButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle popup state on mouse release to prevent immediate re-closing
        if (DataContext is AddTradeDialogViewModel vm && !vm.IsTimePopupOpen)
            vm.IsTimePopupOpen = true;
    }

    private void TimeCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        // Release mouse capture from the Calendar control to ensure the Apply button receives the next click
        if (Mouse.Captured != null)
            Mouse.Capture(null);

        // Programmatically shift focus to the Apply button after a date is selected
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyTimeButton.Focus();
            Keyboard.Focus(ApplyTimeButton);
        }), DispatcherPriority.Input);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}