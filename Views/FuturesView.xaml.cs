using CryptoJournal.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CryptoJournal.Wpf.Views;

public partial class FuturesView : UserControl
{
    public FuturesView()
    {
        InitializeComponent();
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent closing if the user clicks inside the scenario editor panel
        if (ScenarioPanel.IsVisible && IsDescendantOf(e.OriginalSource as DependencyObject, ScenarioPanel))
            return;

        // Prevent closing if the user selects a DataGrid row or cell
        if (IsInsideDataGridRowOrCell(e.OriginalSource as DependencyObject))
            return;

        // Close the scenario panel on any other external click
        if (DataContext is FuturesViewModel vm)
            vm.ClearSelectedPositionCommand.Execute(null);
    }

    private static bool IsInsideDataGridRowOrCell(DependencyObject? dep)
    {
        while (dep != null)
        {
            if (dep is DataGridRow || dep is DataGridCell)
                return true;
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
        return false;
    }

    private static bool IsDescendantOf(DependencyObject? dep, DependencyObject ancestor)
    {
        while (dep != null)
        {
            if (ReferenceEquals(dep, ancestor))
                return true;
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
        return false;
    }
}
