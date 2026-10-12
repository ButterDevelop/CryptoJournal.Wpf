using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Storage.Scenarios;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace CryptoJournal.Wpf.ViewModels;

public partial class ScenarioEditorViewModel : ObservableObject
{
    private readonly IScenarioStore      _store;
    private readonly IEnvironmentService _env;

    [ObservableProperty] private string? symbol;
    [ObservableProperty] private decimal positionQty;
    [ObservableProperty] private bool    isPercentMode = true;
    [ObservableProperty] private string? errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool isDirty;

    public bool HasUnsavedChanges => IsDirty;

    public ObservableCollection<ScenarioLegVm> Legs { get; } = [];

    public event Action<string>? ScenariosChanged;

    /// <summary>
    /// Event fired when the user clicks the close ('X') button in the UI.
    /// The parent viewmodel can listen to this to hide the panel.
    /// </summary>
    public event Action? RequestCloseUi;

    public string  HeaderTitle    => Symbol is null ? "Scenario" : $"{Symbol} — Sell plan";
    public string  HeaderSubtitle => Symbol is null ? "" : "Define target sells (legs)";

    public decimal PlannedQty     => Legs.Sum(GetEffectiveQty);
    public decimal RemainingQty   => PositionQty - PlannedQty;

    private bool   _suppressRecalc;
    private string _loadedSig = "";
    private bool   _loadedWasAuto;
    private bool   _suppressDirty;

    private readonly HashSet<ScenarioLegVm> _trackedLegs = [];

    public ScenarioEditorViewModel(IScenarioStore store, IEnvironmentService env)
    {
        _store = store;
        _env   = env;

        _store.Load(_env.Current.Id);

        _env.CurrentChanged += (_, __) => _store.Load(_env.Current.Id);

        Legs.CollectionChanged += Legs_CollectionChanged;
    }

    private void Legs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var l in _trackedLegs)
                l.PropertyChanged -= Leg_PropertyChanged;

            _trackedLegs.Clear();

            Recalc();
            UpdateDirty();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var x in e.OldItems.OfType<ScenarioLegVm>().Where(_trackedLegs.Remove))
            {
                x.PropertyChanged -= Leg_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var x in e.NewItems.OfType<ScenarioLegVm>().Where(_trackedLegs.Add))
            {
                x.PropertyChanged += Leg_PropertyChanged;
            }
        }

        Recalc();
        UpdateDirty();
    }

    private void Leg_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressRecalc) return;

        Recalc();

        if (_suppressDirty) return;
        UpdateDirty();
    }

    private void CaptureLoadedStateFromUi()
    {
        _loadedSig = CaptureSignature();
        IsDirty = false;
    }

    private void UpdateDirty()
    {
        if (Symbol is null)
        {
            IsDirty = false;
            return;
        }

        var cur = CaptureSignature();
        IsDirty = !string.Equals(cur, _loadedSig, StringComparison.Ordinal);
    }

    private string CaptureSignature()
    {
        var sb = new StringBuilder();
        sb.Append(IsPercentMode ? "1" : "0");
        sb.Append('|').Append(Legs.Count);

        foreach (var l in Legs)
        {
            sb.Append('|').Append(l.InputAmount.ToString("G29", CultureInfo.InvariantCulture));
            sb.Append('|').Append(l.TargetPrice.ToString("G29", CultureInfo.InvariantCulture));
            sb.Append('|').Append((l.Note ?? "").Trim());
        }

        return sb.ToString();
    }

    private static bool IsAutoDefaultPlan(ScenarioPlanDto? plan)
    {
        if (plan is null)                              return false;
        if (!plan.IsPercentMode)                       return false;
        if (plan.Legs is null || plan.Legs.Count != 1) return false;

        var leg = plan.Legs[0];
        if (leg.InputAmount != 100m) return false;
        if (leg.TargetPrice <= 0m)   return false;

        var note = (leg.Note ?? "").Trim();
        return note.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase);
    }

    public void DiscardChanges()
    {
        if (Symbol is null) return;
        OpenForPosition(Symbol, PositionQty);
    }

    public bool EnsureDefaultPlan(string symbol, decimal positionQty, decimal? athPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        if (positionQty <= 0) return false;

        if (_store.TryGetPlan(symbol) is not null)
            return false;

        var price = athPrice ?? 0m;
        if (price <= 0) return false;

        var plan = new ScenarioPlanDto
        {
            Symbol        = symbol.Trim().ToUpperInvariant(),
            IsPercentMode = true,
            Legs          =
            [
                new ScenarioLegDto
                {
                    InputAmount = 100m,
                    TargetPrice = price,
                    Note        = "Auto: 100% @ ATH"
                }
            ]
        };

        _store.SetPlan(plan);
        _store.Save();
        return true;
    }

    public bool EnsureDefaultFuturesPlan(string symbol, decimal positionQty, decimal defaultTargetPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        if (positionQty <= 0) return false;

        if (_store.TryGetPlan(symbol) is not null)
            return false;

        var plan = new ScenarioPlanDto
        {
            Symbol        = symbol.Trim().ToUpperInvariant(),
            IsPercentMode = true,
            BaseQty       = positionQty,
            Legs          =
            [
                new ScenarioLegDto
                {
                    InputAmount = 100m,
                    TargetPrice = defaultTargetPrice,
                    Note        = "Auto: 100% at Take Profit"
                }
            ]
        };

        _store.SetPlan(plan);
        _store.Save();

        ScenariosChanged?.Invoke(symbol);
        _store.NotifyScenariosChanged(symbol);
        return true;
    }

    public bool HasPlan(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var p = _store.TryGetPlan(symbol);
        return p is not null && p.Legs.Count > 0;
    }

    public void SetDefaultPlanNoSave(string symbol, decimal positionQty, decimal athPrice)
    {
        var plan = new ScenarioPlanDto
        {
            Symbol        = symbol.Trim().ToUpperInvariant(),
            IsPercentMode = true,
            BaseQty       = positionQty,
            Legs          =
            [
                new ScenarioLegDto
                {
                    InputAmount = 100m,
                    TargetPrice = athPrice,
                    Note        = "Auto: 100% @ ATH"
                }
            ]
        };

        _store.SetPlan(plan);
    }

    public void SavePlans() => _store.Save();

    public void OpenForPosition(string symbol, decimal positionQty, decimal defaultTargetPrice = 0m)
    {
        Symbol      = symbol;
        PositionQty = positionQty;

        Legs.Clear();

        var plan = _store.TryGetPlan(symbol);
        _loadedWasAuto = IsAutoDefaultPlan(plan);

        if (plan is not null)
        {
            IsPercentMode = plan.IsPercentMode;
            foreach (var leg in plan.Legs)
                Legs.Add(new ScenarioLegVm
                {
                    InputAmount = leg.InputAmount,
                    TargetPrice = leg.TargetPrice,
                    Note        = leg.Note
                });
        }
        else if ((symbol ?? "").Contains(':'))
        {
            IsPercentMode = true;
            Legs.Add(new ScenarioLegVm
            {
                InputAmount = 100m,
                TargetPrice = defaultTargetPrice,
                Note        = "Auto: 100% at Take Profit"
            });
        }

        Recalc();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));

        CaptureLoadedStateFromUi();
    }

    [RelayCommand]
    private void CloseUi()
    {
        RequestCloseUi?.Invoke();
    }

    public void Close()
    {
        Symbol      = null;
        PositionQty = 0;
        Legs.Clear();
        ErrorText   = null;

        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        Recalc();
    }

    public bool TryUpgradeAutoPlanToAth(string symbol, decimal ath)
    {
        if (string.IsNullOrWhiteSpace(symbol) || ath <= 0m)
            return false;

        symbol = symbol.Trim().ToUpperInvariant();

        var plan = _store.TryGetPlan(symbol);
        if (plan is null) return false;

        if (!plan.IsPercentMode) return false;
        if (plan.Legs is null || plan.Legs.Count != 1) return false;

        var leg = plan.Legs[0];

        if (leg.InputAmount != 100m) return false;
        if (string.IsNullOrWhiteSpace(leg.Note) || !leg.Note.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (leg.TargetPrice == ath) return false;

        leg.TargetPrice = ath;

        _store.SetPlan(plan);
        _store.Save();

        ScenariosChanged?.Invoke(symbol);
        _store.NotifyScenariosChanged(symbol);

        return true;
    }

    public ScenarioPlanDto? TryGetPlanPublic(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        return _store.TryGetPlan(symbol.Trim().ToUpperInvariant());
    }

    [RelayCommand]
    private void AddLeg()
    {
        Legs.Add(new ScenarioLegVm { InputAmount = 0, TargetPrice = 0, Note = "" });
        Recalc();

        if (Symbol is not null)
        {
            ScenariosChanged?.Invoke(Symbol);
            _store.NotifyScenariosChanged(Symbol);
        }
    }

    [RelayCommand]
    private void RemoveLeg(ScenarioLegVm leg)
    {
        Legs.Remove(leg);
        Recalc();

        if (Symbol is not null)
        {
            ScenariosChanged?.Invoke(Symbol);
            _store.NotifyScenariosChanged(Symbol);
        }
    }

    [RelayCommand]
    private void ClosePanel()
    {
        Close();
    }

    [RelayCommand]
    private void Save()
    {
        if (Symbol is null) return;

        Recalc();
        if (!string.IsNullOrWhiteSpace(ErrorText))
            return;

        if (_loadedWasAuto && IsDirty && Legs.Count == 1)
        {
            var leg  = Legs[0];
            var note = (leg.Note ?? "").Trim();

            if (note.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase))
            {
                _suppressDirty = true;
                try
                {
                    leg.Note = string.Empty;
                }
                finally
                {
                    _suppressDirty = false;
                }
            }
        }

        var plan = new ScenarioPlanDto
        {
            Symbol        = Symbol.Trim().ToUpperInvariant(),
            IsPercentMode = IsPercentMode,
            BaseQty       = PositionQty,
            Legs          = Legs.Select(l => new ScenarioLegDto
            {
                InputAmount = l.InputAmount,
                TargetPrice = l.TargetPrice,
                Note        = l.Note,
            }).ToList()
        };

        _store.SetPlan(plan);
        _store.Save();

        _loadedWasAuto = false;

        CaptureLoadedStateFromUi();

        ScenariosChanged?.Invoke(Symbol);
        _store.NotifyScenariosChanged(Symbol);
    }

    partial void OnIsPercentModeChanging(bool value)
    {
        var oldMode = isPercentMode;

        if (oldMode == value) return;
        if (Symbol is null)   return;
        if (PositionQty <= 0) return;
        if (Legs.Count  == 0) return;

        _suppressRecalc = true;
        _suppressDirty  = true;
        try
        {
            foreach (var leg in Legs)
            {
                var x = leg.InputAmount;
                if (x == 0) continue;

                if (value)
                    leg.InputAmount = Round8(x / PositionQty * 100m);
                else
                    leg.InputAmount = Round8(PositionQty * x / 100m);
            }
        }
        finally
        {
            _suppressDirty  = false;
            _suppressRecalc = false;
        }
    }

    partial void OnIsPercentModeChanged(bool value)
    {
        if (!_suppressRecalc)
            Recalc();

        if (!_suppressDirty)
            UpdateDirty();

        if (Symbol is not null)
        {
            ScenariosChanged?.Invoke(Symbol);
            _store.NotifyScenariosChanged(Symbol);
        }
    }

    public IReadOnlyList<string> ReconcilePlansWithPositions(IReadOnlyList<PositionSnapshot> positions, IReadOnlyList<TradeFill> fills)
    {
        var changed = new List<string>();

        var qtyBySymbol = positions.Where(p => !string.IsNullOrWhiteSpace(p.Symbol))
                                   .ToDictionary(
                                       p => p.Symbol.Trim().ToUpperInvariant(),
                                       p => p.Quantity,
                                       StringComparer.OrdinalIgnoreCase);

        var plans = _store.GetPlansSnapshot()
                          .Where(p => !(p.Symbol ?? "").Contains(':'))
                          .ToList();

        const decimal EPS = 0.00000001m;

        foreach (var plan in plans)
        {
            var sym = (plan.Symbol ?? "").Trim().ToUpperInvariant();
            if (sym.Length == 0) continue;

            qtyBySymbol.TryGetValue(sym, out var currentQty);

            if (currentQty <= EPS)
            {
                if (_store.RemovePlan(sym))
                    changed.Add(sym);
                continue;
            }

            if (plan.BaseQty <= EPS)
            {
                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);
                _store.SetPlan(plan);
                changed.Add(sym);
                continue;
            }

            if (currentQty > plan.BaseQty + EPS)
            {
                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);
                _store.SetPlan(plan);
                changed.Add(sym);
                continue;
            }

            if (currentQty < plan.BaseQty - EPS)
            {
                var soldQty = plan.BaseQty - currentQty;

                ConsumeFromPlan(plan, soldQty, plan.BaseQty);

                plan.Legs = plan.Legs.Where(l => l.InputAmount > EPS).ToList();

                if (plan.Legs.Count == 0)
                {
                    _store.RemovePlan(sym);
                    changed.Add(sym);
                    continue;
                }

                if (plan.IsPercentMode)
                {
                    foreach (var leg in plan.Legs)
                    {
                        var absQty = plan.BaseQty * (leg.InputAmount / 100m);
                        var newPct = currentQty <= EPS ? 0m : (absQty / currentQty) * 100m;
                        leg.InputAmount = Round8(newPct);
                    }

                    plan.Legs = plan.Legs.Where(l => l.InputAmount > EPS).ToList();

                    if (plan.Legs.Count == 0)
                    {
                        _store.RemovePlan(sym);
                        changed.Add(sym);
                        continue;
                    }
                }

                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);

                _store.SetPlan(plan);
                changed.Add(sym);
            }
        }

        if (changed.Count > 0)
            _store.Save();

        foreach (var s in changed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ScenariosChanged?.Invoke(s);
            _store.NotifyScenariosChanged(s);
        }

        return changed;
    }

    /// <summary>
    /// Reconciles futures TP scenario plans against current open futures positions.
    /// Composite key format: "SYMBOL:Long" or "SYMBOL:Short" (e.g. "BTC:Long").
    /// </summary>
    public void ReconcileFuturesPlans(IReadOnlyList<FuturesPosition> positions)
    {
        const decimal EPS = 0.00000001m;

        var changed = new List<string>();

        var qtyByKey = positions.ToDictionary(
            p => $"{p.Symbol.Trim().ToUpperInvariant()}:{p.Side}",
            p => p.Quantity,
            StringComparer.OrdinalIgnoreCase);

        var plans = _store.GetPlansSnapshot()
                          .Where(p => (p.Symbol ?? "").Contains(':'))
                          .ToList();

        foreach (var plan in plans)
        {
            var key = (plan.Symbol ?? "").Trim().ToUpperInvariant();
            if (key.Length == 0) continue;

            qtyByKey.TryGetValue(key, out var currentQty);

            if (currentQty <= EPS)
            {
                if (_store.RemovePlan(key))
                    changed.Add(key);
                continue;
            }

            if (plan.BaseQty <= EPS)
            {
                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);
                _store.SetPlan(plan);
                changed.Add(key);
                continue;
            }

            if (currentQty > plan.BaseQty + EPS)
            {
                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);
                _store.SetPlan(plan);
                changed.Add(key);
                continue;
            }

            if (currentQty < plan.BaseQty - EPS)
            {
                var closedQty = plan.BaseQty - currentQty;
                ConsumeFromPlan(plan, closedQty, plan.BaseQty);

                plan.Legs = plan.Legs.Where(l => l.InputAmount > EPS).ToList();

                if (plan.Legs.Count == 0)
                {
                    _store.RemovePlan(key);
                    changed.Add(key);
                    continue;
                }

                if (plan.IsPercentMode)
                {
                    foreach (var leg in plan.Legs)
                    {
                        var absQty = plan.BaseQty * (leg.InputAmount / 100m);
                        leg.InputAmount = Round8(currentQty <= EPS ? 0m : (absQty / currentQty) * 100m);
                    }

                    plan.Legs = plan.Legs.Where(l => l.InputAmount > EPS).ToList();
                    if (plan.Legs.Count == 0)
                    {
                        _store.RemovePlan(key);
                        changed.Add(key);
                        continue;
                    }
                }

                plan.BaseQty = currentQty;
                NormalizeAutoNote(plan);
                _store.SetPlan(plan);
                changed.Add(key);
            }
        }

        if (changed.Count > 0)
            _store.Save();

        foreach (var k in changed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ScenariosChanged?.Invoke(k);
            _store.NotifyScenariosChanged(k);
        }
    }

    private static void ConsumeFromPlan(ScenarioPlanDto plan, decimal soldQty, decimal baseQty)
    {
        const decimal EPS = 0.00000001m;

        for (int i = 0; i < plan.Legs.Count && soldQty > EPS;)
        {
            var leg = plan.Legs[i];

            var legAbs = plan.IsPercentMode
                         ? baseQty * (leg.InputAmount / 100m)
                         : leg.InputAmount;

            if (legAbs <= EPS)
            {
                plan.Legs.RemoveAt(i);
                continue;
            }

            if (soldQty >= legAbs - EPS)
            {
                soldQty -= legAbs;
                plan.Legs.RemoveAt(i);
                continue;
            }

            var remainingAbs = legAbs - soldQty;
            soldQty = 0m;

            if (plan.IsPercentMode)
            {
                var pct = baseQty <= EPS ? 0m : (remainingAbs / baseQty) * 100m;
                leg.InputAmount = Round8(pct);
            }
            else
            {
                leg.InputAmount = remainingAbs;
            }

            i++;
        }
    }

    private static void NormalizeAutoNote(ScenarioPlanDto plan)
    {
        if (plan.Legs is null) return;

        bool isStillAutoDefault = plan.IsPercentMode && plan.Legs.Count == 1 && Math.Abs(plan.Legs[0].InputAmount - 100m) < 0.00000001m &&
                                  !string.IsNullOrWhiteSpace(plan.Legs[0].Note) && 
                                  plan.Legs[0].Note.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase);

        if (isStillAutoDefault)
            return;

        foreach (var leg in plan.Legs)
        {
            if (!string.IsNullOrWhiteSpace(leg.Note) &&
                leg.Note.StartsWith("Auto:", StringComparison.OrdinalIgnoreCase))
            {
                leg.Note = "";
            }
        }
    }

    private static decimal Round8(decimal v) => Math.Round(v, 8, MidpointRounding.AwayFromZero);

    private void Recalc()
    {
        ErrorText = null;

        if (Symbol is null)
        {
            NotifyTotals();
            return;
        }

        foreach (var leg in Legs)
        {
            if (leg.InputAmount <= 0)
                ErrorText = "Leg qty/% must be > 0.";
            if (leg.TargetPrice <= 0)
                ErrorText = "Target price must be > 0.";
            if (leg.Note.Length > 1_000_000)
                ErrorText = "Note is too big.";
        }

        var planned = PlannedQty;
        if (planned > PositionQty + 0.00000001m)
            ErrorText = $"Planned qty ({planned}) exceeds position qty ({PositionQty}).";

        NotifyTotals();
    }

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(PlannedQty));
        OnPropertyChanged(nameof(RemainingQty));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
    }

    private decimal GetEffectiveQty(ScenarioLegVm leg)
    {
        if (IsPercentMode)
            return PositionQty * (leg.InputAmount / 100m);

        return leg.InputAmount;
    }
}

public partial class ScenarioLegVm : ObservableObject
{
    [ObservableProperty] private decimal inputAmount;
    [ObservableProperty] private decimal targetPrice;
    [ObservableProperty] private string  note         = string.Empty;
}
