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
                foreach (var r in Positions.Where(p => sym.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase)))
                {
                    r.ApplyLastPrice(price);
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            // Price updates are best effort.
        }
    }

    partial void OnSelectedPositionChanged(PositionRowVm? value)
    {
        // Refreshing the collection temporarily clears its selection.
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
            var upgraded = ScenarioEditor.TryUpgradeAutoPlanToAth(symbol, ath);
            if (!upgraded) return;

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
            // Price updates are best effort.
        }
    }
    
    partial void OnSelectedPositionChanging(PositionRowVm? value)
    {
        if (_suppressSelectionGuard) return;

        if (_isRefreshingPositions && value is null)
            return;

        var old = selectedPosition;
        if (ReferenceEquals(old, value)) return;

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

        if (res is null)
        {
            RestoreOldSelection(old);
            return;
        }

        if (res.Value)
        {
            ScenarioEditor.SaveCommand.Execute(null);

            if (ScenarioEditor.HasUnsavedChanges || !string.IsNullOrWhiteSpace(ScenarioEditor.ErrorText))
            {
                RestoreOldSelection(old);
                return;
            }

            return;
        }

        ScenarioEditor.DiscardChanges();
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

        var posList = positions as IList<PositionSnapshot> ?? positions.ToList();

        var currentSymbols = posList.Where(p => p.Quantity > 0m && !string.IsNullOrWhiteSpace(p.Symbol))
                                    .Select(p => p.Symbol.Trim().ToUpperInvariant())
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

        var changed = ScenarioEditor.ReconcilePlansWithPositions(posList.AsReadOnly(), _transactionsVm.FillsSeq.ToList().AsReadOnly());

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

        var prevSymbol = SelectedPosition?.Symbol;

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
            using var gate = new SemaphoreSlim(initialCount: 4, maxCount: 4);

            var athTasks = symbolsNeedingPlan.Select(async sym =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var price = await _ath.GetAthOrLastAsync(sym, ct).ConfigureAwait(false);
                    return (Symbol: sym, Ath: price);
                }
                catch
                {
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

                if (ScenarioEditor.HasPlan(sym))
                    continue;

                if (!qtyBySymbol.TryGetValue(sym, out var baseQty) || baseQty <= 0m)
                    continue;

                ScenarioEditor.SetDefaultPlanNoSave(sym, baseQty, ath.Value);
                anyAdded = true;
            }

            if (anyAdded)
                ScenarioEditor.SavePlans();

            if (SelectedPosition is not null)
            {
                var sym = SelectedPosition.Symbol.Trim().ToUpperInvariant();

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

        var rows = posList.Select(p => new PositionRowVm(p)).ToList();

        _isRefreshingPositions = true;
        try
        {
            await _ui.InvokeAsync(() =>
            {
                Positions.Clear();
                foreach (var r in rows)
                    Positions.Add(r);

                if (!string.IsNullOrWhiteSpace(prevSymbol))
                {
                    var match = Positions.FirstOrDefault(p =>
                    prevSymbol.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase));

                    if (match is not null)
                    {
                        SelectedPosition = match; // prevents "flashing"
                    }
                }
            }, DispatcherPriority.DataBind, ct);
        }
        finally
        {
            _isRefreshingPositions = false;
        }

        var currentSelected = SelectedPosition;
        if (currentSelected is not null &&
            !Positions.Any(p => p.Symbol.Equals(currentSelected.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.InvokeAsync(() =>
            {
                SelectedPosition = null;
            }, DispatcherPriority.DataBind, ct);
        }

        var distinctSymbols = rows.Select(r => r.Symbol)
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Select(s => s.Trim().ToUpperInvariant())
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        _poll.SetSymbols(distinctSymbols);

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
