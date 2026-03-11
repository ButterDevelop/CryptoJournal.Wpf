using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels.Dialogs;

public sealed record ConfirmLine(string Label, string Value);

public sealed class ConfirmDialogViewModel
{
    public string TitleText   { get; init; } = "Confirm";
    public string HeaderText  { get; init; } = "Confirm";
    public string MessageText { get; init; } = "";

    public IReadOnlyList<ConfirmLine> Lines { get; init; } = [];
    public bool HasLines => Lines.Count > 0;

    public string ConfirmText { get; init; } = "OK";
    public string CancelText  { get; init; } = "Cancel";

    // You can add a red/accent background
    public Brush ConfirmBrush { get; init; } = new SolidColorBrush(Color.FromRgb(0x00, 0xC7, 0xD9));
}