using XScreen.App;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var state = new ControlEditState();
foreach (var (from, to) in new (uint, uint)[] { (100, 50), (50, 100), (100, 0), (0, 100) })
{
    state.Load(from); state.Edit(to); var intent = state.TakeSubmission()!.Value;
    Check(state.Desired == to && state.Confirmed == from && state.Pending, $"{from}->{to} preserves target while readback is pending");
    Check(state.TakeSubmission() == null, "Duplicate release/capture-loss cannot submit twice");
    state.Confirm(intent, to);
    Check(state.Desired == to && state.Confirmed == to && !state.Pending, "Confirmation keeps target stationary");
}
state.Load(100); state.Edit(50); var first = state.TakeSubmission()!.Value;
state.Edit(70); state.Edit(80); var newest = state.TakeSubmission()!.Value;
Check(!state.IsCurrent(first) && state.IsCurrent(newest), "Superseded queued targets can be discarded");
state.Confirm(first, 50);
Check(state.Confirmed == 50 && state.Desired == 80 && state.Pending, "Old readback cannot overwrite newer edit");
state.Confirm(newest, 80);
Check(state.Confirmed == 80 && state.Desired == 80 && !state.Pending, "Latest request settles normally");
state.Load(50); state.Edit(60); first = state.TakeSubmission()!.Value;
state.Edit(state.Desired + 10); state.Edit(state.Desired + 10);
Check(state.Desired == 80, "Repeated increments accumulate from desired value");
state.Cancel();
Check(!state.Pending && state.TakeSubmission() == null && !state.IsCurrent(first), "Cancellation drops deferred requests");
state.Confirm(first, 60);
Check(state.Desired == 60 && state.Confirmed == 60 && !state.Pending, "An already attempted write can still finish verification after cancellation");
state.Load(60); state.Edit(80); first = state.TakeSubmission()!.Value;
state.Load(30);
Check(state.TakeSubmission() == null && !state.IsCurrent(first), "State reload invalidates old intent without submitting");
Check(!state.Edit(30) && state.TakeSubmission() == null, "Unchanged target does not send duplicate work");
state.Load(100); state.Edit(50); first = state.TakeSubmission()!.Value;
state.Edit(80); state.Cancel(first);
Check(state.Pending && !state.HasUnsubmitted && state.Desired == 50, "Cancel discards newer drag while retaining the in-flight target");
Check(state.TakeSubmission() == null, "Cancel never replays an in-flight write");
state.Confirm(first, 50);
Check(!state.Pending && state.Desired == 50 && state.Confirmed == 50, "Retained in-flight confirmation settles without becoming stuck");
state.Load(50); state.Edit(60); first = state.TakeSubmission()!.Value;
state.Edit(80); state.Cancel(first); state.Edit(90); newest = state.TakeSubmission()!.Value;
state.Confirm(first, 60);
Check(state.Pending && state.Desired == 90, "New edit after cancellation remains protected from old confirmation");
state.Confirm(newest, 90);
Check(!state.Pending && state.Desired == 90 && !state.HasUnsubmitted, "Newer edit can still settle after cancellation");
Console.WriteLine($"{checks} interaction-state checks passed; no monitor transport exists in this test.");
