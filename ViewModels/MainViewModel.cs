using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Services.Candles;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Services.Prices;
using CryptoJournal.Wpf.UI;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace CryptoJournal.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CandleSyncService       _candleSync;
    private readonly CancellationTokenSource _autoSyncCts = new();
    private readonly PriceService            _prices;
    private readonly IPortfolioCalculator    _calc;

    [ObservableProperty]
    private string windowTitle = "CryptoJournal by ButterDevelop";

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public DashboardViewModel    DashboardVm    { get; }
    public TransactionsViewModel TransactionsVm { get; }
    public PositionsViewModel    PositionsVm    { get; }
    public FuturesViewModel      FuturesVm      { get; }
    public AboutViewModel        AboutVm        { get; }

    private Task? _autoSyncTask;

    private readonly SemaphoreSlim   _recalcGate       = new(1, 1);
    private readonly HashSet<string> _athSynced        = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _recalcDebounceCts;

    private readonly IServiceProvider    _services;
    private readonly IEnvironmentService _env;

    public string CurrentEnvironmentLabel => $"{_env.Current.Name} ({_env.Current.QuoteCurrency})";

    public MainViewModel(
        IServiceProvider      services,
        IEnvironmentService   env,
        CandleSyncService     candleSync,
        PriceService          prices,
        IPortfolioCalculator  calc,
        DashboardViewModel    dash,
        TransactionsViewModel tx,
        PositionsViewModel    pos,
        FuturesViewModel      futures,
        AboutViewModel        about)
    {
        _services      = services;
        _env           = env;
        _env.CurrentChanged += async (_, __) =>
        {
            UpdateWindowTitle();
            OnPropertyChanged(nameof(CurrentEnvironmentLabel));
            await OnEnvironmentChangedAsync();
        };
        _candleSync    = candleSync;
        _prices        = prices;
        _calc          = calc;
        DashboardVm    = dash;
        TransactionsVm = tx;
        TransactionsVm.FillsChanged += (_, __) => RequestRecalc(withPrices: true);
        PositionsVm    = pos;
        FuturesVm      = futures;
        AboutVm        = about;

        UpdateWindowTitle();

        _ = InitializeAsync();

        var poll = _services.GetRequiredService<MarketDataPollingService>();
        poll.Start(TimeSpan.FromSeconds(30));
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;

        try
        {
            await _env.InitializeAsync();
            await TransactionsVm.LoadAsync();

            await RecalculateAsync(withPrices: true);

            TransactionsVm.FillsView.CollectionChanged += (_, __) => RequestRecalc(withPrices: true);

            _autoSyncTask ??= Task.Run(() => AutoSyncLoopAsync(_autoSyncCts.Token));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnEnvironmentChangedAsync()
    {
        IsLoading = true;

        try
        {
            ErrorMessage = null;
            await TransactionsVm.LoadAsync();
            await RecalculateAsync(withPrices: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateWindowTitle()
    {
        WindowTitle = $"CryptoJournal by ButterDevelop - {_env.Current.Name} ({_env.Current.QuoteCurrency})";
    }

    [RelayCommand]
    private void OpenEnvironments()
    {
        var dlg = _services.GetRequiredService<EnvironmentManagerDialog>();
        dlg.ShowDialog();
    }

    private void RequestRecalc(bool withPrices)
    {
        _recalcDebounceCts?.Cancel();
        _recalcDebounceCts = new CancellationTokenSource();
        var ct = _recalcDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, ct);
                await Application.Current.Dispatcher.InvokeAsync(() => RecalculateAsync(withPrices), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private async Task AutoSyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var symbols = TransactionsVm.FillsSeq
                    .Select(f => f.Symbol)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Where(s => !SymbolUtil.IsQuote(s, _env.Current.QuoteCurrency))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var sym in symbols)
                {
                    if (_athSynced.Add(sym))
                        await _candleSync.EnsureDailyHistoryForAthAsync("binance", SymbolUtil.ToPair(sym, _env.Current.QuoteCurrency), ct);
                }

                var op = Application.Current.Dispatcher.InvokeAsync(() => RecalculateAsync(withPrices: true));
                await op.Task.Unwrap();
            }
            catch
            {
                // The next sync cycle will retry transient market-data failures.
            }

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    private async Task RecalculateAsync(bool withPrices)
    {
        await _recalcGate.WaitAsync();

        try
        {
            var fills = TransactionsVm.FillsSeq.ToList();
            Dictionary<string, decimal>? marks = null;

            string quote = _env.Current.QuoteCurrency;

            if (withPrices)
            {
                var bases = fills
                    .Select(f => f.Symbol)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => SymbolUtil.NormalizeBase(s, quote))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var pairs = bases
                    .Where(b => !SymbolUtil.IsQuote(b, quote))
                    .Select(b => SymbolUtil.ToPair(b, quote))
                    .ToList();

                var pairMarks = await _prices.GetLastPricesAsync(pairs);

                marks = pairMarks.ToDictionary(
                    kv => SymbolUtil.NormalizeBase(kv.Key, quote),
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase);

                marks[quote] = 1m;
            }

            var snapshot = _calc.Calculate(fills, marks);

            await PositionsVm.SetPositionsAsync(snapshot.Positions);
            await FuturesVm.SetPositionsAsync(snapshot.FuturesPositions);
            await DashboardVm.UpdateAsync(snapshot, fills, marks, quote);
        }
        finally
        {
            _recalcGate.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _autoSyncCts.Cancel();
        _recalcDebounceCts?.Cancel();

        _autoSyncCts.Dispose();
        _recalcDebounceCts?.Dispose();
        _recalcGate.Dispose();
    }

    ~MainViewModel()
    {
        Dispose(false);
    }
}
