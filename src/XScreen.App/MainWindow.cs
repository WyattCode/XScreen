using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Globalization.NumberFormatting;
using XScreen.Core;

namespace XScreen.App;

sealed partial class MainWindow : Window
{
    readonly MonitorController controller;
    readonly bool renderOnly;
    readonly RenderTransport? renderTransport;
    readonly Grid root = new(), channels = new();
    readonly Grid windowRoot = new();
    readonly TitleBar titleBar = new() { Title = Program.WindowTitle, RequestedTheme = ElementTheme.Dark };
    readonly Grid layout = new() { Padding = new Thickness(30, 12, 30, 14), RowSpacing = 12 };
    readonly Dictionary<byte, ControlChannel> rows = [];
    readonly HashSet<Task> operations = [];
    readonly Dictionary<byte, (ControlChannel Row, ControlIntent Intent)> pendingWrites = [];
    readonly Queue<byte> commitOrder = new();
    readonly TextBlock connection = Text("正在连接显示器…", 12), mode = Text("—", 24, true), contrast = Text("—", 24, true), black = Text("—", 24, true), lastRead = Text("尚未读取", 12);
    readonly Button refresh = Command("刷新", Symbol.Refresh);
    readonly Button reset = Command("重置设置", Symbol.Undo);
    readonly InfoBar notice = new() { IsOpen = false, IsClosable = true };
    readonly ComboBox appearance = new() { MinWidth = 150, SelectedIndex = 0 };
    // Null in every offline renderer/test unless an isolated test path is explicitly injected.
    internal string? AppearancePath { get; set; }
    readonly ScrollViewer scroll;
    readonly Image backdrop, logo;
    NativeWindow? native;
    TrayMenuWindow? trayMenu;
    OsdComparisonView? osdComparison;
    ResetSettingsView? resetView;
    MonitorSnapshot? latestSnapshot;
    CancellationTokenSource? resetCancellation;
    bool resetDialogOpen, resetting;
    OsdControlsView? advanced;
    readonly SelectorBar pages = new();
    ScrollViewer? advancedScroll;
    Grid? statusShelf;
    MonitorDevice? selected;
    bool ready, exiting, closeApproved, refreshing, busy, halted, initialPlacementPrepared;
    readonly List<object> captures = [];
    internal Func<bool> RemoteSessionCheck { get; set; }

    public MainWindow(IMonitorTransport transport, bool isRenderOnly)
    {
        AppLog.Mode = isRenderOnly ? "offline" : "live";
        controller = new(new TracingMonitorTransport(transport, trace => AppLog.Write("ddc-io", JsonSerializer.Serialize(new
        { trace, nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts })), AppLog.Mode));
        renderOnly = isRenderOnly; renderTransport = transport as RenderTransport;
        WindowStatePath = isRenderOnly ? null : WindowStateStore.DefaultPath;
        HotkeyPath = isRenderOnly ? null : HotkeyStore.DefaultPath;
        AppearancePath = isRenderOnly ? null : AppearanceStore.DefaultPath;
        RemoteSessionCheck = isRenderOnly ? () => false : SessionEnvironment.IsRemoteDesktop;
        // Loaded once here so the constructor can apply it before the first frame.
        var savedAppearance = AppearancePath is { } appearanceFile ? AppearanceStore.Load(appearanceFile) : null;
        Title = Program.WindowTitle;
        // Seed the requested theme from the saved choice so no later re-assignment is needed.
        root.RequestedTheme = (savedAppearance?.Selection ?? 0) switch { 0 => ElementTheme.Dark, 1 => ElementTheme.Light, _ => ElementTheme.Default };
        root.Background = Palette.Ground;
        foreach (string key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed", "AccentFillColorDefaultBrush", "SystemControlHighlightAccentBrush" }) root.Resources[key] = Palette.Accent;
        root.Resources["SystemAccentColor"] = Windows.UI.Color.FromArgb(255, 229, 0, 70);
        root.Resources["SliderTrackThemeHeight"] = 10d;
        root.Resources["SliderTrackCornerRadius"] = new CornerRadius(5);
        foreach (string key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) root.Resources[key] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 53, 53, 59));
        // Thumb colour is set in UpdatePalette along with the rest of the theme, because a fixed
        // white disc disappears against the light ground.
        // Faded so the diagonal artwork reads as background texture instead of competing with
        // the numerals and the rail.
        backdrop = new() { Source = Asset("UltraGearBackdrop.png"), Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Opacity = .4 };
        AutomationProperties.SetAccessibilityView(backdrop, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.Children.Add(backdrop); root.Children.Add(layout);
        foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) layout.RowDefinitions.Add(new() { Height = h });
        var header = new Grid { ColumnSpacing = 22, MinHeight = 82, Padding = new Thickness(0, 0, 0, 12), BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) header.ColumnDefinitions.Add(new() { Width = w });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, VerticalAlignment = VerticalAlignment.Center };
        logo = new() { Source = Asset("LG-Dark.svg"), Width = 109, Height = 48, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(logo, "LG"); brand.Children.Add(logo); brand.Children.Add(new Border { Width = 1, Height = 28, Background = Palette.Line });
        var ultra = Text("ULTRAGEAR", 19, true); ultra.CharacterSpacing = 210; ultra.VerticalAlignment = VerticalAlignment.Center; brand.Children.Add(ultra); header.Children.Add(brand);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        refresh.Click += (_, _) => Track(RefreshAsync(manual: true)); commands.Children.Add(refresh);
        reset.IsEnabled = false; reset.Click += (_, _) => Track(ShowResetDialogAsync()); commands.Children.Add(reset);
        ToolTipService.SetToolTip(reset, "重置两个页面的六项可调设置，对比度除外；先查看目标值再确认");
        var tray = Command("收起到托盘", Symbol.BackToWindow); tray.Click += (_, _) => HideToTray(); commands.Children.Add(tray);
        Grid.SetColumn(commands, 1); header.Children.Add(commands);
        var product = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 };
        product.Children.Add(Text("32GX870A", 27, true)); product.Children.Add(connection); Grid.SetColumn(product, 2); header.Children.Add(product); layout.Children.Add(header);
        for (int i = 0; i < ControlSpec.Writable.Length; i++)
        {
            channels.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var row = new ControlChannel(ControlSpec.Writable[i], OnControlChanged, UpdateRefresh); rows.Add(row.Spec.Code, row);
            var host = new Border { Child = row.View, Padding = new Thickness(22, 8, 22, 8), BorderBrush = Palette.Line, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
            Grid.SetColumn(host, i); channels.Children.Add(host);
        }
        scroll = new() { Content = channels, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ThinScrollBar(scroll);
        advanced = new OsdControlsView(ReadPictureStateAsync, WriteOsdAsync, Track, UpdateRefresh);
        advancedScroll = new() { Content = advanced.View, Visibility = Visibility.Collapsed, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ThinScrollBar(advancedScroll);
        var pageHost = new Grid { RowSpacing = 8 }; pageHost.RowDefinitions.Add(new() { Height = GridLength.Auto }); pageHost.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var quickPage = new SelectorBarItem { Text = "快速控制" }; var picturePage = new SelectorBarItem { Text = "画面与色彩" };
        pages.Items.Add(quickPage); pages.Items.Add(picturePage); pages.SelectedItem = quickPage; AutomationProperties.SetName(pages, "控制页面");
        pages.SelectionChanged += (_, _) => { bool picture = pages.SelectedItem == picturePage; scroll.Visibility = picture ? Visibility.Collapsed : Visibility.Visible; advancedScroll.Visibility = picture ? Visibility.Visible : Visibility.Collapsed; if (statusShelf != null) statusShelf.Visibility = picture ? Visibility.Collapsed : Visibility.Visible; };
        pageHost.Children.Add(pages); Grid.SetRow(scroll, 1); pageHost.Children.Add(scroll); Grid.SetRow(advancedScroll, 1); pageHost.Children.Add(advancedScroll);
        Grid.SetRow(pageHost, 1); layout.Children.Add(pageHost);
        var shelf = new Grid { ColumnSpacing = 28, MinHeight = 134, Padding = new Thickness(12, 18, 12, 12), BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 1) };
        statusShelf = shelf;
        foreach (double w in new[] { .7, 1, 1, 1 }) shelf.ColumnDefinitions.Add(new() { Width = new GridLength(w, GridUnitType.Star) });
        var statusTitle = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; statusTitle.Children.Add(Text("当前状态", 20, true)); statusTitle.Children.Add(lastRead); shelf.Children.Add(statusTitle);
        AddStatus(shelf, 1, "画面模式", mode, "在画面与色彩中切换"); AddStatus(shelf, 2, "对比度", contrast, "当前受限，仅显示读取值"); AddStatus(shelf, 3, "黑色稳定器", black, "调节步长 5"); Grid.SetRow(shelf, 2); layout.Children.Add(shelf);
        var footer = new Grid { ColumnSpacing = 12, MinHeight = 30 };
        foreach (var w in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) footer.ColumnDefinitions.Add(new() { Width = w });
        appearance.Items.Add("UltraGear 深色"); appearance.Items.Add("UltraGear 浅色"); appearance.Items.Add("跟随 Windows"); AutomationProperties.SetName(appearance, "界面外观");
        // Restore the saved choice before the change handler exists, so reading it back
        // is never mistaken for a user edit and re-saved. The requested theme was already
        // seeded above; the normal ActualThemeChanged path finishes the palette work.
        if (savedAppearance is { } restored) appearance.SelectedIndex = restored.Selection;
        appearance.SelectionChanged += (_, _) => { ApplyAppearance(appearance.SelectedIndex); if (AppearancePath is { } path) AppearanceStore.Save(path, new(appearance.SelectedIndex)); }; Grid.SetColumn(appearance, 1); footer.Children.Add(appearance);
        var more = new Button { Content = new SymbolIcon(Symbol.More), MinWidth = 36 }; AutomationProperties.SetName(more, "更多操作");
        var menu = new MenuFlyout();
        var hotkeySettings = new MenuFlyoutItem { Text = "全局快捷键", Icon = new SymbolIcon(Symbol.Keyboard) }; hotkeySettings.Click += (_, _) => Track(ShowHotkeySettingsAsync()); menu.Items.Add(hotkeySettings);
        var quit = new MenuFlyoutItem { Text = "退出 XScreen", Icon = new SymbolIcon(Symbol.Cancel) }; quit.Click += (_, _) => Track(ExitAsync()); menu.Items.Add(quit); more.Flyout = menu;
        Grid.SetColumn(more, 2); footer.Children.Add(more); Grid.SetRow(footer, 3); layout.Children.Add(footer);
        ConfigureFloatingNotice(footer);
        // Use one WinUI layout for the title and icon instead of separate native caption metrics.
        titleBar.Title = Program.WindowTitle;
        titleBar.Resources["TitleBarIconMargin"] = new Thickness(0, 0, 8, 0);
        titleBar.Resources["TitleBarTitleMargin"] = new Thickness(0, 0, 8, 0);
        titleBar.Loaded += (_, _) => AlignCaption(titleBar);
        titleBar.Background = windowRoot.Background = Palette.Ground;
        titleBar.IconSource = new ImageIconSource { ImageSource = Asset("XScreen.png") };
        windowRoot.RowDefinitions.Add(new() { Height = GridLength.Auto });
        windowRoot.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        windowRoot.Children.Add(titleBar); Grid.SetRow(root, 1); windowRoot.Children.Add(root); Content = windowRoot;
        ExtendsContentIntoTitleBar = true; SetTitleBar(titleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "XScreen.ico"));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Dark; AppWindow.TitleBar.BackgroundColor = AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 24, 24, 27); AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        // Minimum size comes from the store's constants so it cannot drift from the restore floor.
        // A stale 680 here let the window shrink far below what the control column needs.
        if (AppWindow.Presenter is OverlappedPresenter p) { p.PreferredMinimumWidth = (int)WindowStateStore.MinimumWidth; p.PreferredMinimumHeight = (int)WindowStateStore.MinimumHeight; }
        AppWindow.Closing += (_, args) =>
        {
            if (closeApproved) return;
            args.Cancel = true;
            // An exit request is not permission to destroy a window whose I/O is draining.
            if (exiting) return;
            if (native?.TrayAvailable == true) HideToTray();
            // A missing tray must not bypass connection disposal or write verification.
            // Queue the exit so final Close cannot re-enter this system close event.
            else DispatcherQueue.TryEnqueue(() => Track(ExitAsync()));
        };
        try { resizeObserver = new(WinRT.Interop.WindowNative.GetWindowHandle(this), OnMoveSize); }
        catch (Exception ex) { AppLog.Write("window-resize-observer", ex.ToString()); }
        Closed += (_, _) => { windowStateTimer?.Stop(); resizeObserver?.Dispose(); hotkeys?.Dispose(); trayMenu?.Dispose(); native?.Dispose(); }; root.ActualThemeChanged += (_, _) => UpdatePalette();
        // RequestedTheme was seeded near the top of this constructor, so ActualTheme may
        // already be resolved before the handler above exists and no change event follows.
        // Painting the palette once here makes the seeded choice visible on the first frame.
        UpdatePalette();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPresenterChange) QueueWindowStateSave();
        };
        root.SizeChanged += (_, e) =>
        {
            // The normal layout needs about 1010 logical pixels (620 for the control column plus
        // 390 of header, shelf and footer). Switching below that keeps the normal metrics from
        // being forced into a window too short for them, which showed up as a lone scrollbar
    // rail down the right edge. The compact layout needs about 820, so 1000 leaves both
    // branches slack and still lets the window shrink well below its default size.
            bool compact = e.NewSize.Height < 1010;
            ultra.Visibility = e.NewSize.Width < 1050 ? Visibility.Collapsed : Visibility.Visible;
            header.MinHeight = compact ? 66 : 82; shelf.MinHeight = compact ? 100 : 134; layout.RowSpacing = compact ? 8 : 12;
            foreach (var row in rows.Values) row.Compact(compact);
        };
        root.Loaded += async (_, _) =>
        {
            if (ready) return; ready = true; PrepareInitialPlacement();
            if (!renderOnly)
            {
                try
                {
                    native = new NativeWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), Path.Combine(AppContext.BaseDirectory, "Assets", "XScreen.Tray.ico"));
                    native.OpenRequested += ShowWindow;
                    native.ContextMenuRequested += point => DispatcherQueue.TryEnqueue(() => ShowTrayMenu(point));
                }
                catch (Exception ex)
                {
                    AppLog.Write("tray-init", ex.ToString());
                    ShowNotice("托盘初始化失败", "窗口将保持可用，无法收起到托盘；可在更多操作中退出。", InfoBarSeverity.Informational);
                }
            }
            var startupRead = RefreshAsync(); Track(startupRead); await startupRead;
            if (exiting) return;
            if (!renderOnly)
            {
                try { InitializeHotkeys(new NativeHotkeyRegistrar(WinRT.Interop.WindowNative.GetWindowHandle(this))); }
                catch (Exception ex) { AppLog.Write("hotkeys-init", ex.ToString()); HotkeyWarning("快捷键服务未能初始化，请重新启动程序。"); }
            }
            AppLog.Write("startup", JsonSerializer.Serialize(new { mode = renderOnly ? "offline-render" : "live", nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, automaticSettingTests = false }));
            if (Program.RenderDirectory is { } directory) await RenderRound(directory);
            else if (Program.PassiveCapture is { } path) { await Task.Delay(500); await Capture(path); }
            if (Program.ShowTray) { ShowTrayMenu(native?.GetTrayAnchor() ?? new PointInt32(800, 600)); if (Program.TrayCapture is { } trayPath) { await Task.Delay(500); await trayMenu!.Capture(trayPath); } }
        };
    }
    // The single precondition every explicit monitor request must clear. Kept in one place
    // so a new blocking state cannot be added to one entry point and missed by the others.
    // Returns the confirmed device rather than a bool: the null/unsupported check and the
    // value handed to the caller then stay linked, which a plain boolean cannot express.
    // `refreshing` counts a read in flight, so callers must not set it before checking.
    MonitorDevice? IdleDevice
    {
        get
        {
            if (exiting || busy || refreshing || resetDialogOpen || hotkeyDialogOpen || halted
                || controller.Halted || rows.Values.Any(r => r.Pending)) return null;
            return selected is { Supported: true } device ? device : null;
        }
    }
    bool CanAdjust(byte code) => ready && !exiting && !refreshing && !resetDialogOpen && !hotkeyDialogOpen && !resetting && advanced?.Busy != true && !halted && !controller.Halted && selected?.Supported == true && rows.TryGetValue(code, out var row) && row.Available;
    void Track(Task task) { operations.Add(task); _ = task.ContinueWith(_ => DispatcherQueue.TryEnqueue(() => operations.Remove(task)), TaskScheduler.Default); }
    async Task RefreshAsync(bool manual = false)
    {
        bool readAgain = manual && halted && controller.CanReadAgain && selected != null;
        if (exiting || refreshing || busy || advanced?.Busy == true || resetDialogOpen || hotkeyDialogOpen || rows.Values.Any(r => r.Pending) || (halted || controller.Halted) && !readAgain) return;
        trayMenu?.Dismiss(); refreshing = true; EnableControls(false); refresh.IsEnabled = false; connection.Text = "正在读取…";
        try
        {
            MonitorSnapshot? recovered = null;
            if (readAgain) { recovered = await controller.ReadAgainAsync(selected!); halted = false; }
            var candidates = recovered == null ? (await controller.EnumerateAsync()).Where(d => d.Supported).ToArray() : new[] { selected! };
            if (exiting) return;
            // Never fall back to an arbitrary monitor or broaden identity matching.
            selected = candidates.Length == 1 ? candidates[0] : null;
            if (selected == null)
            {
                latestSnapshot = null;
                foreach (var row in rows.Values) row.Clear(); mode.Text = contrast.Text = black.Text = "—";
                advanced?.Clear();
                bool remote = candidates.Length == 0 && RemoteSessionCheck();
                connection.Text = candidates.Length > 1 ? "检测到多个受支持设备" : remote ? "远程桌面 · 实体显示器不可用" : "未找到受支持的显示器"; lastRead.Text = "状态不可用";
                ShowNotice(remote ? "远程会话暂不可控制" : "未启用控制", candidates.Length > 1 ? "请只连接一台目标显示器后刷新。" : remote ? "当前远程桌面会话未提供受支持的实体显示器。回到这台电脑本地登录后，手动刷新即可重新检测；不会自动发送设置。" : "当前会话未找到目标显示器。若正在使用远程桌面或远程控制，请回到这台电脑本地登录后手动刷新；若已在本地，请检查连接与 DDC/CI。", InfoBarSeverity.Informational); AppLog.Write("read-unavailable", connection.Text); return;
            }
            var snapshot = recovered ?? await controller.ReadAsync(selected); if (exiting) return;
            PresentSnapshot(snapshot);
            AppLog.Write("read", JsonSerializer.Serialize(new { selected.Name, selected.Display, at = snapshot.At, values = snapshot.Values, source = renderOnly ? "offline-sample" : "monitor" }));
        }
        catch (Exception ex) { Halt("读取失败", ex); }
        finally { refreshing = false; UpdateRefresh(); EnableControls(!halted && !exiting); }
    }
    static string Value(MonitorValue? v) => v?.Success == true ? v.Current.ToString() : "—";
    void PresentSnapshot(MonitorSnapshot snapshot, bool loadEditors = true)
    {
        latestSnapshot = snapshot with { Values = Array.AsReadOnly(snapshot.Values.ToArray()) };
        foreach (var row in rows.Values) row.Load(snapshot.Get(row.Spec.Code));
        if (loadEditors) advanced?.Load(snapshot);
        string Display(OsdControl control) => snapshot.Get(OsdProtocol.Code(control)) is { } value
            ? OsdProtocol.Decode(control, value).Display : "—";
        mode.Text = Display(OsdControl.PictureMode); contrast.Text = Display(OsdControl.Contrast); black.Text = Display(OsdControl.BlackStabilizer);
        connection.Text = "已连接 · DDC/CI"; lastRead.Text = "读取于 " + snapshot.At.ToString("HH:mm:ss"); notice.IsOpen = false;
        if (hotkeys?.LastError is { } hotkeyError) HotkeyWarning(hotkeyError);
    }
    bool CanOpenReset() => ready && !hotkeyDialogOpen && !exiting && !busy && !refreshing && !halted && !controller.Halted && advanced?.Busy != true
        && selected?.Supported == true && !rows.Values.Any(r => r.Pending) && rows.Values.All(r => r.Available)
        && latestSnapshot != null && OsdProtocol.Controls.All(c => latestSnapshot.Get(OsdProtocol.Code(c)) is { } value && OsdProtocol.Decode(c, value).Recognized);
    async Task ShowResetDialogAsync()
    {
        if (resetDialogOpen || !CanOpenReset()) return;
        resetDialogOpen = true; trayMenu?.Dismiss(); EnableControls(false); UpdateRefresh();
        string deviceId = selected!.Id;
        try
        {
            resetView = new(root.XamlRoot, latestSnapshot!);
            bool confirmed = await resetView.ShowAsync(root.ActualTheme);
            if (!confirmed || exiting || selected?.Id != deviceId || !CanOpenReset()) return;
            busy = resetting = true; EnableControls(false); UpdateRefresh();
            using var cancellation = new CancellationTokenSource(); resetCancellation = cancellation;
            try
            {
                AppLog.Write("user-reset-request", JsonSerializer.Serialize(new { source = "explicit-confirmed-six-settings", targets = AppResetProfile.Settings }));
                ShowNotice("正在重置", "正在依次核对六项可调设置，请稍候…", InfoBarSeverity.Informational);
                var result = await controller.ResetToAppDefaultsAsync(selected!, cancellation.Token);
                if (!exiting)
                {
                    PresentSnapshot(result.Snapshot);
                    ShowNotice("重置完成", result.WriteCount == 0 ? "六项可调设置已是应用默认值，没有重复写入。" : "六项可调设置已回读确认，两个页面已同步。", InfoBarSeverity.Success);
                }
                AppLog.Write("reset-readback-match", JsonSerializer.Serialize(new { result.WriteCount, result.Snapshot.At, values = result.Snapshot.Values, fullDisplayRecovery = "unconfirmed" }));
            }
            catch (OperationCanceledException) { AppLog.Write("reset-cancelled", "Cancelled before settings were sent."); }
            catch (Exception ex) { AppLog.Write("reset-failed", ex.ToString()); if (!exiting) Halt("重置未完成", ex); }
            finally { resetCancellation = null; resetting = busy = false; }
        }
        catch (Exception ex) { AppLog.Write("reset-dialog-error", ex.ToString()); if (!exiting) ShowNotice("重置暂不可用", "请关闭弹窗后重试。", InfoBarSeverity.Warning); }
        finally { resetDialogOpen = false; resetView = null; UpdateRefresh(); EnableControls(!halted && !exiting); }
    }
    async Task<MonitorSnapshot> ReadPictureStateAsync()
    {
        if (IdleDevice is not { } device)
            throw new InvalidOperationException("当前无法刷新。请等待当前调节完成，并确认显示器已连接且会话未暂停。");
        refreshing = true; EnableControls(false); UpdateRefresh(); trayMenu?.Dismiss();
        try
        {
            // The ordinary pane must refresh the quick controls and summary as well.
            // The separate diagnostic comparison retains its explicit four-field capture.
            var snapshot = await controller.ReadAsync(device);
            if (exiting) throw new OperationCanceledException("程序正在退出。");
            PresentSnapshot(snapshot, loadEditors: false);
            AppLog.Write("picture-state-read", JsonSerializer.Serialize(new { snapshot.At, values = snapshot.Values, source = renderOnly ? "offline-sample" : "monitor" }));
            return snapshot;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Halt("画面状态读取失败", ex); throw; }
        finally { refreshing = false; UpdateRefresh(); EnableControls(!halted && !exiting); }
    }
    async Task ShowOsdComparisonAsync()
    {
        if (exiting || !ready) return;
        osdComparison ??= new OsdComparisonView(root.XamlRoot, ReadOsdComparisonAsync, Track);
        try { await osdComparison.ShowAsync(root.ActualTheme); }
        catch (Exception ex) { AppLog.Write("osd-dialog-error", ex.ToString()); if (!exiting) ShowNotice("对照窗口暂不可用", ex.Message, InfoBarSeverity.Warning); }
    }
    async Task<MonitorSnapshot> ReadOsdComparisonAsync()
    {
        if (IdleDevice is not { } device)
            throw new InvalidOperationException("当前无法采集。请先确认已连接且没有调节、读取或故障待处理。");
        refreshing = true; EnableControls(false); UpdateRefresh(); trayMenu?.Dismiss();
        try
        {
            var snapshot = await controller.ReadOsdComparisonAsync(device);
            if (exiting) throw new OperationCanceledException("程序正在退出。");
            return snapshot;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Halt("OSD 对照读取失败", ex); throw; }
        finally { refreshing = false; UpdateRefresh(); EnableControls(!halted && !exiting); }
    }
    async Task<OsdWriteResult> WriteOsdAsync(OsdControl control, uint target)
    {
        // Reject stale or programmatic contrast requests before busy/logging/fault state.
        MonitorSafetyPolicy.EnsureWriteAllowed(OsdProtocol.Code(control));
        if (IdleDevice is not { } device)
            throw new InvalidOperationException("请等待当前调节完成，并确认显示器已连接且会话未暂停。");
        busy = true; EnableControls(false); UpdateRefresh(); trayMenu?.Dismiss();
        try
        {
            var command = OsdProtocol.DescribeCandidate(control, target);
            AppLog.Write("user-write-request", JsonSerializer.Serialize(new { code = command.Code, target, encodedTarget = command.EncodedValue, source = "explicit-osd-apply", hardwareWriteVerified = false }));
            var result = await controller.WriteOsdAsync(device, control, target);
            if (!exiting)
            {
                PresentSnapshot(result.Snapshot, loadEditors: false);
            }
            AppLog.Write("osd-apply-readback", JsonSerializer.Serialize(new { control, result.Previous, result.Actual, result.EncodedTarget, result.WasWritten,
                result.Snapshot.At, values = result.Snapshot.Values, fullDisplayRecovery = "unconfirmed", hardwareAcceptance = "pending-user-test" }));
            return result;
        }
        catch (Exception ex) { Halt(OsdProtocol.Name(control) + "未能确认", ex); throw; }
        finally { busy = false; UpdateRefresh(); EnableControls(!halted && !exiting); }
    }
    void OnControlChanged(ControlChannel row, ControlIntent intent)
    {
        if (!CanAdjust(row.Spec.Code)) { row.CancelPending(); UpdateRefresh(); return; }
        // At most one deferred target per channel; native I/O remains strictly serial.
        if (!pendingWrites.ContainsKey(row.Spec.Code)) commitOrder.Enqueue(row.Spec.Code);
        pendingWrites[row.Spec.Code] = (row, intent);
        if (busy) return;
        busy = true; UpdateRefresh(); Track(CommitPendingAsync(selected!));
    }
    async Task CommitPendingAsync(MonitorDevice device)
    {
        try
        {
            while (commitOrder.Count > 0 && !halted && !exiting)
            {
                byte code = commitOrder.Dequeue();
                if (!pendingWrites.Remove(code, out var request)) continue;
                var (row, intent) = request;
                if (!row.IsCurrent(intent)) continue; // Newer edit is still being dragged/debounced.
                row.BeginCommit(intent);
                try
                {
                    AppLog.Write("user-write-request", $"code=0x{row.Spec.Code:X2};target={intent.Target};source={(renderOnly ? "offline-harness" : "explicit-control")}");
                    var result = await controller.WriteAsync(device, row.Spec, intent.Target); row.Accept(intent, result);
                    if (latestSnapshot is { } cached) latestSnapshot = cached with { At = result.At,
                        Values = Array.AsReadOnly(cached.Values.Select(v => v.Code == row.Spec.Code ? v with { Current = result.Actual } : v).ToArray()) };
                    AppLog.Write("parameter-readback-match", $"code=0x{row.Spec.Code:X2};previous={result.Previous};target={intent.Target};actual={result.Actual};fullDisplayRecovery=unconfirmed");
                }
                catch (Exception ex) { Halt(row.Spec.Name + "未能确认", ex); break; }
            }
        }
        finally { busy = false; UpdateRefresh(); }
    }
    void Halt(string title, Exception ex)
    {
        // Report the field whose read failed, not the requested setting. A color Apply
        // also pre-reads F9, so its failure must not be presented as a color fault.
        if (ex is ReadbackException { Actual: { } actual })
        {
            string? failedName = ControlSpec.Writable.SingleOrDefault(s => s.Code == actual.Code)?.Name;
            var failedOsd = OsdProtocol.Controls.Where(c => OsdProtocol.Code(c) == actual.Code).ToArray();
            if (failedName == null && failedOsd.Length == 1) failedName = OsdProtocol.Name(failedOsd[0]);
            if (failedName != null) title = failedName + "状态未能确认";
        }
        trayMenu?.Dismiss(); halted = true; pendingWrites.Clear(); commitOrder.Clear(); EnableControls(false); refresh.IsEnabled = false;
        latestSnapshot = null;
        foreach (var row in rows.Values) { row.Clear(); row.Feedback.Text = "当前值未确认"; }
        mode.Text = contrast.Text = black.Text = "—"; lastRead.Text = "当前状态未确认";
        advanced?.Clear(unconfirmed: true);
        bool readOnlyFault = controller.CanReadAgain;
        connection.Text = readOnlyFault ? "读取校验失败 · 本次未写入" : "控制已暂停";
        ShowNotice(title, readOnlyFault ? "读取校验未通过，本次调节没有发送。请点击“重新读取”确认状态，或在托盘菜单选择“重新读取状态”；不会重发调节。" : ex.Message + " 本次会话已暂停；检查显示器，确认正常后再手动重启程序。", InfoBarSeverity.Error);
        if (ex is ReadbackException detail) AppLog.Write("operation-failed", JsonSerializer.Serialize(new { detail.Phase, detail.WriteAttempted, detail.Actual, canReadAgain = controller.CanReadAgain, nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, inner = detail.InnerException?.Message }));
        AppLog.Write("session-halted", ex.ToString()); UpdateRefresh(); ShowWindow();
    }
    void UpdateRefresh()
    {
        advanced?.SetAvailable(!exiting && !busy && !refreshing && !resetDialogOpen && !hotkeyDialogOpen && !halted && !controller.Halted && selected?.Supported == true && !rows.Values.Any(r => r.Pending));
        reset.IsEnabled = !resetDialogOpen && !hotkeyDialogOpen && CanOpenReset();
        refresh.IsEnabled = !exiting && !busy && !refreshing && advanced?.Busy != true && !resetDialogOpen && !hotkeyDialogOpen && !rows.Values.Any(r => r.Pending) && (!halted || controller.CanReadAgain);
        if (refresh.Content is StackPanel content) content.Children.OfType<TextBlock>().Single().Text = halted && controller.CanReadAgain ? "重新读取" : "刷新";
    }
    void EnableControls(bool enabled) { foreach (var row in rows.Values) row.SetEnabled(enabled && !resetDialogOpen && !hotkeyDialogOpen && selected?.Supported == true); advanced?.SetAvailable(enabled && !resetDialogOpen && !hotkeyDialogOpen && !busy && !refreshing && selected?.Supported == true && !rows.Values.Any(r => r.Pending)); }
    void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        // A secondary UI warning must not hide the cause or recovery guidance of
        // an open hardware fault. Dismissal and successful recovery remain explicit.
        if (halted && notice.IsOpen && notice.Severity == InfoBarSeverity.Error && severity != InfoBarSeverity.Error) return;
        notice.Title = title; notice.Message = message; notice.Severity = severity; notice.IsOpen = true;
        // Replacing a visible message must also return to its beginning.
        noticeScroll.ChangeView(null, 0, null, true);
    }
    void HideToTray() { if (native?.TrayAvailable == true) { SaveWindowState(); AppWindow.Hide(); } else ShowNotice("托盘暂不可用", "窗口将保持打开。可在更多操作中退出。", InfoBarSeverity.Informational); }
    void ShowTrayMenu(PointInt32 point)
    {
        if (exiting) return;
        try
        {
            if (trayMenu == null)
            {
                trayMenu = new TrayMenuWindow(CanAdjust, ShowWindow, (code, delta) => { if (CanAdjust(code)) rows[code].Adjust(delta); }, () => Track(ExitAsync()),
                    () => ready && !exiting && !busy && !refreshing && !resetDialogOpen && !hotkeyDialogOpen && halted && controller.CanReadAgain,
                    () => Track(RefreshAsync(manual: true)));
                trayMenu.Failed += OnTrayMenuFailed;
            }
            trayMenu.ShowAt(point, connection.Text, root.ActualTheme);
        }
        catch (Exception ex) { OnTrayMenuFailed(ex); }
    }
    void OnTrayMenuFailed(Exception ex)
    {
        var failed = trayMenu; trayMenu = null;
        try { failed?.Dispose(); } catch (Exception cleanup) { AppLog.Write("tray-menu-cleanup", cleanup.ToString()); }
        AppLog.Write("tray-menu-failed", ex.ToString());
        if (exiting) return;
        ShowNotice("托盘菜单暂不可用", "可继续使用主窗口；再次右键可重新打开菜单。", InfoBarSeverity.Warning);
        ShowWindow();
    }
    void ShowWindow()
    {
        if (exiting) return;
        trayMenu?.Dismiss();
        // The same restoration path applies even when notification-area setup is absent.
        NativeWindow.ShowHandle(WinRT.Interop.WindowNative.GetWindowHandle(this)); Activate();
    }
    async Task ExitAsync()
    {
        if (exiting) return; SaveWindowState(); exiting = true; hotkeys?.Dispose(); hotkeyView?.Dismiss(); windowStateTimer?.Stop(); trayMenu?.Dismiss(); pendingWrites.Clear(); commitOrder.Clear(); EnableControls(false); foreach (var row in rows.Values) row.CancelPending();
        resetCancellation?.Cancel(); resetView?.Dismiss();
        if (operations.Count > 0) await Task.WhenAll(operations.ToArray());
        trayMenu?.Dispose(); native?.Dispose();
        closeApproved = true; Close(); Application.Current.Exit();
    }
    // Single place that maps the selector onto the requested theme. Only user-driven
    // changes call this; ActualTheme settles on the next frame and ActualThemeChanged
    // re-runs UpdatePalette with the resolved value. Construction seeds RequestedTheme
    // directly so the first frame is already correct.
    void ApplyAppearance(int selection)
    {
        root.RequestedTheme = selection switch { 0 => ElementTheme.Dark, 1 => ElementTheme.Light, _ => ElementTheme.Default };
        UpdatePalette();
    }
    void UpdatePalette()
    {
        bool dark = root.ActualTheme == ElementTheme.Dark; Palette.Apply(dark); backdrop.Visibility = dark ? Visibility.Visible : Visibility.Collapsed; logo.Source = Asset(dark ? "LG-Dark.svg" : "LG.svg");
        titleBar.RequestedTheme = root.ActualTheme; titleBar.Background = windowRoot.Background = Palette.Ground;
        // Secondary buttons do not follow the palette on their own.
        foreach (var row in rows.Values) row.ApplyButtonTheme();
        // The thumb keeps the ground colour so it reads as a disc punched out of the rail in
        // both themes; the accent halo behind it supplies the "this is draggable" cue.
        var thumb = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 246, 243, 245) : Windows.UI.Color.FromArgb(255, 24, 22, 25));
        root.Resources["SliderOuterThumbBackground"] = thumb;
        root.Resources["SliderThumbBorderBrush"] = thumb;
        trayMenu?.SetTheme(root.ActualTheme);
        osdComparison?.SetTheme(root.ActualTheme);
        resetView?.SetTheme(root.ActualTheme);
        hotkeyView?.SetTheme(root.ActualTheme);
        AppWindow.TitleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light; AppWindow.TitleBar.BackgroundColor = AppWindow.TitleBar.ButtonBackgroundColor = Palette.Ground.Color; AppWindow.TitleBar.ButtonForegroundColor = Palette.Primary.Color;
    }
    void ResizeLogical(int w, int h) { double scale = root.XamlRoot?.RasterizationScale ?? 1; AppWindow.Resize(new SizeInt32((int)Math.Round(w * scale), (int)Math.Round(h * scale))); }
    internal void PrepareInitialPlacement()
    {
        if (initialPlacementPrepared) return;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = Math.Max(1, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d);
        var saved = WindowStatePath == null ? null : WindowStateStore.Load(WindowStatePath);
        var bounds = saved == null ? InitialBounds(work, scale, renderOnly) : WindowStateStore.Bounds(work, scale, saved);
        rememberedWindowState = saved ?? new(bounds.Width / scale, bounds.Height / scale, false);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)Math.Ceiling(WindowStateStore.MinimumWidth * scale), bounds.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)Math.Ceiling(WindowStateStore.MinimumHeight * scale), bounds.Height);
        }
        AppWindow.MoveAndResize(bounds);
        if (saved?.Maximized == true && AppWindow.Presenter is OverlappedPresenter maximized) maximized.Maximize();
        initialPlacementPrepared = true;
    }
    internal static RectInt32 InitialBounds(RectInt32 work, double scale, bool offline)
    {
        int margin = Math.Min((int)Math.Ceiling(16 * scale), Math.Max(0, (Math.Min(work.Width, work.Height) - 1) / 2));
        int width = Math.Min((int)Math.Round((offline ? 1440 : 1220) * scale), Math.Max(1, work.Width - 2 * margin));
        // Tall enough for the 540px control column plus header, shelf and footer without the
        // column having to compress the rail.
        // Leaves headroom above the 390px of surrounding chrome plus the 620px control column;
        // sizing it to the exact sum clipped the channel icons by a few pixels.
        int height = Math.Min((int)Math.Round((offline ? 1120 : 1060) * scale), Math.Max(1, work.Height - 2 * margin));
        return new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    async Task RenderRound(string directory)
    {
        if (!renderOnly) throw new InvalidOperationException("Offline render requires its own sample transport.");
        Directory.CreateDirectory(directory); connection.Text = "离线界面检查"; foreach (var row in rows.Values) row.Feedback.Text = "示例数值";
        await Task.Delay(650); await Capture(Path.Combine(directory, "desktop.png"));
        ResizeLogical(1000, 800); await Task.Delay(400); await Capture(Path.Combine(directory, "compact.png"));
        var interactionChecks = await CheckSmoothInteractionsAsync(directory);
        // Change presentation only; never call any control handler during capture.
        foreach (var row in rows.Values) row.Clear(); selected = null; mode.Text = contrast.Text = black.Text = "—"; connection.Text = "离线断开示例";
        lastRead.Text = "状态不可用"; await Task.Delay(250); await Capture(Path.Combine(directory, "disconnected.png"));
        await RefreshAsync();
        if (renderTransport == null) throw new InvalidOperationException("Renderer requires its explicit fake.");
        int writesBeforeFaults = renderTransport.WriteAttempts;
        renderTransport.FailNextBrightnessRead = true; rows[0x10].Request(69);
        for (int i = 0; i < 30 && busy; i++) await Task.Delay(50);
        bool preWritePaused = halted && controller.CanReadAgain && refresh.IsEnabled && rows.Values.All(r => !r.Slider.IsEnabled && r.DisplayValue == "—");
        if (!preWritePaused || renderTransport.WriteAttempts != writesBeforeFaults) throw new InvalidOperationException("Pre-write checksum must pause without writing.");
        await Task.Delay(250); await Capture(Path.Combine(directory, "prewrite-checksum.png"));
        await RefreshAsync(manual: true);
        bool manualReadOnly = !halted && !controller.Halted && renderTransport.WriteAttempts == writesBeforeFaults && rows.Values.All(r => r.Slider.IsEnabled);
        if (!manualReadOnly) throw new InvalidOperationException("Manual state read must not replay the setting request.");
        await Task.Delay(250); await Capture(Path.Combine(directory, "manual-read-restored.png"));
        renderTransport.FailWrites = true;
        rows[0x10].Request(69); rows[0x10].Request(80); rows[0x62].Request(40);
        for (int i = 0; i < 30 && busy; i++) await Task.Delay(50);
        bool stopped = halted && controller.Halted && pendingWrites.Count == 0 && commitOrder.Count == 0 && renderTransport.WriteAttempts == writesBeforeFaults + 1 && rows.Values.All(r => !r.Pending && !r.Slider.IsEnabled && r.DisplayValue == "—");
        if (!stopped) throw new InvalidOperationException("Offline user-handler failure did not stop every control.");
        await Task.Delay(250); await Capture(Path.Combine(directory, "halted.png"));
        await File.WriteAllTextAsync(Path.Combine(directory, "render-report.json"), JsonSerializer.Serialize(new { at = DateTimeOffset.Now, source = "own-window RenderTargetBitmap", offline = true, nativeReadAttempts = WindowsMonitorTransport.NativeReadAttempts, nativeWriteAttempts = WindowsMonitorTransport.NativeWriteAttempts, automaticHardwareSettingTests = false, interactionChecks, fakePreWriteChecksumStopsWithoutWrite = preWritePaused, fakeManualReadDoesNotWrite = manualReadOnly, fakeHandlerFailureStopsAllControls = stopped, captures }, new JsonSerializerOptions { WriteIndented = true })); AppLog.Write("render-complete", directory); await ExitAsync();
    }
    async Task<object> CheckSmoothInteractionsAsync(string directory)
    {
        if (!renderOnly || renderTransport == null) throw new InvalidOperationException("Interaction tests require the isolated fake.");
        List<string> passed = []; List<object> frames = []; List<(string Case, byte Code, string Value)> changes = [];
        List<(ControlChannel Row, long ValueToken, long EnabledToken)> observers = [];
        string scenario = "prepare"; int disabledTransitions = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Interaction regression: " + label); passed.Add(label); }
        void Sample(object? sender, object args) => frames.Add(new { scenario, busy, refreshEnabled = refresh.IsEnabled, values = rows.Values.Select(r => new { r.Spec.Code, r.Current, r.Desired, r.Pending, text = r.DisplayValue, slider = r.Slider.Value, enabled = r.Slider.IsEnabled }).ToArray() });
        async Task Settled()
        {
            for (int i = 0; i < 500 && (busy || rows.Values.Any(r => r.Pending)); i++) await Task.Delay(10);
            Check(!busy && !halted && !controller.Halted && rows.Values.All(r => !r.Pending), scenario + " settles");
        }
        foreach (var row in rows.Values)
        {
            long valueToken = row.ValueLabel.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => changes.Add((scenario, row.Spec.Code, row.DisplayValue)));
            long enabledToken = row.Slider.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) => { if (!row.Slider.IsEnabled) disabledTransitions++; });
            observers.Add((row, valueToken, enabledToken));
        }
        CompositionTarget.Rendering += Sample;
        try
        {
            var brightness = rows[0x10]; brightness.Request(100); await Settled();
            foreach (uint target in new uint[] { 50, 100 })
            {
                scenario = target == 50 ? "100-to-50" : "50-to-100";
                brightness.Request(target);
                Check(brightness.DisplayValue == target.ToString() && brightness.Slider.Value == target && rows.Values.All(r => r.Slider.IsEnabled) && !refresh.IsEnabled, scenario + " keeps target and controls enabled during I/O");
                if (target == 50) await Capture(Path.Combine(directory, "adjustment-pending.png"));
                await Settled();
                Check(changes.Where(c => c.Case == scenario && c.Code == 0x10).All(c => c.Value == target.ToString()), scenario + " has no old-value text transition");
            }
            scenario = "rapid-multichannel";
            int start = renderTransport.WriteAttempts;
            brightness.Request(50); brightness.Request(60); brightness.Request(70); brightness.Request(80);
            rows[0x62].Request(31); rows[0x62].Request(32); rows[0x62].Request(33);
            rows[0x87].Request(60); rows[0x87].Request(70);
            Check(brightness.Desired == 80 && rows[0x62].Desired == 33 && rows[0x87].Desired == 70, "Every channel accepts edits while another write is pending");
            await Settled();
            Check(renderTransport.WriteAttempts - start == 4 && brightness.Current == 80 && rows[0x62].Current == 33 && rows[0x87].Current == 70, "Queued edits coalesce to one latest target per channel");
            scenario = "drag-during-readback"; start = renderTransport.WriteAttempts;
            brightness.Request(40); brightness.BeginDrag(); brightness.Slider.Value = 90;
            for (int i = 0; i < 500 && busy; i++) await Task.Delay(10);
            await Task.Delay(350);
            Check(brightness.DisplayValue == "90" && brightness.Slider.Value == 90 && brightness.Current == 40 && brightness.Pending && brightness.Slider.IsEnabled && !refresh.IsEnabled && renderTransport.WriteAttempts == start + 1, "Earlier readback cannot move an unfinished drag or send its target");
            brightness.FinishDrag(); brightness.FinishDrag(); await Settled();
            Check(brightness.Current == 90 && renderTransport.WriteAttempts == start + 2, "Release plus capture-loss submits only once");
            scenario = "keyboard-debounce"; start = renderTransport.WriteAttempts;
            brightness.Slider.Value = 70; brightness.Slider.Value = 60; brightness.Slider.Value = 50;
            await Settled();
            Check(brightness.Current == 50 && renderTransport.WriteAttempts == start + 1, "Native ValueChanged debounce sends only the final keyboard target");
            scenario = "repeated-plus"; start = renderTransport.WriteAttempts;
            brightness.Adjust(1); brightness.Adjust(1); brightness.Adjust(1);
            Check(brightness.DisplayValue == "53", "Repeated plus uses desired rather than old confirmed value");
            await Settled(); Check(brightness.Current == 53 && renderTransport.WriteAttempts == start + 2, "Repeated plus confirms first and latest values only");
            scenario = "refresh-read-only"; start = renderTransport.WriteAttempts;
            // Refresh legitimately disables controls, so stop the successful-write observer here.
            Check(disabledTransitions == 0, "Healthy adjustment never disables any slider");
            var frameData = JsonSerializer.SerializeToElement(frames);
            foreach (var (name, expected) in new (string, uint)[] { ("100-to-50", 50), ("50-to-100", 100), ("rapid-multichannel", 80), ("drag-during-readback", 90), ("keyboard-debounce", 50), ("repeated-plus", 53) })
            {
                var relevant = frameData.EnumerateArray().Where(f => f.GetProperty("scenario").GetString() == name).ToArray();
                Check(relevant.Length > 0 && relevant.All(f => f.GetProperty("values").EnumerateArray().All(v => v.GetProperty("enabled").GetBoolean()) && f.GetProperty("values")[0].GetProperty("text").GetString() == expected.ToString() && f.GetProperty("values")[0].GetProperty("slider").GetDouble() == expected), name + " remains stable in every sampled render frame");
            }
            foreach (var (row, valueToken, enabledToken) in observers)
            { row.ValueLabel.UnregisterPropertyChangedCallback(TextBlock.TextProperty, valueToken); row.Slider.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, enabledToken); }
            observers.Clear();
            await RefreshAsync(manual: true);
            Check(renderTransport.WriteAttempts == start, "Binding a manual refresh never writes");
            return new { passed, healthyAdjustmentDisabledTransitions = disabledTransitions, sampledFrames = frames.Count, uiChanges = changes.Select(c => new { c.Case, c.Code, c.Value }).ToArray() };
        }
        finally
        {
            CompositionTarget.Rendering -= Sample;
            foreach (var (row, valueToken, enabledToken) in observers)
            { row.ValueLabel.UnregisterPropertyChangedCallback(TextBlock.TextProperty, valueToken); row.Slider.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, enabledToken); }
            await File.WriteAllTextAsync(Path.Combine(directory, "interaction-frames.json"), JsonSerializer.Serialize(frames, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    Task Capture(string path) => CaptureElement(path, root);
    internal Task CaptureTitleBar(string path) => CaptureElement(path, titleBar);
    static void AlignCaption(DependencyObject parent)
    {
        // The shipped TitleBar template pre-resolves CaptionTextBlockStyle. Adjust its
        // title part directly so glyph bounds, rather than font line leading, are centered.
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock { Name: "PART_TitleText" } title) title.TextLineBounds = TextLineBounds.Tight;
            else AlignCaption(child);
        }
    }
    async Task CaptureElement(string path, FrameworkElement element)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        using var pixels = DataReader.FromBuffer(await bitmap.GetPixelsAsync()); byte[] data = new byte[pixels.UnconsumedBufferLength]; pixels.ReadBytes(data);
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, data); await encoder.FlushAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync(checked((uint)stream.Size)); byte[] png = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(png);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); await file.WriteAsync(png);
        captures.Add(new { path, width = bitmap.PixelWidth, height = bitmap.PixelHeight, logicalWidth = root.ActualWidth, logicalHeight = root.ActualHeight, scrollableHeight = scroll.ScrollableHeight, halted, values = rows.Values.Select(r => new { r.Spec.Name, lastKnown = r.Current, displayedValue = r.DisplayValue, r.Available, enabled = r.Slider.IsEnabled }).ToArray() }); AppLog.Write("passive-capture", path);
    }
    // A window can land a pixel short of its content at some DPI and the Auto scrollbar then
// appears as a full-height rail, which reads as a layout crack rather than as scrolling.
// Trimming the bar keeps that case quiet without changing whether content scrolls.
    static void ThinScrollBar(ScrollViewer viewer)
    {
        viewer.Resources["ScrollBarSize"] = 6d;
        viewer.Resources["ScrollBarVerticalThumbMinHeight"] = 24d;
        viewer.Resources["ScrollBarTrackFill"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        viewer.Resources["ScrollBarTrackFillPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        viewer.Resources["ScrollBarPanningThumbBackground"] = Palette.Line;
        viewer.Resources["ScrollBarThumbFill"] = Palette.Line;
        viewer.Resources["ScrollBarThumbFillPointerOver"] = Palette.Secondary;
    }
    static ImageSource Asset(string f) => f.EndsWith(".svg") ? new SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", f))) : new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", f)));
    static void AddStatus(Grid parent, int column, string label, TextBlock value, string helper)
    {
        var group = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center }; group.Children.Add(Text(label, 13)); group.Children.Add(value); group.Children.Add(Text(helper, 12));
        var border = new Border { Child = group, BorderBrush = Palette.Line, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(22, 0, 0, 0) }; Grid.SetColumn(border, column); parent.Children.Add(border);
    }
    internal static TextBlock Text(string s, double size, bool strong = false) => new() { Text = s, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Foreground = size <= 14 ? Palette.Secondary : Palette.Primary, TextWrapping = TextWrapping.Wrap };
    static Button Command(string text, Symbol icon)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; content.Children.Add(new SymbolIcon(icon) { VerticalAlignment = VerticalAlignment.Center }); content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextLineBounds = TextLineBounds.Tight });
        var b = new Button { Content = content, MinHeight = 36, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) }; AutomationProperties.SetName(b, text); return b;
    }
}

static class Palette
{
    public static readonly SolidColorBrush Ground = Brush(17, 17, 19), Primary = Brush(246, 243, 245), Secondary = Brush(186, 182, 189), Line = Brush(50, 49, 54), Accent = Brush(229, 0, 70);
    public static readonly SolidColorBrush Error = Brush(255, 163, 181);
    // Surface for secondary buttons that carry no accent: the WinUI default is nearly
    // invisible on the light ground, so both themes paint it explicitly.
    public static readonly SolidColorBrush ControlSurface = Brush(53, 53, 59);
    public static readonly SolidColorBrush ControlSurfaceHover = Brush(66, 66, 73), ControlSurfacePressed = Brush(44, 44, 49);
    static SolidColorBrush Brush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
    public static void Apply(bool dark)
    {
        Ground.Color = dark ? Windows.UI.Color.FromArgb(255, 17, 17, 19) : Windows.UI.Color.FromArgb(255, 249, 247, 248);
        Primary.Color = dark ? Windows.UI.Color.FromArgb(255, 246, 243, 245) : Windows.UI.Color.FromArgb(255, 24, 22, 25);
        // Light secondary text is darker than the design token #635D66 so small helper
        // copy keeps a comfortable contrast ratio on the near-white ground.
        Secondary.Color = dark ? Windows.UI.Color.FromArgb(255, 186, 182, 189) : Windows.UI.Color.FromArgb(255, 84, 78, 90);
        Line.Color = dark ? Windows.UI.Color.FromArgb(255, 50, 49, 54) : Windows.UI.Color.FromArgb(255, 218, 212, 218);
        Error.Color = dark ? Windows.UI.Color.FromArgb(255, 255, 163, 181) : Windows.UI.Color.FromArgb(255, 164, 0, 48);
        ControlSurface.Color = dark ? Windows.UI.Color.FromArgb(255, 53, 53, 59) : Windows.UI.Color.FromArgb(255, 234, 230, 235);
        ControlSurfaceHover.Color = dark ? Windows.UI.Color.FromArgb(255, 66, 66, 73) : Windows.UI.Color.FromArgb(255, 221, 216, 224);
        ControlSurfacePressed.Color = dark ? Windows.UI.Color.FromArgb(255, 44, 44, 49) : Windows.UI.Color.FromArgb(255, 210, 204, 213);
    }
}

sealed class ControlChannel
{
    readonly Action<ControlChannel, ControlIntent> changed;
    readonly Action editChanged;
    readonly ControlEditState edit = new();
    // Widths match the rail so every row shares one horizontal centre; differing widths made
    // the track sit a few pixels left of the buttons above and below it.
    readonly Button increase = new() { Content = new SymbolIcon(Symbol.Add), Width = 56, Height = 36 }, decrease = new() { Content = new SymbolIcon(Symbol.Remove), Width = 56, Height = 36 }, undo = new() { Content = "撤销", Width = 100, Height = 34 };
    readonly TextBlock value = MainWindow.Text("—", 96, true);
    readonly Button valueButton;
    // Preserve invalid user input until explicit validation; never turn 150 into a write of100.
    readonly NumberBox number = new() { Minimum = 0, Maximum = 100, Width = 180, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, PlaceholderText = "—", ValidationMode = NumberBoxValidationMode.Disabled };
    readonly TextBlock inputError = MainWindow.Text("", 12);
    readonly Button applyInput = new() { Content = "应用", HorizontalAlignment = HorizontalAlignment.Right };
    readonly Flyout editor = new();
    readonly Microsoft.UI.Dispatching.DispatcherQueueTimer debounce;
    bool binding = true, enabled, dragging, dirty;
    uint? previous;
    ControlIntent? inFlight;
    string confirmedFeedback = "等待读取";
    public ControlSpec Spec { get; }
    public Grid View { get; } = new() { RowSpacing = 5, MinHeight = 620 };
    public Slider Slider { get; }
    public TextBlock Feedback { get; } = MainWindow.Text("等待读取", 12);
    public uint Current => edit.Confirmed;
    public uint Desired => edit.Desired;
    public bool Pending => edit.Pending;
    public bool Available { get; private set; }
    public string DisplayValue => value.Text;
    internal TextBlock ValueLabel => value;
    public ControlChannel(ControlSpec spec, Action<ControlChannel, ControlIntent> onChanged, Action onEditChanged)
    {
        Spec = spec; changed = onChanged; editChanged = onEditChanged;
        // The feedback line needs a full text row; at 26px its ascenders and descenders were
// clipped into a single smear.
        foreach (double h in new double[] { 28, 30, 20, 110, 32, -1, 32, 46, 34 }) View.RowDefinitions.Add(new() { Height = h < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h) });
        if (spec.Code == 0x87) Put(new Image { Source = new SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Sharpness.svg"))), Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center }, 0);
        else Put(new FontIcon { Glyph = spec.Code == 0x10 ? "\uE706" : "\uE767", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 28, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center }, 0);
        var title = MainWindow.Text(spec.Name, 26, true); title.HorizontalAlignment = HorizontalAlignment.Center; Put(title, 1);
        value.FontWeight = FontWeights.Bold;
// The rail's own + / - buttons move by Step; the tray rows and global hotkeys move by
// CoarseStep. That difference is surfaced as a tooltip per button and on the channel,
// not as extra helper text: spelling both numbers inline overflows the narrowest
// channel and truncates the label.
        var helper = MainWindow.Text(spec.Code switch { 0x10 => "调节屏幕明暗", 0x62 => "不影响 Windows 系统音量", _ => "调整画面细节" }, 13);
        helper.HorizontalAlignment = HorizontalAlignment.Center; Put(helper, 2);
        string coarseHint = spec.Step == spec.CoarseStep
            ? $"每次 {spec.Step}"
            : $"这里的加号每次 {spec.Step}，托盘菜单和全局快捷键每次 {spec.CoarseStep}";
        valueButton = new() { Content = value, Padding = new Thickness(8, 0, 8, 0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        valueButton.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Colors.Transparent);
        valueButton.Resources["ButtonBorderBrushDisabled"] = new SolidColorBrush(Colors.Transparent);
        ToolTipService.SetToolTip(valueButton, "点击输入" + spec.Name); AutomationProperties.SetName(valueButton, "输入" + spec.Name);
        var input = new StackPanel { Spacing = 12 }; input.Children.Add(MainWindow.Text("输入" + spec.Name, 16, true)); number.SmallChange = spec.Step; number.LargeChange = spec.Step; AutomationProperties.SetName(number, spec.Name + "数值"); input.Children.Add(number);
        inputError.Foreground = Palette.Error; inputError.Visibility = Visibility.Collapsed; inputError.MaxWidth = 220;
        AutomationProperties.SetLiveSetting(inputError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        input.Children.Add(inputError);
        applyInput.Click += (_, _) => { if (ApplyNumber()) editor.Hide(); };
        input.Children.Add(applyInput); editor.Content = input; valueButton.Flyout = editor;
        Put(valueButton, 3); Put(increase, 4);
        // MinHeight sits below the space the star row actually receives, so the rail is never
// the element that gets squeezed when the window is short.
        // The vertical slider template puts the track slightly left of the control's centre, so
        // the rail lined up with neither the thumb nor the buttons above and below it. With
        // centre alignment the margin box is what gets centred, so a left margin of 18 shifts
        // the control right by 9 logical pixels (15.75 physical at 175%, the measured gap).
        Slider = new() { Minimum = 0, Maximum = 100, StepFrequency = spec.Step, SmallChange = spec.Step, LargeChange = Math.Max(10u, spec.Step), TickPlacement = TickPlacement.None, Orientation = Orientation.Vertical, MinHeight = 140, MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, IsEnabled = false, Width = 56, Margin = new Thickness(18, 0, 0, 0), IsThumbToolTipEnabled = false };
        // Track and thumb sizing lives in the slider's own template, so it must be set on
        // the control rather than the window resources (which apply to every slider).
        Slider.Resources["SliderTrackThemeHeight"] = 10d;
        Slider.Resources["SliderVerticalThumbWidth"] = 26d;
        Slider.Resources["SliderVerticalThumbHeight"] = 26d;
        Slider.Resources["SliderThumbCornerRadius"] = new CornerRadius(13);
        Slider.Resources["SliderTrackCornerRadius"] = new CornerRadius(5);
        editor.Opening += (_, _) =>
        {
            ClearInputError(); number.Maximum = Slider.Maximum;
            // An invalid raw string can coexist with the last numeric value.
            // Force a value transition so reopening always shows the current target.
            number.Value = double.NaN; number.Value = Desired;
        };
        editor.Closed += (_, _) => { ClearInputError(); if (enabled && Available) UpdateFeedback(); };
        AutomationProperties.SetName(Slider, spec.Name + "滑块");
        debounce = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer(); debounce.Interval = TimeSpan.FromMilliseconds(300); debounce.IsRepeating = false;
        debounce.Tick += (_, _) => DebounceTick();
        Slider.ValueChanged += (_, _) => { if (!binding) Queue(Slider.Value); };
        Slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => BeginDrag()), true);
        Slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => FinishDrag()), true);
        Slider.PointerCaptureLost += (_, _) => FinishDrag();
        Slider.PointerCanceled += (_, _) => CancelDrag();
        increase.Click += (_, _) => Adjust(spec.Step); decrease.Click += (_, _) => Adjust(-(double)spec.Step); undo.Click += (_, _) => { if (previous is { } v) Request(v); };
        AutomationProperties.SetName(increase, "提高" + spec.Name); AutomationProperties.SetName(decrease, "降低" + spec.Name); AutomationProperties.SetName(undo, "撤销上次" + spec.Name + "调节");
        ToolTipService.SetToolTip(increase, coarseHint); ToolTipService.SetToolTip(decrease, coarseHint); ToolTipService.SetToolTip(Slider, coarseHint);
        Put(Slider, 5); Put(decrease, 6); Put(undo, 7); Feedback.HorizontalAlignment = HorizontalAlignment.Center; Feedback.TextAlignment = TextAlignment.Center; AutomationProperties.SetLiveSetting(Feedback, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); Put(Feedback, 8);
        binding = false; Clear();
    }
    // Every row centres on the same axis. The rail used to keep its own alignment, which left
    // the track sitting a few pixels off the +/- buttons above and below it.
    void Put(FrameworkElement e, int row) { Grid.SetRow(e, row); e.HorizontalAlignment = HorizontalAlignment.Center; View.Children.Add(e); }
    public void Compact(bool compact)
    {
        value.FontSize = compact ? 72 : 96; View.MinHeight = compact ? 500 : 620; View.RowSpacing = compact ? 4 : 5;
        double[] h = compact ? [22, 26, 18, 82, 26, -1, 26, 36, 30] : [28, 30, 20, 110, 32, -1, 32, 46, 34];
        for (int i = 0; i < h.Length; i++) View.RowDefinitions[i].Height = h[i] < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h[i]);
        Slider.MinHeight = compact ? 90 : 140; Slider.MaxHeight = compact ? 180 : 320;
        // The left margin compensates the template's built-in track offset; it is a constant
        // shift, not a proportion, so it must not change with the control's width.
        Slider.Width = compact ? 48 : 56; Slider.Margin = new Thickness(18, 0, 0, 0);
        increase.Width = decrease.Width = compact ? 48 : 56; increase.Height = decrease.Height = compact ? 32 : 40;
        undo.Width = compact ? 100 : 116; undo.Height = compact ? 32 : 38;
    }
    internal bool Editing => dragging || editor.IsOpen;
    internal bool PrepareGlobalAdjustment(bool windowVisible)
    {
        if (dragging || windowVisible && editor.IsOpen) return false;
        // Hiding an AppWindow need not close WinUI flyouts. Discard only the hidden
        // numeric draft; do not let it permanently swallow tray/global shortcuts.
        if (!windowVisible) editor.Hide();
        return true;
    }
    internal void BeginDrag() { if (!enabled || !Available) return; dragging = true; dirty = Pending; debounce.Stop(); }
    internal void FinishDrag()
    {
        if (!dragging) return;
        dragging = false;
        if (dirty && edit.HasUnsubmitted) { debounce.Stop(); debounce.Start(); }
        dirty = false; UpdateFeedback(); UpdateUndo();
    }
    internal void CancelDrag() { if (!enabled || !Available) return; CancelPending(); editChanged(); }
    internal void DebounceTick() { if (!dragging) Submit(); }
    void Queue(double raw)
    {
        if (!enabled || !Available || binding) return;
        uint target = Normalize(raw);
        if (Slider.Value != target) { binding = true; try { Slider.Value = target; } finally { binding = false; } }
        if (!edit.Edit(target)) return;
        value.Text = Desired.ToString(); UpdateFeedback();
        undo.IsEnabled = false; debounce.Stop(); if (dragging) dirty = true; else debounce.Start(); editChanged();
    }
    void Submit() { if (enabled && Available && edit.TakeSubmission() is { } intent) changed(this, intent); }
    uint Normalize(double raw) => (uint)Math.Clamp(Math.Round(raw / Spec.Step, MidpointRounding.AwayFromZero) * Spec.Step, 0, Slider.Maximum);
    public void Request(double raw)
    {
        if (!enabled || !Available || !double.IsFinite(raw)) return;
        debounce.Stop(); edit.Edit(Normalize(raw)); ShowDesired(); undo.IsEnabled = false;
        editChanged(); Submit(); UpdateFeedback(); UpdateUndo();
    }
    internal bool ApplyNumber()
    {
        if (!enabled || !Available) return false;
        number.ApplyTemplate();
        // NumberBox.Text/Value may retain the last valid value for non-numeric text.
        // Parse its actual edit field with the same locale-aware formatter instead.
        string raw = (FindNumberInput(number)?.Text ?? number.Text ?? "").Trim();
        double? parsed = ((INumberParser)number.NumberFormatter).ParseDouble(raw);
        if (parsed is not { } target || !double.IsFinite(target) || target < 0 || target > Slider.Maximum || target % Spec.Step != 0)
        {
            inputError.Text = Feedback.Text = $"请输入 0–{Slider.Maximum}，步长 {Spec.Step}";
            inputError.Visibility = Visibility.Visible; return false;
        }
        ClearInputError(); Request(target); return true;
    }
    static TextBox? FindNumberInput(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is TextBox text) return text; if (FindNumberInput(child) is { } nested) return nested; }
        return null;
    }
    void ClearInputError() { inputError.Text = ""; inputError.Visibility = Visibility.Collapsed; }
    public void Adjust(double delta) => Request(Desired + delta);
    public void Load(MonitorValue? read)
    {
        // A new snapshot may belong to a different picture/color mode. Undo may only
        // refer to a direct adjustment accepted since this synchronization boundary.
        previous = null; inFlight = null; CancelPending(); Available = read?.Success == true && read.Maximum <= 100 && read.Maximum >= Spec.Step && read.Current <= read.Maximum && read.Current % Spec.Step == 0;
        binding = true; try { if (Available) { ShowKnown(); Slider.Maximum = number.Maximum = read!.Maximum / Spec.Step * Spec.Step; edit.Load(read.Current); Slider.Value = read.Current; value.Text = Current.ToString(); } else ShowUnknown(); } finally { binding = false; }
        confirmedFeedback = Available ? "已读取显示器实际值" : "暂不可调"; UpdateFeedback(); UpdateUndo();
    }
    // Unknown state must never leave the rail pointing at a stale value: a reader would
    // take that position as the current setting while the text says it is unknown. The
    // rail is parked at its minimum and faded, so position carries no meaning at all.
    void ShowUnknown()
    {
        binding = true;
        try { Slider.Value = Slider.Minimum; value.Text = "—"; }
        finally { binding = false; }
        Slider.Opacity = .35;
    }
    void ShowKnown() => Slider.Opacity = 1;
    void UpdateFeedback()
    {
        if (!Pending) Feedback.Text = confirmedFeedback;
        else if (dragging && edit.HasUnsubmitted) Feedback.Text = "松开后应用…";
        else Feedback.Text = inFlight != null && !edit.HasUnsubmitted ? "正在写入并回读…" : "等待应用…";
    }
    void UpdateUndo() => undo.IsEnabled = enabled && !Pending && inFlight == null && previous is { } v && v <= Slider.Maximum && v % Spec.Step == 0 && v != Current;
    public void SetEnabled(bool allow)
    {
        enabled = allow && Available;
        if (!enabled) { CancelPending(); editor.Hide(); ClearInputError(); }
        Slider.IsEnabled = increase.IsEnabled = decrease.IsEnabled = valueButton.IsEnabled = number.IsEnabled = applyInput.IsEnabled = enabled;
        value.Foreground = enabled ? Palette.Primary : Palette.Secondary; ApplyButtonTheme(); UpdateUndo();
    }
    // The undo button has no accent of its own, so it must be painted explicitly in both
    // themes: the stock WinUI look all but vanishes on the light ground. Called on every
    // theme change as well as on enable/disable.
    internal void ApplyButtonTheme()
    {
        undo.Background = Palette.ControlSurface;
        undo.Foreground = Palette.Primary;
        undo.BorderBrush = Palette.Line;
        undo.BorderThickness = new Thickness(1);
        // Hover and pressed must be pinned too, otherwise the stock WinUI brushes take over
        // on pointer-over and the button flashes a colour that belongs to neither theme.
        undo.Resources["ButtonBackground"] = Palette.ControlSurface;
        undo.Resources["ButtonBackgroundPointerOver"] = Palette.ControlSurfaceHover;
        undo.Resources["ButtonBackgroundPressed"] = Palette.ControlSurfacePressed;
        undo.Resources["ButtonBackgroundDisabled"] = Palette.ControlSurface;
        undo.Resources["ButtonForeground"] = Palette.Primary;
        undo.Resources["ButtonForegroundPointerOver"] = Palette.Primary;
        undo.Resources["ButtonForegroundPressed"] = Palette.Primary;
        undo.Resources["ButtonForegroundDisabled"] = Palette.Secondary;
        undo.Resources["ButtonBorderBrush"] = Palette.Line;
        undo.Resources["ButtonBorderBrushPointerOver"] = Palette.Line;
        undo.Resources["ButtonBorderBrushPressed"] = Palette.Line;
        undo.Resources["ButtonBorderBrushDisabled"] = Palette.Line;
    }
    public bool IsCurrent(ControlIntent intent) => edit.IsCurrent(intent);
    public void BeginCommit(ControlIntent intent) { inFlight = intent; UpdateFeedback(); }
    public void Accept(ControlIntent intent, WriteResult result)
    {
        previous = result.Previous; edit.Confirm(intent, result.Actual);
        inFlight = null; confirmedFeedback = "参数回读相符 · " + result.At.ToString("HH:mm:ss");
        if (!Pending) ShowDesired();
        UpdateFeedback(); UpdateUndo();
    }
    void ShowDesired()
    {
        if (!Available) { ShowUnknown(); return; }
        ShowKnown();
        binding = true; try { if (Slider.Value != Desired) Slider.Value = Desired; value.Text = Desired.ToString(); } finally { binding = false; }
    }
    public void CancelPending() { debounce.Stop(); dragging = dirty = false; edit.Cancel(inFlight); ShowDesired(); UpdateFeedback(); UpdateUndo(); }
    public void Clear() { inFlight = null; CancelPending(); previous = null; Available = false; SetEnabled(false); ShowUnknown(); Feedback.Text = confirmedFeedback = "等待读取"; }
}
