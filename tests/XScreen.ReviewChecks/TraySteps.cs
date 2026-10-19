using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunTraySteps()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            var rows = (IDictionary)Get(window!, "rows");
            var point = new PointInt32(850, 650);
            Call(window!, "ShowTrayMenu", point);
            var menu = (Window)Get(window!, "trayMenu");
            var actions = ((IEnumerable)Get(menu, "actions")).Cast<Button>().ToArray();
            await Until(() => ((Grid)Get(menu, "root")).IsLoaded);
            foreach (var index in new[] { 1, 2, 3, 4 })
            {
                string hint = index is 1 or 3 ? "+10" : "−10";
                Check(((Grid)actions[index].Content).Children.OfType<TextBlock>().Last().Text == hint
                    && AutomationProperties.GetName(actions[index]).EndsWith(hint),
                    "Tray step is visible and accessible for action " + index);
            }
            Call(menu, "Dismiss");
            foreach (byte code in new byte[] { 0x10, 0x62 })
            {
                int increase = code == 0x10 ? 1 : 3, decrease = increase + 1;
                async Task Seed(uint value)
                {
                    Call(rows[code]!, "Request", (double)value);
                    await Task.Delay(400); await Idle();
                    Check((uint)Get(rows[code]!, "Current") == value, $"Fake {code:X2} starts at {value}");
                }
                async Task Step(int action, uint expected, bool writes)
                {
                    int before = fake.Writes.Count;
                    Call(window!, "ShowTrayMenu", point); Call(menu, "InvokeAction", action);
                    // A duplicate callback from the dismissed popup must remain inert.
                    Call(menu, "InvokeAction", action);
                    await Task.Delay(400); await Idle();
                    Check((uint)Get(rows[code]!, "Current") == expected
                        && fake.Writes.Count == before + (writes ? 1 : 0)
                        && (!writes || fake.Writes.Last() == (code, expected))
                        && !(bool)Get(menu, "IsOpen"),
                        $"Tray {code:X2} action {action} reaches {expected} once, boundary no-op adds no writes");
                }
                await Seed(50);
                await Step(increase, 60, true);
                await Step(decrease, 50, true);
                await Seed(95);
                await Step(increase, 100, true);
                await Step(increase, 100, false);
                await Seed(5);
                await Step(decrease, 0, true);
                await Step(decrease, 0, false);
            }
            Check(WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "Tray step checks perform zero real monitor reads and writes");
        }
        catch (Exception ex) { failures++; Environment.ExitCode = 1; results.Add(new { harnessError = ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!, "ExitAsync")!; }
    }
}
