namespace XScreen.App;

// UI-thread state: hardware confirmation and the user's newest edit have separate lives.
readonly record struct ControlIntent(long Revision, uint Target);

sealed class ControlEditState
{
    long revision, confirmedRevision, submittedRevision;
    long? retainedConfirmation;
    public uint Confirmed { get; private set; }
    public uint Desired { get; private set; }
    public bool Pending => revision != confirmedRevision;
    public bool HasUnsubmitted => Pending && submittedRevision != revision;

    public void Load(uint value)
    {
        Confirmed = Desired = value;
        confirmedRevision = submittedRevision = ++revision;
        retainedConfirmation = null;
    }
    public bool Edit(uint value)
    {
        if (Desired == value) return false;
        Desired = value; revision++; retainedConfirmation = null; return true;
    }
    public ControlIntent? TakeSubmission()
    {
        if (!HasUnsubmitted) return null;
        submittedRevision = revision;
        return new(revision, Desired);
    }
    public bool IsCurrent(ControlIntent intent) => intent.Revision == revision;
    public void Confirm(ControlIntent intent, uint actual)
    {
        Confirmed = actual;
        // A completed readback must never move a newer drag, key press or number edit.
        if (IsCurrent(intent) || retainedConfirmation == intent.Revision || !Pending)
        { Desired = actual; confirmedRevision = revision; retainedConfirmation = null; }
    }
    public void Cancel(ControlIntent? inFlight = null)
    {
        if (inFlight is not { } active) { Load(Confirmed); return; }
        // Discard only the unsent edit. A native write already in progress still
        // needs its one readback, without replaying that target or showing the old value.
        Desired = active.Target; submittedRevision = ++revision;
        retainedConfirmation = active.Revision;
    }
}
