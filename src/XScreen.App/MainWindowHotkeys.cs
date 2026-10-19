using Microsoft.UI.Xaml.Controls;

namespace XScreen.App;

sealed partial class MainWindow
{
    GlobalHotkeys? hotkeys;
    HotkeySettingsView? hotkeyView;
    bool hotkeyDialogOpen;
    internal string? HotkeyPath { get; set; }
    internal void InitializeHotkeys(IHotkeyRegistrar registrar)
    {
        hotkeys = new GlobalHotkeys(registrar, OnGlobalHotkey);
        if (registrar is NativeHotkeyRegistrar nativeKeys) nativeKeys.Pressed = hotkeys.Dispatch;
        var settings = HotkeyPath == null ? HotkeySettings.Defaults() : HotkeyStore.Load(HotkeyPath);
        if (hotkeys.Start(settings) is { } error) HotkeyWarning(error);
    }
    void HotkeyWarning(string error)
    {
        AppLog.Write("hotkeys-unavailable", error);
        if (!exiting) ShowNotice("全局快捷键暂不可用", error + " 可在“更多操作 → 全局快捷键”中修改。", InfoBarSeverity.Warning);
    }
    internal void OnGlobalHotkey(HotkeyAction action)
    {
        if (!Enum.IsDefined(action) || hotkeyDialogOpen) return;
        var (code, delta) = HotkeySettings.Target(action);
        if (!CanAdjust(code) || !rows[code].PrepareGlobalAdjustment(AppWindow.IsVisible)) return;
        AppLog.Write("global-hotkey", $"action={action};code=0x{code:X2};delta={delta}");
        rows[code].Adjust(delta);
    }
    async Task ShowHotkeySettingsAsync()
    {
        if (exiting || hotkeyDialogOpen || resetDialogOpen || busy || refreshing || advanced?.Busy == true || rows.Values.Any(r => r.Pending)) return;
        if (hotkeys == null) { HotkeyWarning("快捷键服务未能初始化，请重新启动程序。"); return; }
        hotkeyDialogOpen = true; hotkeys.Pause(); trayMenu?.Dismiss(); EnableControls(false); UpdateRefresh();
        try
        {
            hotkeyView = new(root.XamlRoot, hotkeys.Settings, settings => hotkeys.TryApply(settings,
                value => HotkeyPath != null && HotkeyStore.Save(HotkeyPath, value)));
            await hotkeyView.ShowAsync(root.ActualTheme);
        }
        catch (Exception ex) { AppLog.Write("hotkeys-dialog", ex.ToString()); if (!exiting) HotkeyWarning("快捷键设置窗口未能打开。"); }
        finally
        {
            hotkeyView = null;
            // Invalidate any queued events captured while the dialog was closing.
            hotkeys.Pause();
            if (!exiting)
            {
                if (hotkeys.TryApply(hotkeys.Settings) is { } error) HotkeyWarning(error);
                else if (notice.Title == "全局快捷键暂不可用") notice.IsOpen = false;
            }
            hotkeyDialogOpen = false; UpdateRefresh(); EnableControls(!halted && !exiting);
        }
    }
}
