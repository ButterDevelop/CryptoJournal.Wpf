using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.Prices;
using CryptoJournal.Wpf.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace CryptoJournal.Wpf.ViewModels;

public partial class FuturesViewModel : ObservableObject
{
    private readonly MarketDataPollingService _poll;
    private readonly ICryptoIconCache         _icons;
    private readonly Dispatcher               _ui;
    private readonly IConfirmDialogService    _confirm;

    public ObservableCollection<FuturesPositionRowVm> Positions { get; } = [];
    public ScenarioEditorViewModel ScenarioEditor { get; }

    [ObservableProperty] private FuturesPositionRowVm? selectedPosition;

    private bool _isRefreshing;
    private bool _suppressSelectionGuard;

    public bool IsScenarioPanelOpen => SelectedPosition is not null;

    public FuturesViewModel(IServiceProvider         services,
                            MarketDataPollingService poll,
                            ICryptoIconCache         icons,
                            IConfirmDialogService    confirm)
    {
        // Dedicated (non-shared) ScenarioEditorViewModel for futures positions
        ScenarioEditor = services.GetRequiredService<ScenarioEditorViewModel>();
        _poll          = poll;
        _icons         = icons;
        _ui            = Application.Current.Dispatcher;
        _confirm       = confirm;

        ScenarioEditor.RequestCloseUi += () => ClearSelectedPositionCommand.Execute(null);

        _poll.LastPriceUpdated += OnLastPriceUpdated;
    }

    private async void OnLastPriceUpdated(string symbol, decimal price)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbol) || price <= 0m) return;
            var sym = symbol.Trim().ToUpperInvariant();

            await _ui.InvokeAsync(() =>
            {
                foreach (var r in Positions.Where(p => sym.Equals(p.Symbol, StringComparison.OrdinalIgnoreCase)))
                    r.ApplyMarkPrice(price);
            }, DispatcherPriority.Background);
        }
        catch { /* never throw from event handler */ }
    }

    partial void OnSelectedPositionChanged(FuturesPositionRowVm? value)
    {
        if (_isRefreshing && value is null) return;

        OnPropertyChanged(nameof(IsScenarioPanelOpen));

        if (value is null)
        {
            ScenarioEditor.Close();
            return;
        }

        // Open scenario editor using the composite key "SYMBOL:LONG" or "SYMBOL:SHORT"
        ScenarioEditor.OpenForPosition(value.ScenarioKey, value.Quantity, value.TakeProfit ?? 0m);
    }

    partial void OnSelectedPositionChanging(FuturesPositionRowVm? value)
    {
        if (_suppressSelectionGuard) return;
        if (_isRefreshing && value is null) return;

        var old = selectedPosition;
        if (ReferenceEquals(old, value)) return;
        if (!ScenarioEditor.HasUnsavedChanges) return;

        var sym = (ScenarioEditor.Symbol ?? old?.ScenarioKey ?? "").Trim().ToUpperInvariant();
        var vm = new Dialogs.ConfirmDialogViewModel
        {
            TitleText   = "Unsaved changes",
            HeaderText  = "Scenario has unsaved changes",
            MessageText = $"Save changes for {sym} before closing?",
            ConfirmText = "Save",
            CancelText  = "Discard"
        };

        var res = _confirm.Show(vm);
        if (res is null) { RestoreOldSelection(old); return; }
        if (res.Value)
        {
            ScenarioEditor.SaveCommand.Execute(null);
            if (ScenarioEditor.HasUnsavedChanges || !string.IsNullOrWhiteSpace(ScenarioEditor.ErrorText))
            { RestoreOldSelection(old); return; }
            return;
        }
        ScenarioEditor.DiscardChanges();
    }

    private void RestoreOldSelection(FuturesPositionRowVm? old)
    {
        _suppressSelectionGuard = true;
        try { SelectedPosition = old; }
        finally { _suppressSelectionGuard = false; }
    }

    [RelayCommand]
    private void ClearSelectedPosition() => SelectedPosition = null;

    public async Task SetPositionsAsync(IReadOnlyList<FuturesPosition> positions, CancellationToken ct = default)
    {
        var prevKey = SelectedPosition?.ScenarioKey;

        // Reconcile scenario plans for futures positions
        ScenarioEditor.ReconcileFuturesPlans(positions);

        // Auto-create default scenario plans for any new futures position
        foreach (var p in positions)
        {
            var keyStr = $"{p.Symbol}:{p.Side}";
            ScenarioEditor.EnsureDefaultFuturesPlan(keyStr, p.Quantity, p.TakeProfit ?? 0m);
        }

        var rows = positions.Select(p => new FuturesPositionRowVm(p)).ToList();
        
        // Load icons
        foreach (var r in rows)
        {
            var icon = await _icons.GetAsync(r.Symbol, ct);
            if (icon is not null) r.SymbolIcon = icon;
        }

        bool restoredSelection = false;
        _isRefreshing = true;

        try
        {
            await _ui.InvokeAsync(() =>
            {
                Positions.Clear();
                foreach (var r in rows) Positions.Add(r);

                if (!string.IsNullOrWhiteSpace(prevKey))
                {
                    var match = Positions.FirstOrDefault(p =>
                        prevKey.Equals(p.ScenarioKey, StringComparison.OrdinalIgnoreCase));

                    if (match is not null)
                    {
                        SelectedPosition  = match;
                        restoredSelection = true;
                    }
                }
            }, DispatcherPriority.DataBind, ct);
        }
        finally
        {
            _isRefreshing = false;
        }

        // Close panel if the selected position disappeared
        var currentSelected = SelectedPosition;
        if (currentSelected is not null &&
            !Positions.Any(p => p.ScenarioKey.Equals(currentSelected.ScenarioKey, StringComparison.OrdinalIgnoreCase)))
        {
            await _ui.InvokeAsync(() => { SelectedPosition = null; }, DispatcherPriority.DataBind, ct);
        }

        // Update polling symbols (no-op: futures symbols are already polled
        // by the global MarketDataPollingService which tracks them via fills)
    }
}
