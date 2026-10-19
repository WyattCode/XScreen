using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XScreen.Core;

namespace XScreen.App;

// Confirmation is entirely UI/cached data. No transport exists in this view.
sealed class ResetSettingsView
{
    readonly ContentDialog dialog;
    public ResetSettingsView(XamlRoot root, MonitorSnapshot snapshot)
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(MainWindow.Text("将两个页面中的六项可调设置恢复为以下应用默认值。对比度受限，不纳入重置。", 14));
        var table = new Grid { RowSpacing = 10, ColumnSpacing = 16 };
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) table.ColumnDefinitions.Add(new() { Width = width });
        void Row(string label, string current, string target, bool header = false)
        {
            int index = table.RowDefinitions.Count; table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            string[] values = [label, current, target];
            for (int column = 0; column < values.Length; column++)
            {
                var text = MainWindow.Text(values[column], header ? 12 : 14, !header && column == 2);
                Grid.SetRow(text, index); Grid.SetColumn(text, column); table.Children.Add(text);
            }
        }
        Row("设置", "当前", "重置为", true);
        foreach (byte code in new byte[] { 0x10, 0x62, 0x87, 0x15, 0x14, 0x12, 0xF9 })
        {
            var setting = AppResetProfile.Settings.SingleOrDefault(s => s.Code == code); var current = snapshot.Get(code);
            string display = current?.Success == true ? current.Current.ToString() : "未确认";
            var osd = OsdProtocol.Controls.Where(c => OsdProtocol.Code(c) == code).ToArray();
            if (current != null && osd.Length == 1) display = OsdProtocol.Decode(osd[0], current).Display;
            Row(setting?.Name ?? "对比度", display, setting?.Display ?? "不重置（受限）");
        }
        body.Children.Add(table);
        body.Children.Add(MainWindow.Text("这是应用预设，不会发送显示器的恢复出厂命令。画面模式或色温切换可能连带改变画面参数。", 13));
        body.Children.Add(MainWindow.Text("每步都会回读核对；遇到异常立即停止，已完成项不会自动回滚。当前值来自最近读取，确认后会重新核对。", 13));
        dialog = new() { XamlRoot = root, Title = "重置六项设置", Content = new ScrollViewer { Content = body, MaxHeight = 430,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "确认重置", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
    }
    public async Task<bool> ShowAsync(ElementTheme theme) { dialog.RequestedTheme = theme; return await dialog.ShowAsync() == ContentDialogResult.Primary; }
    public void SetTheme(ElementTheme theme) => dialog.RequestedTheme = theme;
    public void Dismiss() => dialog.Hide();
}
