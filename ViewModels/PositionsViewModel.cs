using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Candles;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.Prices;
using CryptoJournal.Wpf.UI;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CryptoJournal.Wpf.ViewModels;

public partial class PositionsViewModel : ObservableObject
{
    private readonly MarketDataPollingService _poll;

    private readonly ICryptoIconCache      _icons;
    private readonly Dispatcher            _ui;
    private readonly AthService            _ath;
    private readonly IConfirmDialogService _confirm;
    private TransactionsViewModel          _transactionsVm;

    public ObservableCollection<PositionRowVm> Positions { get; } = [];

    public ScenarioEditorViewModel ScenarioEditor { get; }

    [ObservableProperty] private PositionRowVm? selectedPosition;

    private bool _isRefreshingPositions;
    private bool _suppressSelectionGuard;

    public bool IsScenarioPanelOpen => SelectedPosition is not null;

    private HashSet<string> _prevPositionSymbols = new(StringComparer.OrdinalIgnoreCase);

    public PositionsViewModel(ICryptoIconCache      icons,   ScenarioEditorViewModel scenarioEditor, AthService ath, MarketDataPollingService poll,
                              IConfirmDialogService confirm, TransactionsViewModel   transactionsVm)
    {
        _icons          = icons;
        _ui             = Application.Current.Dispatcher;
        ScenarioEditor  = scenarioEditor;
        _ath            = ath;
        _confirm        = confirm;
        _transactionsVm = transactionsVm;

        ScenarioEditor.RequestCloseUi += () => ClearSelectedPositionCommand.Execute(null);

        _poll = poll;
        _poll.LastPriceUpdated += OnLastPriceUpdated;

        _ath.AthUpdated += OnAthUpdated;
    }

    private async void OnLastPriceUpdated(string symbol, decimal price)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbol)) return;
            if (price <= 0m) return;

            var sym = symbol.Trim().ToUpperInvariant();

            await _ui.InvokeAsync(() =>
            {
                // Update all rows matching this symbol
                foreach (var r in Positions.Where(p => sym.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase)))
                {
                    r.ApplyLastPrice(price);
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            // Never throw from async event handler
        }
    }

    partial void OnSelectedPositionChanged(PositionRowVm? value)
    {
        // IMPORTANT: If the table is currently being updated, ignore the temporary selection reset
        if (_isRefreshingPositions && value is null)
            return;

        OnPropertyChanged(nameof(IsScenarioPanelOpen));

        if (value is null)
        {
            ScenarioEditor.Close();
            return;
        }

        ScenarioEditor.OpenForPosition(value.Symbol, value.Quantity);
    }

    private async void OnAthUpdated(string symbol, decimal ath)
    {
        try
        {
            // Upgrade only "Auto:" plans
            var upgraded = ScenarioEditor.TryUpgradeAutoPlanToAth(symbol, ath);
            if (!upgraded) return;

            // If the currently selected position matches the symbol, reload editor on UI thread
            var selected = SelectedPosition;
            if (selected is null) return;

            if (!symbol.Equals(selected.Symbol?.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                return;

            await _ui.InvokeAsync(() =>
            {
                ScenarioEditor.OpenForPosition(selected.Symbol, selected.Quantity);
            }, DispatcherPriority.Background);
        }
        catch
        {
            // Never throw from async event handler
        }
    }
    
    partial void OnSelectedPositionChanging(PositionRowVm? value)
    {
        if (_suppressSelectionGuard) return;

        // Ignore temporary selection drop while refreshing
        if (_isRefreshingPositions && value is null)
            return;

        var old = selectedPosition; // backing field (old value)
        if (ReferenceEquals(old, value)) return;

        // If scenario editor is not open or nothing dirty, allow navigation.
        if (!ScenarioEditor.HasUnsavedChanges)
            return;

        var sym = (ScenarioEditor.Symbol ?? old?.Symbol ?? "").Trim().ToUpperInvariant();

        var vm = new Dialogs.ConfirmDialogViewModel
        {
            TitleText   = "Unsaved changes",
            HeaderText  = "Scenario has unsaved changes",
            MessageText = $"Save changes for {sym} before closing?",
            ConfirmText = "Save",
            CancelText  = "Discard"
        };

        var res = _confirm.Show(vm);

        // Window closed -> cancel navigation (restore old selection)
        if (res is null)
        {
            RestoreOldSelection(old);
            return;
        }

        // Save
        if (res.Value)
        {
            ScenarioEditor.SaveCommand.Execute(null);

            // If save failed (validation error), keep editor open and cancel navigation
            if (ScenarioEditor.HasUnsavedChanges || !string.IsNullOrWhiteSpace(ScenarioEditor.ErrorText))
            {
                RestoreOldSelection(old);
                return;
            }

            return; // allow navigation
        }

        // Discard
        ScenarioEditor.DiscardChanges();
        // allow navigation
    }

    private void RestoreOldSelection(PositionRowVm? old)
    {
        _suppressSelectionGuard = true;
        try
        {
            SelectedPosition = old;
        }
        finally
        {
            _suppressSelectionGuard = false;
        }
    }

    [RelayCommand]
    private void ClearSelectedPosition()
    {
        SelectedPosition = null;
    }

    public async Task SetPositionsAsync(IEnumerable<PositionSnapshot> positions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(positions);

        // Snapshot the input to avoid multiple enumeration and to keep a stable view of "current positions"
        var posList = positions as IList<PositionSnapshot> ?? positions.ToList();

        var currentSymbols = posList.Where(p => p.Quantity > 0m && !string.IsNullOrWhiteSpace(p.Symbol))
                                    .Select(p => p.Symbol.Trim().ToUpperInvariant())
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

        // 0) reconcile scenarios with new position sizes (sell/withdraw)
        var changed = ScenarioEditor.ReconcilePlansWithPositions(posList.AsReadOnly(), _transactionsVm.FillsSeq.ToList().AsReadOnly());

        // if a scenario is currently open for the selected position, reload it (so that the legs are updated)
        if (SelectedPosition is not null &&
            changed.Any(s => s.Equals(SelectedPosition.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.InvokeAsync(() =>
            {
                ScenarioEditor.OpenForPosition(
                    SelectedPosition.Symbol.Trim().ToUpperInvariant(),
                    SelectedPosition.Quantity);
            }, DispatcherPriority.Background, ct);
        }

        // Remember currently selected symbol so we can restore selection after repopulating the DataGrid
        var prevSymbol = SelectedPosition?.Symbol;

        // ------------------------------------------------------------
        // 1) Auto-create default scenario plans (only if a plan does NOT exist yet)
        //    Default: IsPercentMode = true, one leg: 100% @ ATH
        // ------------------------------------------------------------
        var symbolsNeedingPlan = currentSymbols.Where(sym => !_prevPositionSymbols.Contains(sym))
                                               .Where(sym => !ScenarioEditor.HasPlan(sym))
                                               .ToList();

        var qtyBySymbol = posList.Where(p => p.Quantity > 0m && !string.IsNullOrWhiteSpace(p.Symbol))
                                 .ToDictionary(
                                     p => p.Symbol.Trim().ToUpperInvariant(),
                                     p => p.Quantity,
                                     StringComparer.OrdinalIgnoreCase);

        if (symbolsNeedingPlan.Count > 0)
        {
            // Fetch ATH with limited concurrency to avoid spamming network / API
            using var gate = new SemaphoreSlim(initialCount: 4, maxCount: 4);

            var athTasks = symbolsNeedingPlan.Select(async sym =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // AthService returns decimal? in your Dashboard sample
                    var price = await _ath.GetAthOrLastAsync(sym, ct).ConfigureAwait(false);
                    return (Symbol: sym, Ath: price);
                }
                catch
                {
                    // Ignore transient errors (network, missing data, etc.)
                    return (Symbol: sym, Ath: null);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            var athResults = await Task.WhenAll(athTasks).ConfigureAwait(false);

            var anyAdded = false;

            foreach (var (sym, ath) in athResults)
            {
                ct.ThrowIfCancellationRequested();

                if (ath is null || ath.Value <= 0m)
                    continue;

                // Race protection: the plan might have appeared while ATH tasks were running
                if (ScenarioEditor.HasPlan(sym))
                    continue;

                if (!qtyBySymbol.TryGetValue(sym, out var baseQty) || baseQty <= 0m)
                    continue;

                ScenarioEditor.SetDefaultPlanNoSave(sym, baseQty, ath.Value);
                anyAdded = true;
            }

            // Persist once (avoid saving JSON per symbol)
            if (anyAdded)
                ScenarioEditor.SavePlans();

            // if just created a default plan for the currently opened symbol,
            // reload the editor so the user sees it immediately (no app restart needed)
            if (SelectedPosition is not null)
            {
                var sym = SelectedPosition.Symbol.Trim().ToUpperInvariant();

                // reload only if editor is open for this symbol and still empty
                if (ScenarioEditor.Symbol is not null &&
                    sym.Equals(ScenarioEditor.Symbol.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) &&
                    ScenarioEditor.Legs.Count == 0)
                {
                    await _ui.InvokeAsync(() =>
                    {
                        ScenarioEditor.OpenForPosition(sym, SelectedPosition.Quantity);
                    }, DispatcherPriority.Background, ct);
                }
            }
        }

        // ------------------------------------------------------------
        // 2) Build row VMs and refresh the UI collection
        // ------------------------------------------------------------
        var rows = posList.Select(p => new PositionRowVm(p)).ToList();

        bool restoredSelection = false;

        _isRefreshingPositions = true;
        try
        {
            await _ui.InvokeAsync(() =>
            {
                Positions.Clear();
                foreach (var r in rows)
                    Positions.Add(r);

                // Try to restore selection by symbol so the scenario panel stays open
                if (!string.IsNullOrWhiteSpace(prevSymbol))
                {
                    var match = Positions.FirstOrDefault(p =>
                    prevSymbol.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase));

                    if (match is not null)
                    {
                        SelectedPosition  = match; // prevents "flashing"
                        restoredSelection = true;
                    }
                }
            }, DispatcherPriority.DataBind, ct);
        }
        finally
        {
            _isRefreshingPositions = false;
        }

        // If the previously selected position no longer exists in the new list, clear selection.
        // This also handles the case where prevSymbol was null (e.g. when all positions disappear).
        var currentSelected = SelectedPosition;
        if (currentSelected is not null &&
            !Positions.Any(p => p.Symbol.Equals(currentSelected.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.InvokeAsync(() =>
            {
                SelectedPosition = null;
            }, DispatcherPriority.DataBind, ct);
        }

        // ------------------------------------------------------------
        // 3) Load icons asynchronously and apply them on the UI thread
        // ------------------------------------------------------------
        var distinctSymbols = rows.Select(r => r.Symbol)
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Select(s => s.Trim().ToUpperInvariant())
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        _poll.SetSymbols(distinctSymbols);

        // Optionally trigger ATH prefetch for all active symbols once
        foreach (var s in distinctSymbols)
            _ath.PrefetchAth(s, CancellationToken.None);

        var iconTasks = distinctSymbols.ToDictionary(
            s => s,
            s => _icons.GetAsync(s, ct),
            StringComparer.OrdinalIgnoreCase);

        foreach (var kv in iconTasks)
        {
            ct.ThrowIfCancellationRequested();

            ImageSource? icon = null;
            try { icon = await kv.Value; }
            catch { /* ignore: no icon / network down */ }

            if (icon is null) continue;

            var sym = kv.Key;

            await _ui.InvokeAsync(() =>
            {
                foreach (var r in rows.Where(r => sym.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase)))
                    r.SymbolIcon = icon;
            }, DispatcherPriority.Background, ct);
        }

        _prevPositionSymbols = new HashSet<string>(currentSymbols, StringComparer.OrdinalIgnoreCase);
    }
}