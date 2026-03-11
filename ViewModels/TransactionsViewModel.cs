using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Storage.Portfolio;
using CryptoJournal.Wpf.UI;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using CryptoJournal.Wpf.Storage.Attachments;

namespace CryptoJournal.Wpf.ViewModels;

public sealed record TxTypeFilterItem(string Title, TxType? Value);

public partial class TransactionsViewModel : ObservableObject
{
    private sealed class Lot
    {
        public decimal Qty;
        public decimal Cost; // Total cost (including purchase fee) for this remaining quantity

        public Lot(decimal qty, decimal cost)
        {
            Qty  = qty;
            Cost = cost;
        }
    }

    private readonly IEnvironmentService _env;
    private readonly IPortfolioStore     _store;
    private readonly IServiceProvider    _services;

    public ObservableCollection<TradeFillRowVm> Rows      { get; } = [];
    public IEnumerable<TradeFill>               FillsSeq => Rows.Select(r => r.Fill);
    public ICollectionView                      FillsView { get; }

    // Filters
    public ObservableCollection<TxTypeFilterItem> TypeFilters { get; } = [];

    [ObservableProperty] private string?           symbolFilter;
    [ObservableProperty] private string?           noteFilter;
    [ObservableProperty] private TxTypeFilterItem? selectedTypeFilter;
    [ObservableProperty] private DateTime?         fromUtcDate;
    [ObservableProperty] private DateTime?         toUtcDate;

    [ObservableProperty] private TradeFillRowVm? selected;

    [ObservableProperty] private bool isLocked = true;

    public bool IsUnlocked => !IsLocked;

    public event EventHandler? FillsChanged;

    private readonly ICryptoIconCache _icons;
    private readonly ILocalImageStore _imageStore;

    public TransactionsViewModel(
        IEnvironmentService env,
        IPortfolioStore     store,
        IServiceProvider    services,
        ICryptoIconCache    icons,
        ILocalImageStore    imageStore)
    {
        _env        = env;
        _store      = store;
        _services   = services;
        _icons      = icons;
        _imageStore = imageStore;

        // Initialize the collection view with default sorting and filtering
        FillsView = CollectionViewSource.GetDefaultView(Rows);
        FillsView.SortDescriptions.Clear();
        FillsView.SortDescriptions.Add(new SortDescription(nameof(TradeFillRowVm.TimeUtc), ListSortDirection.Descending));
        FillsView.Filter = o => o is TradeFillRowVm r && PassesFilter(r);

        TypeFilters.Add(new TxTypeFilterItem("All", null));
        foreach (var t in Enum.GetValues<TxType>())
            TypeFilters.Add(new TxTypeFilterItem(TxTypeDisplayConverter.Format(t), t));

        // Set default filter state
        SelectedTypeFilter = TypeFilters[0];
    }

    private bool PassesFilter(TradeFillRowVm r)
    {
        var f = r.Fill;

        // Filter by transaction type
        var t = SelectedTypeFilter?.Value;
        if (t is not null && f.Type != t.Value) return false;

        // Filter by symbol name
        if (!string.IsNullOrWhiteSpace(SymbolFilter))
        {
            var q = SymbolFilter.Trim();
            if (!f.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Filter by user notes
        if (!string.IsNullOrWhiteSpace(NoteFilter))
        {
            var q = NoteFilter.Trim();
            if (string.IsNullOrWhiteSpace(f.Note) || !f.Note.Contains(q, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Apply inclusive date range filtering based on UTC value
        var dt = f.TimeUtc.UtcDateTime;

        if (FromUtcDate is not null)
        {
            var from = FromUtcDate.Value.Date; // 00:00
            if (dt < from) return false;
        }

        if (ToUtcDate is not null)
        {
            var toExclusive = ToUtcDate.Value.Date.AddDays(1); // next day 00:00
            if (dt >= toExclusive) return false;
        }

        return true;
    }

    private void RefreshFilters()
    {
        if (FillsView is IEditableCollectionView iecv && (iecv.IsAddingNew || iecv.IsEditingItem))
            return; // Prevent refresh while row is being edited

        FillsView.Refresh();
    }

    // Trigger collection refresh on filter change
    partial void OnSymbolFilterChanged(string? value)                 => RefreshFilters();
    partial void OnNoteFilterChanged(string? value)                   => RefreshFilters();
    partial void OnSelectedTypeFilterChanged(TxTypeFilterItem? value) => RefreshFilters();
    partial void OnFromUtcDateChanged(DateTime? value)                => RefreshFilters();
    partial void OnToUtcDateChanged(DateTime? value)                  => RefreshFilters();

    [RelayCommand]
    private void ClearFilters()
    {
        SymbolFilter = null;
        NoteFilter   = null;
        FromUtcDate  = null;
        ToUtcDate    = null;
        SelectedTypeFilter = TypeFilters[0];
        RefreshFilters();
    }

    private void RecomputeSellPnl()
    {
        var selectedId = Selected?.Id;

        // Track spot positions using FIFO logic per symbol
        var lotsBySymbol = new Dictionary<string, Queue<Lot>>(StringComparer.OrdinalIgnoreCase);

        // Track futures positions using FIFO logic per symbol and direction
        var futuresLots = new Dictionary<(string sym, bool isLong), Queue<FutLot>>();

        // Store calculated PnL results mapped by transaction ID
        var res = new Dictionary<Guid, (decimal? pnl, decimal? pct)>();

        foreach (var row in Rows.OrderBy(x => x.TimeUtc))
        {
            var f = row.Fill;

            if (string.IsNullOrWhiteSpace(f.Symbol)) continue;

            // Process Spot Transactions
            if (f.Type == TxType.Buy)
            {
                if (f.Quantity <= 0m || f.Price <= 0m) continue;
                var cost = (f.Quantity * f.Price) + f.FeeQuote;
                if (!lotsBySymbol.TryGetValue(f.Symbol, out var q))
                    lotsBySymbol[f.Symbol] = q = new Queue<Lot>();
                q.Enqueue(new Lot(f.Quantity, cost));
                continue;
            }

            if (f.Type == TxType.Sell)
            {
                if (f.Quantity <= 0m || f.Price <= 0m) { res[f.Id] = (null, null); continue; }
                if (!lotsBySymbol.TryGetValue(f.Symbol, out var lots) || lots.Count == 0) { res[f.Id] = (null, null); continue; }

                var sellQtyLeft    = f.Quantity;
                decimal costBasisSold = 0m;

                while (sellQtyLeft > 0m && lots.Count > 0)
                {
                    var lot = lots.Peek();
                    var take = Math.Min(lot.Qty, sellQtyLeft);
                    var portionCost = lot.Cost * (take / lot.Qty);
                    costBasisSold += portionCost;
                    lot.Qty  -= take;
                    lot.Cost -= portionCost;
                    sellQtyLeft -= take;
                    if (lot.Qty <= 0m) lots.Dequeue();
                }

                if (sellQtyLeft > 0m) { res[f.Id] = (null, null); continue; }

                var proceedsNet = (f.Quantity * f.Price) - f.FeeQuote;
                var pnl         = proceedsNet - costBasisSold;
                res[f.Id] = (pnl, costBasisSold > 0m ? pnl / costBasisSold : null);
                continue;
            }

            // Process Futures Open Transactions
            if (f.Type is TxType.OpenLong or TxType.OpenShort)
            {
                if (f.Quantity <= 0m || f.Price <= 0m) continue;
                var key = (f.Symbol.Trim().ToUpperInvariant(), f.Type == TxType.OpenLong);
                var lev = Math.Max(1m, f.Leverage);
                if (!futuresLots.TryGetValue(key, out var q))
                    futuresLots[key] = q = new Queue<FutLot>();
                q.Enqueue(new FutLot(f.Quantity, f.Price, lev));
                continue;
            }

            // Process Futures Close Transactions
            if (f.Type is TxType.CloseLong or TxType.CloseShort)
            {
                if (f.Quantity <= 0m || f.Price <= 0m) { res[f.Id] = (null, null); continue; }

                var isLong = f.Type == TxType.CloseLong;
                var key    = (f.Symbol.Trim().ToUpperInvariant(), isLong);

                if (!futuresLots.TryGetValue(key, out var lots) || lots.Count == 0) { res[f.Id] = (null, null); continue; }

                var qtyLeft      = f.Quantity;
                decimal pnlTotal = 0m;
                decimal margin   = 0m;  // Track applied margin to calculate Return on Equity (ROE)

                while (qtyLeft > 0m && lots.Count > 0)
                {
                    var lot  = lots.Peek();
                    var take = Math.Min(lot.Qty, qtyLeft);

                    decimal pnlUnit = isLong
                        ? f.Price - lot.Entry   // Calculate profit for long positions
                        : lot.Entry - f.Price;  // Calculate profit for short positions

                    pnlTotal += pnlUnit * take * lot.Lev;
                    margin   += take * lot.Entry / lot.Lev;

                    lot.Qty -= take;
                    if (lot.Qty <= 0m) lots.Dequeue();

                    qtyLeft -= take;
                }

                pnlTotal -= f.FeeQuote;

                // Handle edge case where closed quantity exceeds open quantity
                if (qtyLeft > 0m) { res[f.Id] = (null, null); continue; }

                res[f.Id] = (pnlTotal, margin > 0m ? pnlTotal / margin : null);
                continue;
            }
        }

        // Apply calculated PnL to respective rows
        foreach (var row in Rows)
        {
            var f = row.Fill;
            bool isCloseRow = f.Type is TxType.Sell or TxType.CloseLong or TxType.CloseShort;

            if (isCloseRow && res.TryGetValue(f.Id, out var r))
                row.UpdateSellPnl(r.pnl, r.pct);
            else
                row.ClearSellPnlIfAny();
        }

        if (selectedId is not null)
            Selected = Rows.FirstOrDefault(x => x.Id == selectedId);

        RefreshFilters();
    }

    public async Task LoadAsync()
    {
        var fills = await _store.LoadAsync();

        Rows.Clear();

        foreach (var f in fills.OrderBy(x => x.TimeUtc))
        {
            var row = new TradeFillRowVm(f, _imageStore);
            row.OnAttachmentsChanged = async () => 
            {
                await SaveAsync();
                FillsChanged?.Invoke(this, EventArgs.Empty);
            };
            Rows.Add(row);
        }

        RecomputeSellPnl();
        RefreshFilters();

        _ = WarmupIconsAsync(); // Preload crypto icons asynchronously
    }

    private async Task WarmupIconsAsync(CancellationToken ct = default)
    {
        var symbols = FillsSeq.Select(r => r.Symbol)
                              .Where(s => !string.IsNullOrWhiteSpace(s))
                              .Select(s => s.Trim().ToUpperInvariant())
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToList();

        foreach (var sym in symbols)
        {
            ImageSource? icon = null;
            try { icon = await _icons.GetAsync(sym, ct); }
            catch { /* don't really care about the exception */ }

            if (icon is null) continue;

            // Bind the active icon material to all relevant rows
            foreach (var r in Rows.Where(r => sym.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase)))
                r.SymbolIcon = icon;
        }
    }

    public async Task SaveAsync()
    {
        await _store.SaveAsync(FillsSeq.OrderBy(x => x.TimeUtc).ToList());
    }

    public async Task CommitEditsAsync()
    {
        RecomputeSellPnl();

        var dirtyRows = Rows.Where(r => r.IsSymbolDirty).ToList();

        await SaveAsync();

        foreach (var row in dirtyRows)
        {
            ImageSource? icon = null;
            try { icon = await _icons.GetAsync(row.Symbol); }
            catch { /* ignore */ }

            row.SymbolIcon = icon;
            row.ClearSymbolDirty();
        }

        FillsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleLock() => IsLocked = !IsLocked;

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private async Task AddTrade()
    {
        var dlg = _services.GetRequiredService<AddTradeDialog>();
        var dvm = (Dialogs.AddTradeDialogViewModel)dlg.DataContext;

        // Explicitly pass existing fills down to the dialog to handle unique validations
        dvm.SetExistingFills(FillsSeq.ToList());

        if (dlg.ShowDialog() == true)
        {
            var vm = (Dialogs.AddTradeDialogViewModel)dlg.DataContext;

            var fill = new TradeFill(
                Id:          Guid.NewGuid(),
                TimeUtc:     vm.TimeUtc,
                Type:        vm.Type,
                Symbol:      SymbolUtil.NormalizeBase(vm.Symbol, _env.Current.QuoteCurrency),
                Quantity:    vm.Quantity,
                Price:       vm.Price,
                FeeQuote:    vm.FeeQuote,
                Note:        vm.Note,
                Leverage:    vm.Leverage,
                TakeProfit:  vm.TakeProfit,
                StopLoss:    vm.StopLoss,
                Attachments: vm.Attachments.Count > 0 ? vm.Attachments.Select(a => a.Filename).ToList() : null
            );

            var row = new TradeFillRowVm(fill, _imageStore);
            row.OnAttachmentsChanged = async () =>
            {
                await SaveAsync();
                FillsChanged?.Invoke(this, EventArgs.Empty);
            };
            Rows.Add(row);

            _ = WarmupIconsAsync(); // Check if we need to load a new coin icon

            RecomputeSellPnl();

            // Refresh view to apply active filters and sorting to the new item
            RefreshFilters();

            await SaveAsync();
        }

        FillsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(IsUnlocked))]
    private async Task RemoveFill(TradeFillRowVm? row)
    {
        if (row is null) return;

        var vm = new Dialogs.ConfirmDialogViewModel
        {
            TitleText   = "Delete Transaction",
            HeaderText  = $"Delete {row.Type}?",
            MessageText = $"Are you sure you want to delete {row.Quantity:0.####} {row.Symbol} @ {row.Price:0.####}?",
            ConfirmText = "Delete",
            CancelText  = "Cancel"
        };

        var res = _services.GetRequiredService<IConfirmDialogService>().Show(vm);
        if (res != true) return;

        // Delete associated image files to avoid disk bloating
        if (row.Fill.Attachments is not null)
        {
            foreach (var att in row.Fill.Attachments)
            {
                try { _imageStore.DeleteImage(att); }
                catch { /* ignore */ }
            }
        }

        Rows.Remove(row);
        if (ReferenceEquals(Selected, row)) Selected = null;
        
        RecomputeSellPnl();
        RefreshFilters();
        await SaveAsync();
        
        FillsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Notify commands to re-evaluate their execution state when the lock toggles
    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUnlocked));
        AddTradeCommand.NotifyCanExecuteChanged();
        RemoveFillCommand.NotifyCanExecuteChanged();
    }
}

internal sealed class FutLot
{
    public decimal Qty;
    public decimal Entry;
    public decimal Lev;

    public FutLot(decimal qty, decimal entry, decimal lev)
    {
        Qty   = qty;
        Entry = entry;
        Lev   = lev;
    }
}