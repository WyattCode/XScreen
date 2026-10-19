using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace XScreen.App;

sealed class HotkeySettingsView
{
    readonly ContentDialog dialog;
    readonly ToggleSwitch enabled = new() { Header = "启用全局快捷键", OnContent = "已启用", OffContent = "已关闭" };
    readonly Dictionary<HotkeyAction, TextBox> fields = [];
    readonly Dictionary<HotkeyAction, HotkeyChord?> chords = [];
    readonly InfoBar feedback = new() { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Error };
    public HotkeySettingsView(XamlRoot root, HotkeySettings settings, Func<HotkeySettings, string?> save)
    {
        var body = new StackPanel { Spacing = 16, MinWidth = 320 };
        body.Children.Add(MainWindow.Text("收起到托盘后仍可使用。亮度、显示器音量、锐度每次加减 10。", 14));
        enabled.IsOn = settings.Enabled;
        var options = new Grid { ColumnSpacing = 16 };
        options.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        options.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        options.Children.Add(enabled);
        var defaults = new Button { Content = "恢复默认快捷键", VerticalAlignment = VerticalAlignment.Bottom, MinHeight = 36 };
        defaults.Click += (_, _) => LoadDefaults(); Grid.SetColumn(defaults, 1); options.Children.Add(defaults);
        body.Children.Add(options);
        var table = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) table.ColumnDefinitions.Add(new() { Width = w });
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            chords[action] = settings.Bindings.Single(b => b.Action == action).Chord;
            int row = table.RowDefinitions.Count; table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            void Put(FrameworkElement element, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); table.Children.Add(element); }
            var label = MainWindow.Text(HotkeySettings.Name(action), 14); label.VerticalAlignment = VerticalAlignment.Center; Put(label, 0);
            var field = new TextBox { IsReadOnly = true, Text = chords[action]?.ToString() ?? "", PlaceholderText = "点击后按组合键", MinHeight = 36, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
            fields.Add(action, field); AutomationProperties.SetName(field, HotkeySettings.Name(action) + "快捷键");
            field.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is VirtualKey.Tab or VirtualKey.Escape) return;
                e.Handled = true;
                uint modifiers = 0;
                bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
                if (Down(VirtualKey.Control)) modifiers |= 2;
                if (Down(VirtualKey.Menu)) modifiers |= 1;
                if (Down(VirtualKey.Shift)) modifiers |= 4;
                if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) { Error("请选择不包含 Windows 键的组合。"); return; }
                Capture(action, (uint)e.Key, modifiers);
            };
            Put(field, 1);
            var clear = new Button { Content = "清除", MinHeight = 36, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(clear, "清除" + HotkeySettings.Name(action) + "快捷键");
            clear.Click += (_, _) => { chords[action] = null; field.Text = ""; feedback.IsOpen = false; }; Put(clear, 2);
        }
        body.Children.Add(table);
        body.Children.Add(MainWindow.Text("使用 Ctrl 或 Alt 搭配字母、数字、方向键、翻页键或功能键（F12 除外），可加 Shift。按住不连发；读取、设置和异常暂停期间不调节。", 12));
        AutomationProperties.SetLiveSetting(feedback, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        // Errors remain visible above the scrolling form, including at the minimum window height.
        var content = new Grid { MaxHeight = 460, RowSpacing = 8 };
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(feedback);
        var scroll = new ScrollViewer { Content = body, Padding = new Thickness(0, 0, 12, 4), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); content.Children.Add(scroll);
        dialog = new() { XamlRoot = root, Title = "全局快捷键", PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            Content = content };
        dialog.PrimaryButtonClick += (_, e) => { if (save(Draft()) is { } error) { e.Cancel = true; Error(error); } };
    }
    internal HotkeySettings Draft() => new(enabled.IsOn, Enum.GetValues<HotkeyAction>().Select(a => new HotkeyBinding(a, chords[a])).ToArray());
    internal void Capture(HotkeyAction action, uint key, uint modifiers)
    {
        if (key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return;
        var chord = new HotkeyChord(modifiers, key);
        if (!chord.Valid) { Error("请按 Ctrl 或 Alt 加一个支持的按键；F12 和 Windows 键不可用。"); return; }
        chords[action] = chord; fields[action].Text = chord.ToString(); feedback.IsOpen = false;
    }
    internal void LoadDefaults()
    {
        var settings = HotkeySettings.Defaults(); enabled.IsOn = settings.Enabled;
        foreach (var binding in settings.Bindings) { chords[binding.Action] = binding.Chord; fields[binding.Action].Text = binding.Chord!.ToString(); }
        feedback.IsOpen = false;
    }
    void Error(string message) { feedback.Message = message; feedback.IsOpen = true; }
    public async Task ShowAsync(ElementTheme theme) { dialog.RequestedTheme = theme; await dialog.ShowAsync(); }
    public void SetTheme(ElementTheme theme) => dialog.RequestedTheme = theme;
    public void Dismiss() => dialog.Hide();
}
