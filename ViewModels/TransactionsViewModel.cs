using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Portfolio;
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
    private readonly IEnvironmentService      _env;
    private readonly IPortfolioStore          _store;
    private readonly IServiceProvider         _services;
    private readonly TransactionPnlCalculator _pnlCalculator;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string? validationMessage;

    public bool IsUnlocked => !IsLocked;
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);

    public event EventHandler? FillsChanged;

    private readonly ICryptoIconCache _icons;
    private readonly ILocalImageStore _imageStore;

    public TransactionsViewModel(
        IEnvironmentService      env,
        IPortfolioStore          store,
        IServiceProvider         services,
        ICryptoIconCache         icons,
        ILocalImageStore         imageStore,
        TransactionPnlCalculator pnlCalculator)
    {
        _env           = env;
        _store         = store;
        _services      = services;
        _icons         = icons;
        _imageStore    = imageStore;
        _pnlCalculator = pnlCalculator;

        FillsView = CollectionViewSource.GetDefaultView(Rows);
        FillsView.SortDescriptions.Clear();
        FillsView.SortDescriptions.Add(new SortDescription(nameof(TradeFillRowVm.TimeUtc), ListSortDirection.Descending));
        FillsView.SortDescriptions.Add(new SortDescription(nameof(TradeFillRowVm.Id), ListSortDirection.Descending));
        FillsView.Filter = o => o is TradeFillRowVm r && PassesFilter(r);

        TypeFilters.Add(new TxTypeFilterItem("All", null));
        foreach (var t in Enum.GetValues<TxType>())
            TypeFilters.Add(new TxTypeFilterItem(TxTypeDisplayConverter.Format(t), t));

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

    private IReadOnlyList<LedgerIssue> RecomputeSellPnl()
    {
        var selectedId = Selected?.Id;
        var calculation = _pnlCalculator.Calculate(FillsSeq.ToList());

        foreach (var row in Rows)
        {
            var f = row.Fill;
            bool isCloseRow = f.Type is TxType.Sell or TxType.CloseLong or TxType.CloseShort;

            if (isCloseRow && calculation.ByTransactionId.TryGetValue(f.Id, out var result))
                row.UpdateSellPnl(result.RealizedPnlQuote, result.RealizedPnlPct);
            else
                row.ClearSellPnlIfAny();
        }

        if (selectedId is not null)
            Selected = Rows.FirstOrDefault(x => x.Id == selectedId);

        RefreshFilters();
        return calculation.Issues;
    }

    private static string? FormatIssues(IReadOnlyList<LedgerIssue> issues)
    {
        if (issues.Count == 0)
            return null;

        var first = issues[0].Message;
        return issues.Count == 1 ? first : $"{first} (+{issues.Count - 1} more)";
    }

    public async Task LoadAsync()
    {
        var fills = await _store.LoadAsync();

        Rows.Clear();

        foreach (var f in fills.OrderBy(fill => fill, TradeFillChronologicalComparer.Instance))
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
        ValidationMessage = null;
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
            catch { /* Icons are optional. */ }

            if (icon is null) continue;

            // Bind the active icon material to all relevant rows
            foreach (var r in Rows.Where(r => sym.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase)))
                r.SymbolIcon = icon;
        }
    }

    public async Task SaveAsync()
    {
        await _store.SaveAsync(FillsSeq
            .OrderBy(fill => fill, TradeFillChronologicalComparer.Instance)
            .ToList());
    }

    public async Task CommitEditsAsync()
    {
        var issues = RecomputeSellPnl();
        var unresolvedIssues = issues.Where(issue => !issue.IsReconciled).ToList();
        if (unresolvedIssues.Count > 0)
        {
            ValidationMessage = FormatIssues(unresolvedIssues);
            return;
        }

        ValidationMessage = null;

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

            var issues = RecomputeSellPnl();
            var newIssues = issues.Where(issue => issue.TransactionId == fill.Id).ToList();
            if (newIssues.Count > 0)
            {
                var validationMessage = FormatIssues(newIssues);
                Rows.Remove(row);
                RecomputeSellPnl();
                ValidationMessage = validationMessage;
                return;
            }

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
        ValidationMessage = null;
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
