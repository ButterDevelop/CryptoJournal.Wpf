using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CryptoJournal.Wpf.UI;

public static class SmartQtyAssist
{
    public static readonly DependencyProperty EnableToggleProperty =
        DependencyProperty.RegisterAttached("EnableToggle", typeof(bool), typeof(SmartQtyAssist), new PropertyMetadata(false, OnEnableToggleChanged));

    public static void SetEnableToggle(DependencyObject element, bool value) => element.SetValue(EnableToggleProperty, value);
    public static bool GetEnableToggle(DependencyObject element) => (bool)element.GetValue(EnableToggleProperty);

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.RegisterAttached("IsExpanded", typeof(bool), typeof(SmartQtyAssist), new PropertyMetadata(false));

    public static void SetIsExpanded(DependencyObject element, bool value) => element.SetValue(IsExpandedProperty, value);
    public static bool GetIsExpanded(DependencyObject element) => (bool)element.GetValue(IsExpandedProperty);

    private static void OnEnableToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb)
        {
            if ((bool)e.NewValue)
            {
                tb.PreviewMouseLeftButtonDown += OnTextBlockPreviewMouseDown;
                tb.Cursor = Cursors.Hand;
            }
            else
            {
                tb.PreviewMouseLeftButtonDown -= OnTextBlockPreviewMouseDown;
                tb.ClearValue(FrameworkElement.CursorProperty);
            }
        }
    }

    private static void OnTextBlockPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is TextBlock tb)
        {
            SetIsExpanded(tb, !GetIsExpanded(tb));
            e.Handled = true;
        }
    }
}
