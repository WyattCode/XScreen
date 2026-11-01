using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace XScreen.App;

sealed partial class MainWindow
{
    readonly Border noticeHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        MaxWidth = 760,
        MaxHeight = 240,
        CornerRadius = new CornerRadius(8),
        Background = Palette.Ground,
        BorderBrush = Palette.Line,
        BorderThickness = new Thickness(1),
        Visibility = Visibility.Collapsed
    };
    readonly ScrollViewer noticeScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsTabStop = false,
        MaxHeight = 238
    };
    readonly Button noticeClose = new()
    {
        Content = new FontIcon { Glyph = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12 },
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Width = 36, Height = 36,
        Margin = new Thickness(8, 4, 8, 0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        Foreground = Palette.Primary,
        BorderThickness = new Thickness(0)
    };
    FrameworkElement? noticeFooter;

    void ConfigureFloatingNotice(FrameworkElement footer)
    {
        noticeFooter = footer;
        // Keep the close action outside the scrolling content. Reserve its width
        // inside the native InfoBar so that neither text nor its scrollbar overlaps it.
        var reservedCloseStyle = new Style(typeof(Button));
        reservedCloseStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 44d));
        reservedCloseStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38d));
        reservedCloseStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 0d));
        reservedCloseStyle.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
        reservedCloseStyle.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        reservedCloseStyle.Setters.Add(new Setter(AutomationProperties.AccessibilityViewProperty,
            Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw));
        notice.CloseButtonStyle = reservedCloseStyle;
        noticeScroll.Content = notice;
        var layer = new Grid(); layer.Children.Add(noticeScroll); layer.Children.Add(noticeClose);
        noticeHost.Child = layer;
        AutomationProperties.SetName(noticeClose, "关闭提示");
        ToolTipService.SetToolTip(noticeClose, "关闭提示");
        noticeClose.Click += (_, _) => notice.IsOpen = false;
        noticeHost.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && notice.IsOpen) { notice.IsOpen = false; e.Handled = true; }
        };
        // A sibling overlay never participates in the controls' grid rows.
        root.Children.Add(noticeHost);
        notice.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) =>
        {
            if (!notice.IsOpen && !exiting && root.XamlRoot is { } xamlRoot
                && IsInsideNotice(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject))
            {
                // Never leave focus in a collapsed overlay or move it to a setting.
                if (!refresh.IsEnabled || !refresh.Focus(FocusState.Programmatic)) appearance.Focus(FocusState.Programmatic);
            }
            noticeHost.Visibility = notice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (notice.IsOpen) noticeScroll.ChangeView(null, 0, null, true);
        });
        void PositionNotice()
        {
            // Clear the shelf as well as the footer. The shelf carries the picture-mode,
            // contrast and black-stabilizer readouts, which are exactly what a fault card
            // would otherwise hide at the moment they matter most. It collapses to zero
            // height on the picture/color page, so this stays correct there too.
            double shelfHeight = statusShelf?.Visibility == Visibility.Visible ? statusShelf.ActualHeight : 0;
            noticeHost.Margin = new Thickness(20, 12, 20, layout.Padding.Bottom + footer.ActualHeight + shelfHeight + 24);
            noticeHost.MaxHeight = Math.Min(240, Math.Max(80, root.ActualHeight * .45));
            noticeScroll.MaxHeight = noticeHost.MaxHeight - 2;
        }
        footer.SizeChanged += (_, _) => PositionNotice();
        if (statusShelf != null) statusShelf.SizeChanged += (_, _) => PositionNotice();
        root.SizeChanged += (_, _) => PositionNotice();
        PositionNotice();
        // Closing the InfoBar only dismisses this card. Fault recovery still uses Refresh.
    }

    bool IsInsideNotice(DependencyObject? element)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, noticeHost)) return true;
        return false;
    }
}
