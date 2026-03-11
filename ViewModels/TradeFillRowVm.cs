using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Storage.Attachments;
using CryptoJournal.Wpf.Views.Dialogs;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels;

public partial class TradeFillRowVm : ObservableObject
{
    private readonly ILocalImageStore _imageStore;

    public TradeFillRowVm(TradeFill fill, ILocalImageStore imageStore)
    {
        _imageStore = imageStore;
        _fill = fill;
        Attachments = new ObservableCollection<AttachmentVm>(
            (fill.Attachments ?? []).Select(f => new AttachmentVm(f, imageStore))
        );
        Attachments.CollectionChanged += (s, e) => UpdateFillAttachments();
    }

    private TradeFill _fill;
    public TradeFill Fill
    {
        get => _fill;
        private set
        {
            if (Equals(_fill, value)) return;
            _fill = value;

            // Trigger UI updates for all computed properties
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(TimeUtc));
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(Symbol));
            OnPropertyChanged(nameof(Quantity));
            OnPropertyChanged(nameof(Price));
            OnPropertyChanged(nameof(ValueQuote));
            OnPropertyChanged(nameof(FeeQuote));
            OnPropertyChanged(nameof(TakeProfit));
            OnPropertyChanged(nameof(StopLoss));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(RealizedPnlQuote));
            OnPropertyChanged(nameof(RealizedPnlPct));
        }
    }

    private  bool _symbolDirty;
    internal bool IsSymbolDirty => _symbolDirty;
    internal void ClearSymbolDirty() => _symbolDirty = false;

    // Editable DataGrid Columns
    public Guid Id => Fill.Id;

    public DateTimeOffset TimeUtc
    {
        get => Fill.TimeUtc;
        set
        {
            if (value == Fill.TimeUtc) return;
            Fill = Fill with { TimeUtc = value };
        }
    }

    public TxType Type
    {
        get => Fill.Type;
        set
        {
            if (value == Fill.Type) return;
            Fill = Fill with { Type = value };
        }
    }

    public string Symbol
    {
        get => Fill.Symbol;
        set
        {
            var v = (value ?? "").Trim().ToUpperInvariant();
            if (v == Fill.Symbol) return;

            Fill = Fill with { Symbol = v };
            _symbolDirty = true;
        }
    }

    public decimal Quantity
    {
        get => Fill.Quantity;
        set
        {
            if (value == Fill.Quantity) return;
            Fill = Fill with { Quantity = value };
        }
    }

    public decimal Price
    {
        get => Fill.Price;
        set
        {
            if (value == Fill.Price) return;
            Fill = Fill with { Price = value };
        }
    }

    public decimal Leverage
    {
        get => Fill.Leverage;
        set
        {
            if (value == Fill.Leverage) return;
            Fill = Fill with { Leverage = value };
        }
    }

    public decimal FeeQuote
    {
        get => Fill.FeeQuote;
        set
        {
            if (value == Fill.FeeQuote) return;
            Fill = Fill with { FeeQuote = value };
        }
    }

    public decimal? TakeProfit
    {
        get => Fill.TakeProfit;
        set
        {
            if (value == Fill.TakeProfit) return;
            Fill = Fill with { TakeProfit = value };
        }
    }

    public decimal? StopLoss
    {
        get => Fill.StopLoss;
        set
        {
            if (value == Fill.StopLoss) return;
            Fill = Fill with { StopLoss = value };
        }
    }

    public string? Note
    {
        get => Fill.Note;
        set
        {
            if (value == Fill.Note) return;
            Fill = Fill with { Note = value };
        }
    }

    public ObservableCollection<AttachmentVm> Attachments { get; }

    private void UpdateFillAttachments()
    {
        var list = Attachments.Select(a => a.Filename).ToList();
        
        // Prevent circular updates by comparing with existing attachments
        var current = Fill.Attachments ?? [];
        if (current.SequenceEqual(list)) return;

        Fill = Fill with { Attachments = list.Count > 0 ? list : null };
        OnAttachmentsChanged?.Invoke();
    }

    // Read-only computed properties
    public decimal ValueQuote => Fill.ValueQuote;

    // Read-only PnL properties (applicable only for Sell transactions)
    public decimal? RealizedPnlQuote => Fill.RealizedPnlQuote;
    public decimal? RealizedPnlPct => Fill.RealizedPnlPct;

    public Action? OnAttachmentsChanged { get; set; }

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Attach Images"
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
        catch { /* Suppress physical deletion errors as a best-effort cleanup */ }
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

    // Symbol Icon
    [ObservableProperty] private ImageSource? symbolIcon;

    // Public API Methods
    public void UpdateSellPnl(decimal? pnl, decimal? pct)
        => Fill = Fill with { RealizedPnlQuote = pnl, RealizedPnlPct = pct };

    public void ClearSellPnlIfAny()
    {
        if (Fill.RealizedPnlQuote is null && Fill.RealizedPnlPct is null) return;
        Fill = Fill with { RealizedPnlQuote = null, RealizedPnlPct = null };
    }
}