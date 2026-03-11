using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.UI;
using System.Collections.ObjectModel;

namespace CryptoJournal.Wpf.ViewModels.Dialogs;

public partial class EnvironmentManagerViewModel : ObservableObject
{
    private readonly IEnvironmentService _env;
    private readonly IConfirmService     _confirm;

    public ObservableCollection<EnvironmentProfile> Items { get; } = [];

    [ObservableProperty] private EnvironmentProfile? selected;

    // Right-side editor fields
    [ObservableProperty] private string editName  = "";
    [ObservableProperty] private string editQuote = "USDT";

    // Create/update mode
    [ObservableProperty] private bool isNewMode;

    public string SaveButtonText => IsNewMode ? "Create" : "Update";

    // For UI "current" indicator in list
    public string CurrentId => _env.Current.Id;

    private bool _suppressAutoSwitch;

    public EnvironmentManagerViewModel(IEnvironmentService env, IConfirmService confirm)
    {
        _env     = env;
        _confirm = confirm;
        Reload();

        _env.EnvironmentsChanged += (_, __) => Reload();
        _env.CurrentChanged += (_, __) =>
        {
            // refresh current indicator
            OnPropertyChanged(nameof(CurrentId));

            // keep selection on current (but do NOT trigger switching loop)
            var cur = Items.FirstOrDefault(x => x.Id == _env.Current.Id);
            if (cur is not null && !ReferenceEquals(Selected, cur))
            {
                _suppressAutoSwitch = true;
                Selected = cur;
                _suppressAutoSwitch = false;
            }
        };
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var e in _env.Environments)
            Items.Add(e);

        OnPropertyChanged(nameof(CurrentId));

        // select current by default
        var cur = Items.FirstOrDefault(x => x.Id == _env.Current.Id) ?? Items.FirstOrDefault();
        _suppressAutoSwitch = true;
        Selected = cur;
        _suppressAutoSwitch = false;

        // edit selected by default
        if (Selected is not null)
        {
            EditName  = Selected.Name;
            EditQuote = Selected.QuoteCurrency;
        }
    }

    partial void OnSelectedChanged(EnvironmentProfile? value)
    {
        if (value is null) return;

        // selection should fill editor
        EditName  = value.Name;
        EditQuote = value.QuoteCurrency;

        // selection ends "create mode"
        IsNewMode = false;
        OnPropertyChanged(nameof(SaveButtonText));

        // auto-switch current (unless we are syncing selection from CurrentChanged)
        if (_suppressAutoSwitch) return;

        _ = AutoSwitchAsync(value);
    }

    private async Task AutoSwitchAsync(EnvironmentProfile value)
    {
        try
        {
            await _env.SetCurrentAsync(value.Id);
        }
        catch
        {
            // ignore / log
        }
    }

    [RelayCommand]
    private async Task New()
    {
        var created = await _env.CreateAsync("New", _env.Current.QuoteCurrency, false);
        var found   = Items.FirstOrDefault(x => x.Id == created.Id) ?? created;

        _suppressAutoSwitch = true;
        Selected            = found;
        _suppressAutoSwitch = false;

        EditName  = found.Name;
        EditQuote = found.QuoteCurrency;

        await _env.SetCurrentAsync(found.Id);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Selected is null) return;

        var name  = (EditName  ?? "").Trim();
        var quote = (EditQuote ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(quote)) quote = _env.Current.QuoteCurrency;

        await _env.UpdateAsync(
            Selected.Id,
            string.IsNullOrWhiteSpace(name) ? Selected.Name : name,
            quote);
    }

    [RelayCommand]
    private async Task Delete(EnvironmentProfile? item)
    {
        if (item is null) return;
    
        // FIRST check "last env"
        if (Items.Count <= 1)
        {
            await _confirm.InfoAsync(
                header:  "Cannot delete",
                message: "You can't delete the last environment.",
                lines:
                [
                    new ConfirmLine("Tip", "Create another environment first.")
                ],
                okText: "OK");
    
            return;
        }
    
        // Further confirmation (оставляем ConfirmAsync как есть)
        var ok1 = await _confirm.ConfirmAsync(
            "Confirm",
            $"Delete environment '{item.Name} ({item.QuoteCurrency})'?",
            confirmText: "Delete",
            cancelText:  "Cancel",
            destructive: true);
    
        if (!ok1) return;
    
        var ok2 = await _confirm.ConfirmAsync(
            "Confirm again",
            "This cannot be undone. Delete?",
            confirmText: "Delete",
            cancelText:  "Cancel",
            destructive: true);
    
        if (!ok2) return;
    
        await _env.DeleteAsync(item.Id);
    }
}