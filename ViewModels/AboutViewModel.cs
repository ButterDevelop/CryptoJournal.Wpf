using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Services.Updates;
using CryptoJournal.Wpf.UI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace CryptoJournal.Wpf.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IConfirmService _confirm;
    private readonly string _currentVersion = GetCurrentVersion();
    private UpdateInfo? _availableUpdate;

    public AboutViewModel(IUpdateService updates, IConfirmService confirm)
    {
        _updates = updates;
        _confirm = confirm;
        VersionText = $"Version {_currentVersion}";
        _ = CheckForUpdatesAsync();
    }

    public string AppName { get; } = "CryptoJournal.Wpf";
    public string VersionText { get; }
    public string RepoUrl { get; } = "https://github.com/ButterDevelop/CryptoJournal.Wpf";

    public string ShortDescription { get; } =
        "A local journal for tracking spot and futures trades, positions, PnL, and exit scenarios.";

    public ObservableCollection<string> Features { get; } =
    [
        "Transactions: Buy / Sell / Deposit / Withdraw / Fee",
        "Futures trading: Long / Short with leverage support",
        "Positions: Market Value, Unrealized PnL and %",
        "Dashboard: Allocation chart + PnL bar (Realized vs Unrealized)",
        "ATH overview table + auto scenario defaults (100% @ ATH)",
        "Attachments: Add or paste screenshot images to transactions",
        "Scenarios: Plan take-profit legs to partially close positions",
        "Candle cache stored on disk (chunked, indexed)",
        "Public market data (no API keys required)"
    ];

    public IReadOnlyList<string> UpcomingFeatures { get; } =
    [
        "More exchanges and better symbol mapping",
        "Price and ATH improvements",
        "Export and import improvements"
    ];

    public string DonateUrl { get; } = "https://nowpayments.io/donation/butterdevelop";

    public string DonateText { get; } =
        "This app is free and open-source. If you find it useful, you can support development via donation.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool isCheckingForUpdates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool isInstallingUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool isUpdateAvailable;
    [ObservableProperty] private string updateStatus = "Check GitHub for a new release.";
    [ObservableProperty] private string installUpdateText = "Download and install";

    private bool CanCheckForUpdates() => !IsCheckingForUpdates && !IsInstallingUpdate;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        IsUpdateAvailable    = false;
        UpdateStatus         = "Checking for updates...";

        try
        {
            _availableUpdate = await _updates.CheckAsync(_currentVersion);
            if (_availableUpdate is null)
            {
                UpdateStatus = "You are using the latest version.";
                return;
            }

            IsUpdateAvailable = true;
            InstallUpdateText = $"Download and install {_availableUpdate.Version}";
            UpdateStatus      = $"Version {_availableUpdate.Version} is available.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanInstallUpdate() =>
        IsUpdateAvailable && !IsCheckingForUpdates && !IsInstallingUpdate;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        var update = _availableUpdate;
        if (update is null) return;

        var confirmed = await _confirm.ConfirmAsync(
            "Install update",
            $"Download and install CryptoJournal {update.Version}? The application will close before setup starts.",
            "Install",
            "Cancel");
        if (!confirmed) return;

        IsInstallingUpdate = true;
        UpdateStatus       = $"Downloading CryptoJournal {update.Version}...";

        try
        {
            var installerPath = await _updates.DownloadAsync(update);
            (Application.Current as App)?.PrepareForUpdate();
            Process.Start(new ProcessStartInfo(installerPath)
            {
                Arguments       = "/CLOSEAPPLICATIONS",
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not install the update: {ex.Message}";
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenRepo() => OpenUrl(RepoUrl);

    [RelayCommand]
    private void CopyRepoUrl() => CopyToClipboard(RepoUrl);

    [RelayCommand]
    private void OpenDonate() => OpenUrl(DonateUrl);

    [RelayCommand]
    private void CopyDonateUrl() => CopyToClipboard(DonateUrl);

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch { }
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion;

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
