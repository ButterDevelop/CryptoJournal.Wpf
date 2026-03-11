using System.Windows;
using System.Windows.Controls;

namespace CryptoJournal.Wpf.UI;

public static class DataGridCenteringAssist
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(DataGridCenteringAssist),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    private const string TbKey  = "Premium.CenteredDataGridTextBlock";
    private const string TbxKey = "Premium.CenteredDataGridTextBox";

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        if ((bool)e.NewValue)
        {
            grid.Loaded += GridOnLoaded;
            grid.Columns.CollectionChanged += (_, __) => Apply(grid);
        }
        else
        {
            grid.Loaded -= GridOnLoaded;
        }
    }

    private static void GridOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid) Apply(grid);
    }

    private static void Apply(DataGrid grid)
    {
        var textBlockStyle = GetOrCreateTextBlockStyle(grid);
        var textBoxStyle   = GetOrCreateTextBoxStyle(grid);
        var comboStyle     = GetOrCreateComboBoxStyle(grid);

        foreach (var col in grid.Columns)
        {
            switch (col)
            {
                case DataGridTextColumn tc:
                    tc.ElementStyle        = textBlockStyle;
                    tc.EditingElementStyle = textBoxStyle;
                    break;

                case DataGridComboBoxColumn cc:
                    cc.ElementStyle        = comboStyle;
                    cc.EditingElementStyle = comboStyle;
                    break;
            }
        }
    }

    private static Style GetOrCreateComboBoxStyle(DataGrid grid)
    {
        const string Key = "Premium.CenteredDataGridComboBox";
        if (grid.Resources[Key] is Style s) return s;

        var baseStyle =
        grid.TryFindResource(typeof(ComboBox)) as Style ??
        Application.Current.TryFindResource(typeof(ComboBox)) as Style;

        var style = new Style(typeof(ComboBox), baseStyle);
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));

        grid.Resources[Key] = style;
        return style;
    }

    private static Style GetOrCreateTextBlockStyle(DataGrid grid)
    {
        if (grid.Resources[TbKey] is Style s) return s;

        var baseStyle =
            grid.TryFindResource(typeof(TextBlock)) as Style ??
            Application.Current.TryFindResource(typeof(TextBlock)) as Style;

        var style = new Style(typeof(TextBlock), baseStyle);
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,              TextAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,   VerticalAlignment.Center));

        grid.Resources[TbKey] = style;
        return style;
    }

    private static Style GetOrCreateTextBoxStyle(DataGrid grid)
    {
        if (grid.Resources[TbxKey] is Style s) return s;

        var baseStyle =
            grid.TryFindResource(typeof(TextBox)) as Style ??
            Application.Current.TryFindResource(typeof(TextBox)) as Style;

        var style = new Style(typeof(TextBox), baseStyle);
        style.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));

        grid.Resources[TbxKey] = style;
        return style;
    }
}