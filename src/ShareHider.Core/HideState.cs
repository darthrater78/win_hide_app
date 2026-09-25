namespace ShareHider.Core;

/// <summary>
/// Decides whether apps should be hidden right now, from automatic share
/// detection plus the user's manual toggle.
/// </summary>
/// <remarks>
/// The manual toggle is an override that lasts until detection changes its mind,
/// so the user can un-hide during a share (or hide without one) without having to
/// remember to switch back later.
/// </remarks>
public sealed class HideState
{
    private readonly int _clearAfterPolls;
    private int _absentPolls;

    /// <param name="clearAfterPolls">
    /// Consecutive polls without a share window before sharing counts as over.
    /// This stops a sharing toolbar that briefly disappears from flickering every app back.
    /// </param>
    public HideState(int clearAfterPolls)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clearAfterPolls, 1);
        _clearAfterPolls = clearAfterPolls;
    }

    /// <summary>Turning detection off also forgets any share it saw, so turning it back on starts clean.</summary>
    public bool AutoDetect
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                SharingDetected = false;
                _absentPolls = 0;
            }
        }
    } = true;

    public bool SharingDetected { get; private set; }

    /// <summary>The user's manual choice, or null when detection decides.</summary>
    public bool? Override { get; private set; }

    public bool ShouldHide => Override ?? (AutoDetect && SharingDetected);

    /// <summary>Feeds one detection poll. Returns true when <see cref="SharingDetected"/> changed.</summary>
    public bool ReportPoll(bool shareWindowSeen)
    {
        if (shareWindowSeen)
        {
            _absentPolls = 0;
            if (SharingDetected)
            {
                return false;
            }

            SharingDetected = true;
            Override = null;
            return true;
        }

        if (!SharingDetected || ++_absentPolls < _clearAfterPolls)
        {
            return false;
        }

        SharingDetected = false;
        _absentPolls = 0;
        Override = null;
        return true;
    }

    /// <summary>Flips the current hide state and holds it until detection changes.</summary>
    public void Toggle() => Override = !ShouldHide;

    public void ClearOverride() => Override = null;
}
