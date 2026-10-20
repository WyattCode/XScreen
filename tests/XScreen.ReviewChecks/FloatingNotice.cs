using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunFloatingNotice()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            int reads = fake.Reads;
            var root = (Grid)Get(window!, "root"); var notice = (InfoBar)Get(window!, "notice");
            var pages = (SelectorBar)Get(window!, "pages"); var appearance = (ComboBox)Get(window!, "appearance");
            var refresh = (Button)Get(window!, "refresh");
            Rect Bounds(FrameworkElement e) => new(e.TransformToVisual(root).TransformPoint(new(0, 0)), new Size(e.ActualWidth, e.ActualHeight));
            bool Same(Rect a, Rect b) => Math.Abs(a.X - b.X) < .25 && Math.Abs(a.Y - b.Y) < .25 && Math.Abs(a.Width - b.Width) < .25 && Math.Abs(a.Height - b.Height) < .25;
            string message = "读取校验未通过，本次调节没有发送。请点击重新读取确认状态；不会重发调节。";
            foreach (int theme in new[] { 0, 1 })
            foreach (var (width, height) in new[] { (1220, 900), (840, 680) })
            foreach (int page in new[] { 0, 1 })
            {
                notice.IsOpen = false; appearance.SelectedIndex = theme;
                Call(window!, "ResizeLogical", width, height); pages.SelectedItem = pages.Items[page];
                await Task.Delay(100); root.UpdateLayout();
                var viewport = (ScrollViewer)Get(window!, page == 0 ? "scroll" : "advancedScroll");
                Rect before = Bounds(viewport), tabBefore = Bounds(pages), refreshBefore = Bounds(refresh);
                foreach (var severity in new[] { InfoBarSeverity.Informational, InfoBarSeverity.Success, InfoBarSeverity.Error })
                {
                    refresh.Focus(FocusState.Programmatic);
                    Call(window!, "ShowNotice", "锐度状态未能确认", string.Concat(Enumerable.Repeat(message, severity == InfoBarSeverity.Error ? 40 : 1)), severity);
                    await Task.Delay(50); root.UpdateLayout();
                    string label = $"{theme}/{width}/page{page}/{severity}";
                    Check(notice.IsOpen && Same(before, Bounds(viewport)), "Showing a notice never moves or shrinks the control viewport: " + label,
                        new { before, after = Bounds(viewport) });
                    Check(Same(tabBefore, Bounds(pages)) && Same(refreshBefore, Bounds(refresh)), "Tabs and Refresh stay fixed while notice text wraps: " + label);
                    Check(refresh.FocusState != FocusState.Unfocused, "Floating notice does not steal keyboard focus: " + label);
                    Check(notice.IsClosable, "Floating notice offers a close action: " + label);
                    var host = (Border)Get(window!, "noticeHost");
                    {
                        Rect card = Bounds(host), footer = Bounds((FrameworkElement)Get(window!, "noticeFooter"));
                        Check(card.Left >= 0 && card.Right <= root.ActualWidth + .5 && card.Top >= 0 && card.Bottom <= footer.Top - 7,
                            "Card stays inside the window and above the bottom toolbar: " + label, new { card, footer });
                        var scroller = (ScrollViewer)Get(window!, "noticeScroll");
                        Check(host.Background is SolidColorBrush brush && brush.Color.A == 255 && host.Opacity == 1,
                            "Card has an opaque surface so underlying control text cannot bleed through: " + label);
                        Check(card.Height <= 240.5 && (severity != InfoBarSeverity.Error || scroller.ScrollableHeight > 0),
                            "Very long text scrolls inside the bounded card: " + label, new { card.Height, scroller.ScrollableHeight });
                    }
                    if (severity == InfoBarSeverity.Error)
                    {
                        await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + $".{theme}-{width}-page{page}.png")!;
                        var scroller = (ScrollViewer)Get(window!, "noticeScroll");
                        scroller.ChangeView(null, double.MaxValue, null, true); await Task.Delay(40);
                        Check(scroller.VerticalOffset > 0, "Long message can be scrolled: " + label);
                        Call(window!, "ShowNotice", "新提示", message, InfoBarSeverity.Informational);
                        await Task.Delay(40); root.UpdateLayout();
                        Check(notice.Title == "新提示" && scroller.VerticalOffset < .5 && Same(before, Bounds(viewport)),
                            "Replacing a scrolled notification returns to the beginning without shifting controls: " + label);
                        await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + $".compact-{theme}-{width}-page{page}.png")!;
                    }
                    var close = NoticeClose(notice);
                    Check(close is { Visibility: Visibility.Visible } && notice.IsClosable, "Close button is available outside scrolling content: " + label);
                    var messageText = Descendants(notice).OfType<TextBlock>().Single(t => t.Name == "Message");
                    Check(Bounds(messageText).Right <= Bounds(close).Left - 1,
                        "Message layout reserves space for the fixed close action: " + label);
                    if (close is { Visibility: Visibility.Visible } && notice.IsClosable)
                    {
                        ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
                        await Task.Delay(40);
                    }
                    else notice.IsOpen = false;
                    root.UpdateLayout();
                    Check(!notice.IsOpen && Same(before, Bounds(viewport)), "Closing the notice keeps control geometry unchanged: " + label);
                    Check(host.Visibility == Visibility.Collapsed, "Dismissed card leaves no hit-test surface: " + label);
                }
            }
            await CheckNoticeDismissalAudit();
            Check(fake.Reads == reads && fake.Writes.Count == 0, "Notice display, resizing, themes, tab switching and dismissal perform no device I/O");
            Call(window!, "Halt", "离线故障", new InvalidOperationException("Offline layout test"));
            await Task.Delay(60);
            var errorClose = NoticeClose(notice);
            if (errorClose is { Visibility: Visibility.Visible } && notice.IsClosable)
                ((IInvokeProvider)new ButtonAutomationPeer(errorClose).GetPattern(PatternInterface.Invoke)).Invoke();
            else notice.IsOpen = false;
            await Task.Delay(60);
            var rows = ((IDictionary)Get(window!, "rows")).Values.Cast<object>().ToArray();
            Check(!notice.IsOpen && (bool)Get(window!, "halted") && rows.All(r => !((Slider)Get(r, "Slider")).IsEnabled),
                "Dismissing an error does not clear the fault or re-enable adjustment");
            Check(fake.Reads == reads && fake.Writes.Count == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "All floating-notice checks remain fake-only, with zero real monitor I/O");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }

    Button NoticeClose(InfoBar notice) => window!.GetType().GetField("noticeClose", Members)?.GetValue(window) as Button
        ?? Descendants(notice).OfType<Button>().Single(b => b.Name == "CloseButton");

    async Task CheckNoticeDismissalAudit()
    {
        var root = (Grid)Get(window!, "root"); var notice = (InfoBar)Get(window!, "notice");
        var host = (Border)Get(window!, "noticeHost"); var scroller = (ScrollViewer)Get(window!, "noticeScroll");
        Call(window!, "ShowNotice", "长提示", string.Concat(Enumerable.Repeat("离线长消息，请读取后确认状态。", 150)), InfoBarSeverity.Warning);
        await Task.Delay(80); root.UpdateLayout();
        scroller.ChangeView(null, double.MaxValue, null, true); await Task.Delay(80); root.UpdateLayout();
        var close = NoticeClose(notice);
        double top = close.TransformToVisual(host).TransformPoint(new(0, 0)).Y;
        Check(top >= 0 && top + close.ActualHeight <= host.ActualHeight + .5,
            "Close remains visible and clickable after scrolling a long message to its end", new { top, close.ActualHeight, hostHeight = host.ActualHeight });
        var closePoint = close.TransformToVisual((UIElement)window!.Content).TransformPoint(new(close.ActualWidth / 2, close.ActualHeight / 2));
        Check(VisualTreeHelper.FindElementsInHostCoordinates(closePoint, root).Any(e => ReferenceEquals(e, close)),
            "Close button is actually hit-testable at the end of the long message");
        await (Task)Call(window!, "Capture", ReviewProgram.ReportPath + ".scrolled.png")!;
        close.Focus(FocusState.Keyboard); await Task.Delay(30);
        ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
        await Task.Delay(50);
        var focused = FocusManager.GetFocusedElement(root.XamlRoot);
        Check(ReferenceEquals(focused, Get(window!, "refresh")) || ReferenceEquals(focused, Get(window!, "appearance")),
            "Keyboard dismissal returns focus to a safe visible command, never an adjustment", new { focusedType = focused?.GetType().Name });

        Call(window!, "ShowNotice", "键盘关闭", "Offline Escape test", InfoBarSeverity.Informational);
        await Task.Delay(40); NoticeClose(notice).Focus(FocusState.Keyboard); await Task.Delay(30);
        PostKey(window!, Windows.System.VirtualKey.Escape); await Task.Delay(60);
        Check(!notice.IsOpen && host.Visibility == Visibility.Collapsed
            && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Get(window!, "refresh")),
            "Routed Escape from the card closes it and restores safe focus");

        Call(window!, "ShowNotice", "保持显示", "Offline resize and theme test", InfoBarSeverity.Warning);
        foreach (var width in new[] { 1220, 840 })
        {
            Call(window!, "ResizeLogical", width, 680);
            ((ComboBox)Get(window!, "appearance")).SelectedIndex = width == 1220 ? 0 : 1;
            await Task.Delay(80); root.UpdateLayout();
            var footer = (FrameworkElement)Get(window!, "noticeFooter");
            var card = new Rect(host.TransformToVisual(root).TransformPoint(new(0, 0)), new Size(host.ActualWidth, host.ActualHeight));
            Check(notice.IsOpen && card.Left >= 0 && card.Right <= root.ActualWidth + .5
                && card.Bottom <= footer.TransformToVisual(root).TransformPoint(new(0, 0)).Y - 7,
                "An open card follows resize and theme changes without overlapping bottom commands: " + width);
        }
        var focusOutside = (ComboBox)Get(window!, "appearance"); focusOutside.Focus(FocusState.Keyboard);
        notice.IsOpen = false; await Task.Delay(40);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), focusOutside),
            "Programmatic dismissal never steals focus from a control outside the card");

        Call(window!, "Halt", "硬件故障", new InvalidOperationException("Offline error priority test"));
        string faultTitle = notice.Title, faultMessage = notice.Message;
        Call(window!, "HotkeyWarning", "Offline conflict");
        Check(notice.IsOpen && notice.Severity == InfoBarSeverity.Error && notice.Title == faultTitle && notice.Message == faultMessage,
            "A shortcut warning cannot replace an open hardware fault or its recovery guidance");
        close = NoticeClose(notice); close.Focus(FocusState.Keyboard); await Task.Delay(20);
        ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(50);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Get(window!, "appearance")),
            "Fault dismissal skips disabled Refresh and keeps keyboard focus out of adjustment controls");
    }
}
