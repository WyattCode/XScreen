using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XScreen.Core;

namespace XScreen.App;

// The supplied callback exposes a read result only. No target editor, Write, restore or timer exists here.
sealed class OsdComparisonView
{
    readonly ContentDialog dialog;
    readonly Func<Task<MonitorSnapshot>> read;
    readonly OsdComparison state = new();
    readonly Button capture = new() { Content = "读取当前状态", MinHeight = 36, Background = Palette.Accent,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
    readonly Button baseline = new() { Content = "将当前设为基线", IsEnabled = false, MinHeight = 36,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = Palette.Primary, BorderBrush = Palette.Line };
    readonly TextBlock message = MainWindow.Text("尚未读取。先采集原始状态并设为基线，再到 LG Switch／OSD 手动调节。", 13);
    readonly TextBlock times = MainWindow.Text("", 12);
    readonly TextBox note = new() { Header = "本次 OSD 操作（可选）", PlaceholderText = "例如：色温 暖色→中等；或对比度控件为灰色", MaxLength = 160 };
    readonly Dictionary<OsdControl, (TextBlock Before, TextBlock After, TextBlock Change)> fields = [];
    bool reading, open;

    public OsdComparisonView(XamlRoot xamlRoot, Func<Task<MonitorSnapshot>> captureState, Action<Task> trackOperation)
    {
        read = captureState;
        var content = new StackPanel { Spacing = 16, MaxWidth = 620 };
        content.Children.Add(MainWindow.Text("在原版 LG Switch／OSD 调整完成后，点击读取进行对照。此窗口不会写入或恢复设置。", 14));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(capture); buttons.Children.Add(baseline); content.Children.Add(buttons);
        AutomationProperties.SetName(capture, "读取四项 OSD 当前状态");
        AutomationProperties.SetName(baseline, "将当前读取保存为对照基线，不修改显示器");
        var table = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        foreach (double width in new[] { 1d, 1d, 1d, .7 }) table.ColumnDefinitions.Add(new() { Width = new GridLength(width, GridUnitType.Star) });
        void Put(TextBlock text, int row, int col) { Grid.SetRow(text, row); Grid.SetColumn(text, col); table.Children.Add(text); }
        table.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string[] headings = ["项目", "基线", "当前", "变化"];
        for (int c = 0; c < 4; c++) Put(MainWindow.Text(headings[c], 12), 0, c);
        foreach (var control in OsdProtocol.Controls)
        {
            int row = table.RowDefinitions.Count; table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var a = MainWindow.Text("—", 14); var b = MainWindow.Text("未读取", 14); var change = MainWindow.Text("—", 12);
            Put(MainWindow.Text(OsdProtocol.Name(control), 14, true), row, 0); Put(a, row, 1); Put(b, row, 2); Put(change, row, 3);
            fields.Add(control, (a, b, change));
        }
        content.Children.Add(table); content.Children.Add(times); content.Children.Add(note); content.Children.Add(message);
        content.Children.Add(MainWindow.Text("名称按已恢复的映射显示，仍需本机核对。采集为顺序读取，请完成调节后再点击；数值一致不代表画面、链路或完整恢复已验证。", 12));
        dialog = new ContentDialog { Title = "OSD 手动对照 · 只读", Content = new ScrollViewer { Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "关闭", XamlRoot = xamlRoot, DefaultButton = ContentDialogButton.None };
        dialog.Closing += (_, e) => { if (reading) e.Cancel = true; };
        // Track the entire UI operation, including failure handling, so application exit can drain it safely.
        capture.Click += (_, _) => trackOperation(ReadNowAsync());
        baseline.Click += (_, _) => SaveBaseline();
    }
    public async Task ShowAsync(ElementTheme theme)
    {
        if (open) return;
        open = true; SetTheme(theme);
        try { await dialog.ShowAsync(); }
        finally { open = false; }
    }
    public void SetTheme(ElementTheme theme) => dialog.RequestedTheme = theme;
    async Task ReadNowAsync()
    {
        if (!open || reading) return;
        reading = true; capture.IsEnabled = baseline.IsEnabled = note.IsEnabled = false;
        string operatorNote = note.Text.Trim();
        message.Text = "正在读取四项状态；不发送调节。";
        try
        {
            var snapshot = await read(); state.Accept(snapshot); Render();
            message.Text = state.Baseline == null ? "读取完成。可将此状态设为基线；然后在原版／OSD 手动调整一项再回来读取。"
                : "对照完成，仅记录参数变化。若需要还原，请你在原版／OSD 手动操作；本工具不会自动恢复。";
            AppLog.Write("osd-comparison-read", JsonSerializer.Serialize(new { snapshot.At, source = AppLog.Mode,
                values = snapshot.Values, decoded = OsdProtocol.Decode(snapshot), baselineAt = state.Baseline?.At,
                differences = state.Differences, manualNote = operatorNote, hardwareWriteVerified = false, fullDisplayRecovery = "unconfirmed" }));
        }
        catch (Exception ex)
        {
            state.InvalidateCurrent(); Render(); message.Text = "本次对照无效：" + ex.Message + " 没有自动重试或恢复设置。";
            AppLog.Write("osd-comparison-error", ex.ToString());
        }
        finally { reading = false; capture.IsEnabled = note.IsEnabled = true; baseline.IsEnabled = state.Current != null; }
    }
    void SaveBaseline()
    {
        if (!open || reading || state.Current == null) return;
        state.SaveBaseline(); Render(); message.Text = "已保存读取结果作为基线；没有访问或修改显示器。";
        AppLog.Write("osd-comparison-baseline", JsonSerializer.Serialize(new { state.Baseline!.At, values = state.Baseline.Values, hardwareWriteVerified = false }));
    }
    void Render()
    {
        string Label(OsdReading value) => value.Display + $"\n原始值 {value.Raw.Current}（0x{value.Raw.Current:X}）";
        var before = state.Baseline == null ? [] : OsdProtocol.Decode(state.Baseline);
        var after = state.Current == null ? [] : OsdProtocol.Decode(state.Current);
        foreach (var control in OsdProtocol.Controls)
        {
            var f = fields[control]; var a = before.SingleOrDefault(v => v.Control == control); var b = after.SingleOrDefault(v => v.Control == control);
            f.Before.Text = a == null ? "—" : Label(a); f.After.Text = b == null ? "未确认" : Label(b);
            f.Change.Text = a == null || b == null ? "—" : a.Raw == b.Raw ? "未变化" : "已变化";
        }
        times.Text = $"基线：{state.Baseline?.At.ToLocalTime().ToString("HH:mm:ss") ?? "未保存"}    当前：{state.Current?.At.ToLocalTime().ToString("HH:mm:ss") ?? "未确认"}";
    }
}
