using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lucent.Ui;

public static class MenuChrome
{
    public static readonly DependencyProperty AnimatedProperty =
        DependencyProperty.RegisterAttached(
            "Animated", typeof(bool), typeof(MenuChrome),
            new PropertyMetadata(false, OnAnimatedChanged));

    public static void SetAnimated(DependencyObject element, bool value) =>
        element.SetValue(AnimatedProperty, value);

    public static bool GetAnimated(DependencyObject element) =>
        (bool)element.GetValue(AnimatedProperty);

    private static void OnAnimatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContextMenu menu) return;

        menu.Opened -= OnOpened;
        if ((bool)e.NewValue) menu.Opened += OnOpened;
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (FindPopup(menu) is not { } popup) return;

        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.Fade;
    }

    private static Popup? FindPopup(ContextMenu menu)
    {
        if (menu.Parent is Popup direct) return direct;

        DependencyObject? node = menu;
        for (int depth = 0; node is not null && depth < 8; depth++)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is Popup found) return found;
        }

        return null;
    }
}
