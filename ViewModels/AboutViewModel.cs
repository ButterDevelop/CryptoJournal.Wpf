using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace CryptoJournal.Wpf.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName     { get; } = "CryptoJournal.Wpf";

    public string VersionText { get; } = BuildVersionText();

    public string RepoUrl     { get; } = "https://github.com/ButterDevelop/CryptoJournal.Wpf"; 

    public string ShortDescription { get; } =
        "A simple spot portfolio journal for tracking transactions, positions, PnL and different scenarios.";

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
        "More exchanges / better symbol mapping",
        "Price/ATH improvements and caching",
        "Export / import improvements"
    ];

    public string DonateUrl  { get; } = "https://nowpayments.io/donation/butterdevelop";

    public string DonateText { get; } =
        "This app is free and open-source. If you find it useful, you can support development via donation.";

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
        catch
        {
            // ignore
        }
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // ignore
        }
    }

    private static string BuildVersionText()
    {
        var asm = Assembly.GetExecutingAssembly();

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return $"Version {info}";

        var ver = asm.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(ver) ? "Version unknown" : $"Version {ver}";
    }
}