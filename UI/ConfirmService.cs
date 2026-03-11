using CryptoJournal.Wpf.ViewModels.Dialogs;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Media;

namespace CryptoJournal.Wpf.UI;

public sealed class ConfirmService : IConfirmService
{
    private readonly IServiceProvider _services;

    public ConfirmService(IServiceProvider services) => _services = services;

    public Task<bool> ConfirmAsync(string header, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false)
    {
        // Dialog instantiation mandates execution on the active UI thread.
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = _services.GetRequiredService<ConfirmDialog>();

            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current.MainWindow;

            dlg.Owner = owner;

            var accent = (Brush)new SolidColorBrush(Color.FromRgb(0x00, 0xC7, 0xD9));
            var danger = (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));

            dlg.DataContext = new ConfirmDialogViewModel
            {
                TitleText    = header,
                HeaderText   = header,
                MessageText  = message,
                ConfirmText  = confirmText,
                CancelText   = cancelText,
                ConfirmBrush = destructive ? danger : accent
            };

            return dlg.ShowDialog() == true;
        }).Task;
    }

    public Task InfoAsync(string header, string message, IEnumerable<ConfirmLine>? lines, string okText = "OK")
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = _services.GetRequiredService<ConfirmDialog>();
            dlg.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current.MainWindow;
    
            var accent = (Brush)new SolidColorBrush(Color.FromRgb(0x00, 0xC7, 0xD9));
    
            dlg.DataContext = new ConfirmDialogViewModel
            {
                TitleText    = header,
                HeaderText   = header,
                MessageText  = message,
                Lines        = lines?.ToList() ?? [],
                ConfirmText  = okText,
                CancelText   = "",
                ConfirmBrush = accent
            };
    
            dlg.ShowDialog();
        }).Task;
    }
}