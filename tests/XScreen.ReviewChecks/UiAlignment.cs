using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XScreen.Core;

namespace XScreen.Review;

public sealed partial class ReviewApplication
{
    async Task RunUiAlignment()
    {
        try
        {
            await Until(() => fake.InitialReadEntered.IsSet); fake.InitialReadRelease.Set(); await Idle();
            int reads = fake.Reads;
            var root = (Grid)Get(window!, "root"); var pages = (SelectorBar)Get(window!, "pages");
            var appearance = (ComboBox)Get(window!, "appearance");
            var advanced = Get(window!, "advanced"); var editors = (IDictionary)Get(advanced, "editors");
            double Center(FrameworkElement e, UIElement relative) => e.TransformToVisual(relative).TransformPoint(new(0, e.ActualHeight/2)).Y;
            double Right(FrameworkElement e, UIElement relative) => e.TransformToVisual(relative).TransformPoint(new(e.ActualWidth,0)).X;
            foreach (int theme in new[] { 0, 1 })
            foreach (var (width, height) in new[] { (1220,900), (840,680) })
            {
                appearance.SelectedIndex = theme; Call(window!, "ResizeLogical", width, height); pages.SelectedItem = pages.Items[1];
                await Task.Delay(150); ((ScrollViewer)Get(window!,"advancedScroll")).ChangeView(null,0,null,true); await Task.Delay(60); root.UpdateLayout();
                string label = (theme == 0 ? "dark" : "light") + "-" + width;
                var rightEdges = new List<double>();
                foreach (var control in OsdProtocol.WritableControls)
                {
                    var editor = editors[control]!; var view = (Grid)Get(editor, "View"); var apply = (Button)Get(editor, "apply");
                    var input = (FrameworkElement)Get(editor, control == OsdControl.BlackStabilizer ? "number" : "choice");
                    double delta = Math.Abs(Center(apply,view) - Center(input,view));
                    Check(delta <= .5 && apply.ActualHeight >= 36 && Math.Abs(apply.ActualHeight-input.ActualHeight) <= .5,
                        "Apply and input share a center and height: " + control + "/" + label,
                        new { centerDelta=delta, applyHeight=apply.ActualHeight, inputHeight=input.ActualHeight });
                    rightEdges.Add(Right(apply, (UIElement)Get(advanced,"View")));
                    Check(Right(apply,view) <= view.ActualWidth + .5 && input.ActualWidth > 50,
                        "Input and Apply stay inside their row: " + control + "/" + label);
                }
                Check(rightEdges.Max()-rightEdges.Min() <= .5, "All Apply buttons share the same right edge in " + label);
                var contrast = editors[OsdControl.Contrast]!;
                Check(((Button)Get(contrast,"apply")).Visibility == Visibility.Collapsed
                    && ((Slider)Get(contrast,"slider")).Visibility == Visibility.Collapsed,
                    "Read-only contrast has no visible adjustment in " + label);
                await (Task)Call(window!,"Capture",ReviewProgram.ReportPath+"."+label+".png")!;
                var viewport = (ScrollViewer)Get(window!,"advancedScroll");
                viewport.ChangeView(null,double.MaxValue,null,true); await Task.Delay(100); root.UpdateLayout();
                var last = editors[OsdControl.BlackStabilizer]!;
                var lastApply = (Button)Get(last,"apply"); var lastNumber = (TextBox)Get(last,"number");
                double top = lastApply.TransformToVisual(viewport).TransformPoint(new(0,0)).Y;
                double bottom = lastNumber.TransformToVisual(viewport).TransformPoint(new(0,lastNumber.ActualHeight)).Y;
                Check(top >= -.5 && bottom <= viewport.ActualHeight+.5,
                    "Scrolling reaches the full last input/Apply row in " + label, new { top,bottom,viewportHeight=viewport.ActualHeight });
                await (Task)Call(window!,"Capture",ReviewProgram.ReportPath+".bottom-"+label+".png")!;
                pages.SelectedItem = pages.Items[0]; await Task.Delay(80); root.UpdateLayout();
                foreach (string name in new[] { "refresh", "reset" })
                {
                    var button = (Button)Get(window!,name); var content = (StackPanel)button.Content;
                    var icon = content.Children.OfType<SymbolIcon>().Single(); var text = content.Children.OfType<TextBlock>().Single();
                    Check(Math.Abs(Center(icon,content)-Center(text,content)) <= .5,
                        "Header command icon and text align: "+name+"/"+label);
                }
                await (Task)Call(window!,"CaptureTitleBar",ReviewProgram.ReportPath+".title-"+label+".png")!;
            }
            foreach (int theme in new[] { 0,1 })
            {
                appearance.SelectedIndex=theme; Call(window!,"ShowTrayMenu",new Windows.Graphics.PointInt32(850,650)); await Task.Delay(100);
                var menu=(Window)Get(window!,"trayMenu");
                bool aligned=true;
                foreach (var button in ((IEnumerable)Get(menu,"actions")).Cast<Button>())
                {
                    var content=(Grid)button.Content; var icon=content.Children.OfType<FontIcon>().Single();
                    foreach (var text in content.Children.OfType<TextBlock>().Where(t=>t.Text.Length>0))
                        aligned &= Math.Abs(Center(icon,content)-Center(text,content)) <= .5;
                }
                Check(aligned,"Tray action icons, labels and numeric hints share vertical centers in theme "+theme);
                Call(menu,"Dismiss");
            }
            pages.SelectedItem = pages.Items[1];
            var black = editors[OsdControl.BlackStabilizer]!; var slider = (Slider)Get(black,"slider"); var number = (TextBox)Get(black,"number");
            Check(slider.SmallChange == 5 && slider.LargeChange % 5 == 0, "Black stabilizer keyboard steps stay in its legal five-point domain");
            slider.Value += slider.SmallChange;
            Check(slider.Value == 55 && number.Text == "55", "A native small increment keeps the slider and numeric draft aligned");
            number.Text = "49";
            Check(slider.Value == 55 && number.Text == "49", "Invalid typed steps remain visible for validation without moving the slider off-domain");
            Check(AutomationProperties.GetLiveSetting((TextBlock)Get(advanced,"feedback")) == Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite,
                "Draft validation and Apply results are available as a polite live announcement");
            Check(fake.Reads == reads && fake.Writes.Count == 0 && WindowsMonitorTransport.NativeReadAttempts == 0 && WindowsMonitorTransport.NativeWriteAttempts == 0,
                "All themes, layouts and draft checks perform no additional monitor I/O");
        }
        catch (Exception ex) { failures++; Environment.ExitCode=1; results.Add(new { harnessError=ex.ToString() }); }
        finally { fake.InitialReadRelease.Set(); WriteReport(); await (Task)Call(window!,"ExitAsync")!; }
    }
}
