using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace XScreen.App;

// A UI-only popup. Its status is a snapshot of MainWindow; opening it never reads hardware.
sealed class TrayMenuWindow : Window, IDisposable
{
    const int AdjustmentStep = 10;
    readonly Grid root = new();
    readonly StackPanel body = new() { Spacing = 1 };
    readonly Border surface = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(7) };
    readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false };
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock group = new() { FontSize = 11, Margin = new Thickness(12, 10, 12, 5) };
    readonly List<Button> actions = [];
    readonly List<Border> separators = [];
    readonly Func<byte, bool> canAdjust;
    readonly Func<bool> canReadAgain;
    readonly Action readAgain;
    readonly Button recovery;
    readonly Action open, exit;
    readonly Action<byte, int> adjust;
    readonly IntPtr hwnd;
    PointInt32 anchor;
    bool disposed, repositioning;
    public bool IsOpen { get; private set; }
    public event Action<Exception>? Failed;
    internal Func<PointInt32, RectInt32> WorkAreaProvider { get; set; } = point => DisplayArea.GetFromPoint(point, DisplayAreaFallback.Nearest).WorkArea;

    public TrayMenuWindow(Func<byte, bool> allowAdjustment, Action openWindow, Action<byte, int> change, Action quit, Func<bool>? allowReadAgain = null, Action? reread = null)
    {
        canAdjust = allowAdjustment; open = openWindow; adjust = change; exit = quit;
        canReadAgain = allowReadAgain ?? (() => false); readAgain = reread ?? (() => { });
        Title = "XScreen · 托盘菜单";
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false;
        presenter.IsMaximizable = presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter); AppWindow.IsShownInSwitchers = false;
        int corner = 2; _ = DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

        var heading = new Grid { Padding = new Thickness(12, 11, 12, 12), ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "XScreen.png"))), Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        heading.Children.Add(icon);
        var label = new StackPanel { Spacing = 3 };
        label.Children.Add(new TextBlock { Text = "XScreen", FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        label.Children.Add(new TextBlock { Text = "32GX870A · UltraGear", FontSize = 11, Foreground = Palette.Secondary, TextTrimming = TextTrimming.CharacterEllipsis });
        label.Children.Add(status); Grid.SetColumn(label, 1); heading.Children.Add(label); body.Children.Add(heading);
        Separator(body);
        AddAction(body, "打开控制窗口", "\uE8A7", "", () => InvokeAction(0));
        recovery = AddAction(body, "重新读取状态", "\uE72C", "", InvokeRecovery, false);
        recovery.Visibility = Visibility.Collapsed; recovery.IsEnabled = false;
        body.Children.Add(group);
        AddAction(body, "亮度增加", "\uE706", $"+{AdjustmentStep}", () => InvokeAction(1));
        AddAction(body, "亮度降低", "\uE706", $"−{AdjustmentStep}", () => InvokeAction(2));
        AddAction(body, "显示器音量增加", "\uE767", $"+{AdjustmentStep}", () => InvokeAction(3));
        AddAction(body, "显示器音量降低", "\uE767", $"−{AdjustmentStep}", () => InvokeAction(4));
        Separator(body);
        AddAction(body, "退出 XScreen", "\uE8BB", "", () => InvokeAction(5));
        scroll.Content = body; surface.Child = scroll; root.Children.Add(surface); Content = root;
        root.Resources["SystemAccentColor"] = Windows.UI.Color.FromArgb(255, 229, 0, 70);
        root.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        // Menu navigation precedes the ScrollViewer's own keyboard scrolling.
        root.PreviewKeyDown += (_, e) => { if (HandleKey(e.Key)) e.Handled = true; };
        root.Loaded += (_, _) =>
        {
            root.XamlRoot.Changed += (_, _) => { if (IsOpen) Reposition(); };
            if (IsOpen && Reposition()) actions[0].Focus(FocusState.Programmatic);
        };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) Dismiss(); };
        AppWindow.Closing += (_, e) => { if (!disposed) { e.Cancel = true; Dismiss(); } };
        SetTheme(ElementTheme.Dark);
    }
    void Separator(StackPanel body)
    {
        var line = new Border { Height = 1, Margin = new Thickness(5, 5, 5, 5) };
        separators.Add(line); body.Children.Add(line);
    }
    Button AddAction(StackPanel body, string text, string glyph, string suffix, Action handler, bool normalAction = true)
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new() { Width = new GridLength(18) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons"), Foreground = Palette.Secondary, VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextLineBounds = TextLineBounds.Tight, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(title, 1); content.Children.Add(title);
        var hint = new TextBlock { Text = suffix, FontSize = 12, Foreground = Palette.Secondary, VerticalAlignment = VerticalAlignment.Center, TextLineBounds = TextLineBounds.Tight }; Grid.SetColumn(hint, 2); content.Children.Add(hint);
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 37, Padding = new Thickness(12, 7, 12, 7), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Colors.Transparent) };
        button.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Colors.Transparent);
        // The row already dims as a whole; avoid applying the template's text alpha again.
        button.Resources["ButtonForegroundDisabled"] = Palette.Primary;
        AutomationProperties.SetName(button, text + (suffix.Length > 0 ? " " + suffix : ""));
        button.Click += (_, _) => handler(); if (normalAction) actions.Add(button); body.Children.Add(button); return button;
    }
    public void SetTheme(ElementTheme theme)
    {
        root.RequestedTheme = theme;
        bool dark = theme == ElementTheme.Dark;
        surface.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 32, 31, 35) : Windows.UI.Color.FromArgb(255, 251, 249, 251));
        surface.BorderBrush = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 66, 62, 69) : Windows.UI.Color.FromArgb(255, 215, 208, 216));
        status.Foreground = group.Foreground = Palette.Secondary;
        foreach (var line in separators) line.Background = Palette.Line;
    }
    public void ShowAt(PointInt32 point, string connection, ElementTheme theme)
    {
        if (disposed) return;
        bool opening = !IsOpen;
        var focused = NavigationActions().FirstOrDefault(b => b.FocusState != FocusState.Unfocused);
        // Keep this visible session anchored; a fresh open may use a different tray position.
        if (opening) anchor = point;
        status.Text = connection; SetTheme(theme);
        bool brightness = canAdjust(0x10), volume = canAdjust(0x62);
        actions[1].IsEnabled = actions[2].IsEnabled = brightness;
        actions[3].IsEnabled = actions[4].IsEnabled = volume;
        bool recoverable = canReadAgain();
        recovery.Visibility = recoverable ? Visibility.Visible : Visibility.Collapsed;
        recovery.IsEnabled = recoverable;
        // WinUI may automatically advance a disabled adjustment's focus to Exit.
        bool focusedActionDisabled = focused != null && (!focused.IsEnabled || focused.Visibility != Visibility.Visible);
        foreach (var button in actions) button.Opacity = button.IsEnabled ? 1 : .45;
        group.Text = brightness || volume ? "快捷调节" : recoverable ? "快捷调节 · 请先重新读取" : "快捷调节 · 当前不可用";
        // Only relocate while hidden. Duplicate context-menu notifications must not
        // move a visible card through the tray anchor or reset its keyboard focus.
        if (opening) AppWindow.Move(anchor);
        IsOpen = true;
        if (!Reposition()) return;
        if (opening)
        {
            AppWindow.Show(); scroll.UpdateLayout(); scroll.ChangeView(null, 0, null, true); Activate();
            actions[0].Focus(FocusState.Programmatic);
        }
        else if (focusedActionDisabled || !NavigationActions().Any(b => b.FocusState != FocusState.Unfocused)) actions[0].Focus(FocusState.Programmatic);
    }
    bool Reposition()
    {
        if (disposed) return false;
        if (repositioning) return true;
        repositioning = true;
        try
        {
            var work = WorkAreaProvider(anchor);
            double scale = Math.Max(1, GetDpiForWindow(hwnd) / 96d);
            int width = Place(anchor, work, scale, 1).Width;
            double horizontalInsets = surface.Padding.Left + surface.Padding.Right + surface.BorderThickness.Left + surface.BorderThickness.Right;
            double verticalInsets = surface.Padding.Top + surface.Padding.Bottom + surface.BorderThickness.Top + surface.BorderThickness.Bottom;
            // Keep the work-area cap so smaller screens scroll instead of clipping actions.
            body.Measure(new Windows.Foundation.Size(Math.Max(1, width / scale - horizontalInsets), double.PositiveInfinity));
            var bounds = Place(anchor, work, scale, body.DesiredSize.Height + verticalInsets);
            if (AppWindow.Position.X != bounds.X || AppWindow.Position.Y != bounds.Y || AppWindow.Size.Width != bounds.Width || AppWindow.Size.Height != bounds.Height)
                AppWindow.MoveAndResize(bounds);
            return true;
        }
        catch (Exception ex) { Dismiss(); Failed?.Invoke(ex); return false; }
        finally { repositioning = false; }
    }
    internal static RectInt32 Place(PointInt32 point, RectInt32 work, double scale, double logicalHeight)
    {
        int gap = Math.Min(Math.Max(1, (int)Math.Round(8 * scale)), Math.Max(0, (Math.Min(work.Width, work.Height) - 1) / 2));
        int width = Math.Min((int)Math.Ceiling(290 * scale), Math.Max(1, work.Width - gap * 2));
        int height = Math.Min((int)Math.Ceiling(logicalHeight * scale), Math.Max(1, work.Height - gap * 2));
        int left = Math.Clamp(point.X - width, work.X + gap, Math.Max(work.X + gap, work.X + work.Width - width - gap));
        int top = point.Y - height - gap;
        if (top < work.Y + gap) top = point.Y + gap;
        top = Math.Clamp(top, work.Y + gap, Math.Max(work.Y + gap, work.Y + work.Height - height - gap));
        return new(left, top, width, height);
    }
    internal void InvokeAction(int index)
    {
        // Re-check live cached eligibility: an already-open menu must not bypass a later fault.
        if (!IsOpen || disposed || index < 0 || index >= actions.Count) return;
        byte code = index is 1 or 2 ? (byte)0x10 : (byte)0x62;
        if (index is >= 1 and <= 4 && (!actions[index].IsEnabled || !canAdjust(code))) { Dismiss(); return; }
        Dismiss();
        if (index == 0) open();
        else if (index == 5) exit();
        else adjust(code, index is 1 or 3 ? AdjustmentStep : -AdjustmentStep);
    }
    internal bool HandleKey(VirtualKey key)
    {
        if (!IsOpen) return false;
        if (key == VirtualKey.Escape) { Dismiss(); return true; }
        if (key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)) return false;
        var available = NavigationActions();
        int current = available.FindIndex(b => b.FocusState != FocusState.Unfocused);
        int delta = key == VirtualKey.Up ? -1 : 1;
        int next;
        if (key == VirtualKey.Home || current < 0 && key == VirtualKey.Down) next = 0;
        else if (key == VirtualKey.End || current < 0) next = available.Count - 1;
        else next = (current + delta + available.Count) % available.Count;
        available[next].Focus(FocusState.Keyboard); return true;
    }
    List<Button> NavigationActions() => actions.Take(1).Concat(new[] { recovery }).Concat(actions.Skip(1)).Where(b => b != null && b.IsEnabled && b.Visibility == Visibility.Visible).ToList();
    internal void InvokeRecovery()
    {
        if (!IsOpen || disposed) return;
        bool allowed = recovery.IsEnabled && canReadAgain(); Dismiss();
        if (allowed) readAgain();
    }
    public void Dismiss() { if (!IsOpen) return; IsOpen = false; AppWindow.Hide(); }
    public void Dispose() { if (disposed) return; Dismiss(); disposed = true; Close(); }

    // Captures this app's own XAML surface only. No desktop capture or setting requests.
    internal async Task Capture(string path)
    {
        root.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        using var pixels = DataReader.FromBuffer(await bitmap.GetPixelsAsync()); byte[] data = new byte[pixels.UnconsumedBufferLength]; pixels.ReadBytes(data);
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, data); await encoder.FlushAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync(checked((uint)stream.Size)); byte[] png = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(png);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); await file.WriteAsync(png);
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
}
