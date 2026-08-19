using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Lucent.Ui;

public static class PageContextMenu
{
    public static void Show(FrameworkElement host, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        CoreWebView2Deferral deferral = e.GetDeferral();
        e.Handled = true;

        int chosen = -1;

        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("ChromeContextMenu"),
            PlacementTarget = host,
            Placement = PlacementMode.Relative,
            HorizontalOffset = e.Location.X,
            VerticalOffset = e.Location.Y
        };

        MenuChrome.SetAnimated(menu, true);

        Fill(menu.Items, e.MenuItems, id => chosen = id);

        menu.Closed += (_, _) =>
        {
            if (chosen >= 0) e.SelectedCommandId = chosen;
            deferral.Complete();
        };

        menu.IsOpen = true;
    }

    private static void Fill(ItemCollection target,
                             IList<CoreWebView2ContextMenuItem> source,
                             Action<int> choose)
    {
        foreach (CoreWebView2ContextMenuItem item in source)
        {
            if (item.Kind == CoreWebView2ContextMenuItemKind.Separator)
            {
                target.Add(new Separator
                {
                    Style = (Style)Application.Current.FindResource("ChromeMenuSeparator")
                });
                continue;
            }

            var entry = new MenuItem
            {
                Header = Label(item.Label),
                InputGestureText = item.ShortcutKeyDescription,
                IsEnabled = item.IsEnabled,
                Icon = Icon(item),
                IsCheckable = item.Kind is CoreWebView2ContextMenuItemKind.CheckBox
                                        or CoreWebView2ContextMenuItemKind.Radio,
                IsChecked = item.IsChecked,

                ItemContainerStyle = (Style)Application.Current.FindResource("ChromeMenuItem")
            };

            if (item.Kind == CoreWebView2ContextMenuItemKind.Submenu)
            {
                Fill(entry.Items, item.Children, choose);
            }
            else
            {
                int command = item.CommandId;
                entry.Click += (_, _) => choose(command);
            }

            target.Add(entry);
        }
    }

    private static string Label(string label)
    {
        var text = new StringBuilder(label.Length);

        for (int i = 0; i < label.Length; i++)
        {
            char c = label[i];

            if (c == '&')
            {
                if (i + 1 < label.Length && label[i + 1] == '&')
                {
                    text.Append('&');
                    i++;
                }

                continue;
            }

            if (c == '_') text.Append('_');

            text.Append(c);
        }

        return text.ToString();
    }

    private static object? Icon(CoreWebView2ContextMenuItem item)
    {
        try
        {
            using Stream? source = item.Icon;
            if (source is null) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = source;
            bitmap.EndInit();
            bitmap.Freeze();

            return new Image { Source = bitmap, Width = 16, Height = 16 };
        }
        catch (Exception)
        {
            return null;        }
    }
}
