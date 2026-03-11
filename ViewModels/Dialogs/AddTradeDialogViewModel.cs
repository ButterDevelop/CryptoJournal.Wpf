using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Validation;
using CryptoJournal.Wpf.Storage.Attachments;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CryptoJournal.Wpf.ViewModels.Dialogs;

public partial class AddTradeDialogViewModel : ObservableObject
{
    private readonly string _quote; // Quote currency identifier (e.g., USDT or USDC)

    private bool _syncing;

    private readonly ITradePrecheck   _precheck;
    private readonly ILocalImageStore _imageStore;
    private IReadOnlyList<TradeFill>  _existing = [];

    public ObservableCollection<AttachmentVm> Attachments { get; } = [];

    public void SetExistingFills(IReadOnlyList<TradeFill> fills) => _existing = fills ?? [];

    public AddTradeDialogViewModel(IEnvironmentService env, ITradePrecheck precheck, ILocalImageStore imageStore)
    {
        _quote      = env.Current.QuoteCurrency.Trim().ToUpperInvariant();
        _precheck   = precheck;
        _imageStore = imageStore;
        Symbol    = _quote;
        Price     = 1m;

        // Initialize default date and time fields
        var now     = DateTimeOffset.UtcNow;
        _syncing    = true;
        TimeUtcText = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        TimeUtcDate = now.UtcDateTime.Date;
        TimeUtcTime = now.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _syncing    = false;

        RecalcUiRules();
    }

    public bool TryValidate(out string message)
    {
        message = "";

        if (!TryGetTimeUtc(out var utc))
        {
            message = "Invalid Time (UTC).";
            return false;
        }

        var sym = (Symbol ?? "").Trim().ToUpperInvariant();
        var res = _precheck.Validate(_existing, utc, Type, sym, Quantity, Price, FeeQuote, Leverage, _quote);

        if (!res.Ok)
        {
            message = res.Message;
            return false;
        }

        return true;
    }

    public DateTime TimeUtc => TryGetTimeUtc(out var utc) ? utc : DateTime.UtcNow;

    [ObservableProperty] private bool      isTimePopupOpen;

    [ObservableProperty] private string    timeUtcText = "";

    // Backing fields for the date and time popup editors
    [ObservableProperty] private DateTime? timeUtcDate;
    [ObservableProperty] private string    timeUtcTime = "00:00:00";

    partial void OnTimeUtcTextChanged(string value)
    {
        if (_syncing) return;

        if (TryParseUtc(value, out var dto))
        {
            _syncing    = true;
            TimeUtcDate = dto.UtcDateTime.Date;
            TimeUtcTime = dto.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _syncing    = false;
        }
    }

    // Applies the selected date and time from the popup
    [RelayCommand]
    private void ApplyPickedTime()
    {
        if (TimeUtcDate is null) return;

        if (!TryParseTimePart(TimeUtcTime, out var tod))
            tod = TimeSpan.Zero;

        var dtUtc = DateTime.SpecifyKind(TimeUtcDate.Value.Date + tod, DateTimeKind.Utc);
        var dto   = new DateTimeOffset(dtUtc);

        _syncing = true;
        TimeUtcText = dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        _syncing = false;

        IsTimePopupOpen = false; // Close the date/time picker popup
    }

    // Retrieves the final UTC time upon confirmation
    public bool TryGetTimeUtc(out DateTime utc)
    {
        utc = default;
        if (!TryParseUtc(TimeUtcText, out var dto))
            return false;

        utc = dto.UtcDateTime;
        return true;
    }

    private static bool TryParseUtc(string input, out DateTimeOffset dto)
    {
        dto = default;

        input = (input ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var styles = DateTimeStyles.AllowWhiteSpaces
                   | DateTimeStyles.AssumeUniversal
                   | DateTimeStyles.AdjustToUniversal;

        string[] formats =
        {
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy HH:mm:ss"
        };

        // Prioritize strict date parsing formats (optimal for pasted text)
        if (DateTimeOffset.TryParseExact(input, formats, CultureInfo.InvariantCulture, styles, out dto))
            return true;

        // Fallback to culture-invariant and local culture parsing
        if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, styles, out dto))
            return true;

        if (DateTimeOffset.TryParse(input, CultureInfo.CurrentCulture, styles, out dto))
            return true;

        return false;
    }

    private static bool TryParseTimePart(string s, out TimeSpan time)
    {
        time = default;

        s = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return false;

        var formats = new[] { "h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss" };

        return TimeSpan.TryParseExact(s, formats, CultureInfo.InvariantCulture, out time)
            || TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out time);
    }

    // Trade fill properties

    [ObservableProperty] private TxType   type;
    [ObservableProperty] private string   symbol     = "";
    [ObservableProperty] private decimal  quantity;
    [ObservableProperty] private decimal  price      = 1m;
    [ObservableProperty] private decimal  feeQuote;
    [ObservableProperty] private string   note       = "";
    [ObservableProperty] private decimal  leverage   = 1m;
    [ObservableProperty] private decimal? takeProfit = null;
    [ObservableProperty] private decimal? stopLoss   = null;

    public bool IsQuoteSymbol =>
        string.Equals(Symbol?.Trim(), _quote, StringComparison.OrdinalIgnoreCase);

    /// <summary>Only OpenLong/OpenShort — allows entry of TP and SL.</summary>
    public bool IsFuturesOpenType =>
        Type is TxType.OpenLong or TxType.OpenShort;

    public bool IsFuturesType =>
        Type is TxType.OpenLong or TxType.CloseLong or TxType.OpenShort or TxType.CloseShort;

    public bool PriceIsRequired =>
        Type is TxType.Buy or TxType.Sell || IsFuturesType;

    public bool PriceIsRelevant =>
        PriceIsRequired && !IsQuoteSymbol;

    public bool PriceVisible =>
        Type is TxType.Buy or TxType.Sell or TxType.OpenLong or TxType.CloseLong or TxType.OpenShort or TxType.CloseShort
        || (Type is TxType.Deposit && !IsQuoteSymbol);

    public bool LeverageVisible => IsFuturesType;
    public bool TpSlVisible     => IsFuturesOpenType;

    partial void OnTypeChanged(TxType value)   => RecalcUiRules();
    partial void OnSymbolChanged(string value) => RecalcUiRules();

    private void RecalcUiRules()
    {
        OnPropertyChanged(nameof(IsQuoteSymbol));
        OnPropertyChanged(nameof(IsFuturesOpenType));
        OnPropertyChanged(nameof(IsFuturesType));
        OnPropertyChanged(nameof(PriceIsRequired));
        OnPropertyChanged(nameof(PriceIsRelevant));
        OnPropertyChanged(nameof(PriceVisible));
        OnPropertyChanged(nameof(LeverageVisible));
        OnPropertyChanged(nameof(TpSlVisible));

        // Default price to 1 for non-pricing transaction types
        if (!PriceIsRequired)
            Price = 1m;

        // Reset futures-specific fields when switching to spot or flat types
        if (!IsFuturesType)
            Leverage = 1m;

        if (!IsFuturesOpenType)
        {
            TakeProfit = null;
            StopLoss   = null;
        }
    }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        var ofd = new OpenFileDialog
        {
            Filter      = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            Multiselect = true,
            Title       = "Attach Images"
        };
        if (ofd.ShowDialog() != true) return;

        foreach (var file in ofd.FileNames)
        {
            var savedName = await _imageStore.SaveImageAsync(file);
            Attachments.Add(new AttachmentVm(savedName, _imageStore));
        }
    }

    [RelayCommand]
    private async Task PasteAttachmentAsync()
    {
        if (!Clipboard.ContainsImage()) return;

        var image = Clipboard.GetImage();
        if (image == null) return;

        using var ms = new System.IO.MemoryStream();
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        encoder.Save(ms);
        ms.Position = 0;

        var savedName = await _imageStore.SaveImageStreamAsync(ms, ".png");
        Attachments.Add(new AttachmentVm(savedName, _imageStore));
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentVm vm)
    {
        if (vm == null) return;
        Attachments.Remove(vm);
        try
        {
            _imageStore.DeleteImage(vm.Filename);
        }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private void ViewAttachment(AttachmentVm vm)
    {
        if (vm == null) return;
        var w = new ImageViewerWindow(Attachments, vm)
        {
            Owner = Application.Current.MainWindow
        };
        w.Show();
    }
}