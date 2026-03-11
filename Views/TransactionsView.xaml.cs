using CryptoJournal.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CryptoJournal.Wpf.Views;

public partial class TransactionsView : UserControl
{
    public TransactionsView() => InitializeComponent();

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Retain focus and selection if the user clicks inside the DataGrid area
        if (TransactionsDataGrid is not null)
        {
            var p = e.GetPosition(TransactionsDataGrid);

            var inside =
                p.X >= 0 && p.X <= TransactionsDataGrid.ActualWidth &&
                p.Y >= 0 && p.Y <= TransactionsDataGrid.ActualHeight;

            if (inside)
                return;
        }

        // Clear the active selection and keyboard focus when clicking outside the grid
        if (DataContext is TransactionsViewModel vm) vm.Selected = null;
        Keyboard.ClearFocus();
    }

    private static DependencyObject? GetParentSmart(DependencyObject? d)
    {
        if (d is null) return null;

        if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(d);

        return LogicalTreeHelper.GetParent(d);
    }

    private static T? FindAncestorSmart<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = GetParentSmart(d);
        }
        return null;
    }

    private async void DataGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;

        // Defer model commit until the UI edit transaction has completely finished
        await Dispatcher.InvokeAsync(async () =>
        {
            if (DataContext is TransactionsViewModel vm)
                await vm.CommitEditsAsync();
        }, DispatcherPriority.Background);
    }

    private void TransactionsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (grid.IsReadOnly) return;

        // Do not interfere with native interaction if the user is clicking an active ComboBox
        if (FindAncestorSmart<ComboBox>(e.OriginalSource as DependencyObject) is not null)
            return;

        var cell = FindAncestorSmart<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null) return;

        if (!Equals(cell.Column?.Header, "Type")) return;
        if (cell.IsEditing) return;

        grid.CurrentCell  = new DataGridCellInfo(cell.DataContext, cell.Column);
        grid.SelectedItem = cell.DataContext;
        grid.BeginEdit();

        e.Handled = true;
    }

    private void TransactionsDataGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (!Equals(e.Column?.Header, "Type")) return;

        if (e.EditingElement is ComboBox cb)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                cb.Focus();
                cb.IsDropDownOpen = true;
            }), DispatcherPriority.Input);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is T t) return t;

            var deeper = FindVisualChild<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private void TypeCombo_DropDownClosed(object sender, EventArgs e)
    {
        if (TransactionsDataGrid.IsReadOnly) return;

        TransactionsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TransactionsDataGrid.CommitEdit(DataGridEditingUnit.Row,  true);
    }

    private void ExpandToggleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton tb && FindAncestorSmart<DataGridRow>(tb) is DataGridRow row)
        {
            if (TransactionsDataGrid.SelectionUnit == DataGridSelectionUnit.CellOrRowHeader)
            {
                row.IsSelected = !row.IsSelected;
                e.Handled = true;
            }
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Clipboard.ContainsImage() && TransactionsDataGrid.SelectedItem is TradeFillRowVm rowVm)
            {
                if (rowVm.PasteAttachmentCommand.CanExecute(null))
                {
                    rowVm.PasteAttachmentCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}