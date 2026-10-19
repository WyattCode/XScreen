using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using XScreen.Core;

namespace XScreen.App;

// Startup supplies the full snapshot. Draft editing never touches the transport.
sealed class OsdControlsView
{
    public StackPanel View { get; } = new() { Spacing = 18, Padding = new Thickness(12, 8, 12, 12) };
    readonly Dictionary<OsdControl, Editor> editors = [];
    readonly Button read = new() { Content = "刷新画面状态", MinHeight = 36 };
    readonly Button cancel = new() { Content = "取消草稿", MinHeight = 36 };
    readonly TextBlock feedback = MainWindow.Text("启动时自动读取当前状态。选择或编辑后点击对应的应用；不会自动调节。", 13);
    readonly Func<Task<MonitorSnapshot>> capture;
    readonly Func<OsdControl, uint, Task<OsdWriteResult>> apply;
    readonly Action<Task> track;
    readonly Action stateChanged;
    bool busy, available;
    public bool Busy => busy;

    public OsdControlsView(Func<Task<MonitorSnapshot>> readState, Func<OsdControl, uint, Task<OsdWriteResult>> write, Action<Task> trackOperation, Action onStateChanged)
    {
        capture = readState; apply = write; track = trackOperation; stateChanged = onStateChanged;
        AutomationProperties.SetLiveSetting(feedback, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        View.Children.Add(MainWindow.Text("画面与色彩", 24, true));
        View.Children.Add(MainWindow.Text("模式、色温与黑色稳定器每次仅应用一项；对比度当前受限，不提供调节。标注“仅 SDR”的模式须先关闭 HDR。", 13));
        View.Children.Add(feedback);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        commands.Children.Add(read); commands.Children.Add(cancel); View.Children.Add(commands);
        AutomationProperties.SetName(read, "刷新画面与色彩状态"); AutomationProperties.SetName(cancel, "取消画面与色彩草稿，不恢复显示器设置");
        foreach (var control in OsdProtocol.Controls)
        {
            var editor = new Editor(control, () => track(ApplyAsync(control)), () => CancelDraft(control)); editors.Add(control, editor); View.Children.Add(editor.View);
        }
        read.Click += (_, _) => track(ReadAsync());
        cancel.Click += (_, _) => CancelDrafts();
        SetAvailable(false);
    }
    public void SetAvailable(bool value) { available = value; UpdateEnabled(); }
    void UpdateEnabled()
    {
        read.IsEnabled = cancel.IsEnabled = available && !busy;
        foreach (var editor in editors.Values) editor.SetEnabled(available && !busy);
    }
    public void Load(MonitorSnapshot snapshot)
    {
        foreach (var editor in editors.Values) editor.Load(snapshot.Get(OsdProtocol.Code(editor.Control)));
        feedback.Text = "当前状态已更新。选择或编辑后点击对应的应用；不会自动调节。";
    }
    public void Clear(bool unconfirmed = false)
    {
        foreach (var editor in editors.Values) editor.Clear(unconfirmed);
        feedback.Text = unconfirmed ? "本次状态未确认，调节已暂停。" : "当前状态不可用。";
        SetAvailable(false);
    }
    void CancelDrafts()
    {
        if (busy || !available) return;
        foreach (var editor in editors.Values) editor.Cancel();
        feedback.Text = "未提交的草稿已取消，没有发送设置或恢复命令。";
    }
    void CancelDraft(OsdControl control)
    {
        if (busy || !available) return;
        editors[control].Cancel();
        feedback.Text = OsdProtocol.Name(control) + "草稿已取消，没有发送设置或恢复命令。";
    }
    async Task ReadAsync()
    {
        if (busy || !available) return;
        busy = true; UpdateEnabled(); stateChanged(); feedback.Text = "正在读取画面状态…";
        try { Load(await capture()); feedback.Text = "状态已读取。未知值保持不可应用；草稿已按本次读取更新。"; }
        catch (Exception ex) { Clear(unconfirmed: true); feedback.Text = ex.Message; }
        finally { busy = false; UpdateEnabled(); stateChanged(); }
    }
    async Task ApplyAsync(OsdControl control)
    {
        if (busy || !available || !editors[control].Recognized || MonitorSafetyPolicy.IsReadOnly(OsdProtocol.Code(control))) return;
        uint target;
        try { target = editors[control].Target(); _ = OsdProtocol.DescribeCandidate(control, target); }
        catch (Exception ex) { feedback.Text = ex.Message; return; }
        busy = true; UpdateEnabled(); stateChanged(); feedback.Text = $"正在应用{OsdProtocol.Name(control)}…";
        try
        {
            var result = await apply(control, target); Load(result.Snapshot);
            feedback.Text = result.WasWritten ? $"{OsdProtocol.Name(control)}回读与目标一致。其他草稿已取消，请按新状态选择；完整显示恢复仍未确认。"
                : "显示器已是该值，没有重复写入。其他草稿已按读取结果更新。";
        }
        catch (Exception ex) { Clear(unconfirmed: true); feedback.Text = ex.Message; }
        finally { busy = false; UpdateEnabled(); stateChanged(); }
    }

    sealed class Editor
    {
        public OsdControl Control { get; }
        public Grid View { get; } = new() { RowSpacing = 8, ColumnSpacing = 16,
            Padding = new Thickness(0, 0, 0, 18), BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        readonly TextBlock current = MainWindow.Text("当前：未读取", 13);
        readonly ComboBox? choice;
        readonly TextBox? number;
        readonly Slider? slider;
        readonly Button apply = new() { Content = "应用", MinWidth = 76, MinHeight = 36 };
        MonitorValue? confirmed;
        bool setting;
        readonly Action submit, cancel;
        public bool Recognized { get; private set; }
        public Editor(OsdControl control, Action onSubmit, Action onCancel)
        {
            Control = control; submit = onSubmit; cancel = onCancel;
            View.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            View.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            View.RowDefinitions.Add(new() { Height = GridLength.Auto }); View.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var heading = new StackPanel { Spacing = 4 }; heading.Children.Add(MainWindow.Text(OsdProtocol.Name(control), 18, true)); heading.Children.Add(current); View.Children.Add(heading);
            // Apply belongs to the input row, not the combined heading/input height.
            Grid.SetColumn(apply, 1); Grid.SetRow(apply, 1); apply.VerticalAlignment = VerticalAlignment.Center; View.Children.Add(apply);
            AutomationProperties.SetName(apply, "应用" + OsdProtocol.Name(control)); apply.Click += (_, _) => submit();
            if (MonitorSafetyPolicy.IsReadOnly(OsdProtocol.Code(control)))
            {
                apply.Visibility = Visibility.Collapsed;
                heading.Children.Add(MainWindow.Text(MonitorSafetyPolicy.ContrastRestriction, 13));
            }
            IReadOnlyList<OsdOption>? options = control switch { OsdControl.PictureMode => OsdProtocol.PictureModes, OsdControl.ColorTemperature => OsdProtocol.ColorTemperatures, _ => null };
            if (options != null)
            {
                choice = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, MinHeight = 36, PlaceholderText = "请先读取当前状态" };
                foreach (var o in options) choice.Items.Add(new ComboBoxItem { Content = o.Label + (o.SdrOnly ? "（仅 SDR）" : ""), Tag = o.Raw });
                AutomationProperties.SetName(choice, "选择" + OsdProtocol.Name(control)); Grid.SetRow(choice, 1); View.Children.Add(choice);
            }
            else
            {
                var input = new Grid { ColumnSpacing = 12 }; input.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); input.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                double step = control == OsdControl.BlackStabilizer ? 5 : 1;
                slider = new() { Minimum = 0, Maximum = 100, StepFrequency = step, SmallChange = step, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
                number = new() { Width = 76, MinHeight = 36, VerticalAlignment = VerticalAlignment.Center, PlaceholderText = "0–100", MaxLength = 10 };
                slider.ValueChanged += (_, _) => { if (!setting) number.Text = ((uint)slider.Value).ToString(CultureInfo.InvariantCulture); };
                number.TextChanged += (_, _) =>
                {
                    if (setting || !uint.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value > slider.Maximum || value % step != 0) return;
                    setting = true; slider.Value = value; setting = false;
                };
                number.KeyDown += (_, e) => { if (HandleNumberKey(e.Key)) e.Handled = true; };
                AutomationProperties.SetName(slider, "编辑" + OsdProtocol.Name(control) + "草稿"); AutomationProperties.SetName(number, "输入" + OsdProtocol.Name(control) + "草稿");
                input.Children.Add(slider); Grid.SetColumn(number, 1); input.Children.Add(number); Grid.SetRow(input, 1); View.Children.Add(input);
            }
            Load(null);
        }
        public uint Target()
        {
            if (choice?.SelectedItem is ComboBoxItem item) return (uint)item.Tag;
            if (number != null && uint.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint target))
            {
                if (confirmed == null || target > Math.Min(100u, confirmed.Maximum))
                    throw new ArgumentException($"请输入0–{Math.Min(100u, confirmed?.Maximum ?? 0)}整数；黑色稳定器须为5的倍数。");
                return target;
            }
            throw new ArgumentException("请输入0–100整数；黑色稳定器须为5的倍数。");
        }
        public void Load(MonitorValue? value)
        {
            confirmed = value; Recognized = value != null && OsdProtocol.Decode(Control, value).Recognized;
            current.Text = "当前：" + (value == null ? "未读取" : OsdProtocol.Decode(Control, value).Display);
            setting = true;
            if (choice != null)
            {
                choice.SelectedItem = Recognized ? choice.Items.Cast<ComboBoxItem>().Single(i => (uint)i.Tag == value!.Current) : null;
                choice.PlaceholderText = value == null ? "等待自动读取" : "状态未确认";
            }
            if (number != null) number.Text = Recognized ? value!.Current.ToString(CultureInfo.InvariantCulture) : "";
            if (slider != null)
            {
                slider.Visibility = number!.Visibility = Recognized && !MonitorSafetyPolicy.IsReadOnly(OsdProtocol.Code(Control)) ? Visibility.Visible : Visibility.Collapsed;
                if (!Recognized) { slider.Value = slider.Minimum; number.PlaceholderText = "—"; }
            }
            if (slider != null && Recognized)
            {
                uint step = Control == OsdControl.BlackStabilizer ? 5u : 1u;
                slider.Maximum = Math.Min(100u, value!.Maximum) / step * step;
                slider.Value = value.Current;
                number!.PlaceholderText = $"0–{slider.Maximum}";
            }
            setting = false;
        }
        public void Clear(bool unconfirmed)
        {
            Load(null);
            if (!unconfirmed) return;
            current.Text = "当前：未确认";
            if (choice != null) choice.PlaceholderText = "状态未确认";
        }
        public void Cancel() => Load(confirmed);
        internal bool HandleNumberKey(VirtualKey key)
        {
            if (key == VirtualKey.Enter) { submit(); return true; }
            if (key == VirtualKey.Escape) { cancel(); return true; }
            return false;
        }
        public void SetEnabled(bool enabled)
        {
            enabled = enabled && !MonitorSafetyPolicy.IsReadOnly(OsdProtocol.Code(Control));
            apply.IsEnabled = enabled && Recognized;
            if (choice != null) choice.IsEnabled = enabled && Recognized;
            if (number != null) number.IsEnabled = enabled && Recognized;
            if (slider != null) slider.IsEnabled = enabled && Recognized;
        }
    }
}
