namespace ShareHider.Core;

/// <summary>
/// Decides which apps (by exe name) have their taskbar buttons hidden right now.
/// </summary>
/// <remarks>
/// Each app has a default: hidden while a share is active if it is on the auto-hide
/// list, otherwise shown. Clicking an app in the taskbar mockup sets a per-app override
/// that flips it from whatever it currently is. An override that ends up equal to the
/// default is dropped, so the app goes back to following the default. When a share
/// ends, "show" overrides are dropped: letting Outlook show during one share shouldn't
/// carry over into the next one. "Hide" overrides stay until clicked again.
/// </remarks>
public sealed class HideSelection
{
    private readonly HashSet<string> _autoHide = [];
    private readonly Dictionary<string, bool> _overrides = [];

    public HideSelection(IEnumerable<string> autoHide)
    {
        foreach (var app in autoHide)
        {
            _autoHide.Add(app);
        }
    }

    /// <summary>Apps hidden automatically while sharing, in no particular order.</summary>
    public IReadOnlyCollection<string> AutoHide => _autoHide;

    public bool IsAutoHide(string app) => _autoHide.Contains(app);

    public bool HasOverride(string app) => _overrides.ContainsKey(app);

    public bool HasAnyOverride => _overrides.Count > 0;

    /// <summary>Whether <paramref name="app"/> is hidden, given whether share-hiding is active.</summary>
    public bool IsHidden(string app, bool shareHiding) =>
        _overrides.TryGetValue(app, out var hide) ? hide : Default(app, shareHiding);

    /// <summary>Every app that should be hidden right now: the auto-hide list while sharing, adjusted by the overrides.</summary>
    public HashSet<string> HiddenApps(bool shareHiding)
    {
        var hidden = new HashSet<string>();
        if (shareHiding)
        {
            hidden.UnionWith(_autoHide);
        }

        foreach (var (app, hide) in _overrides)
        {
            if (hide)
            {
                hidden.Add(app);
            }
            else
            {
                hidden.Remove(app);
            }
        }

        return hidden;
    }

    /// <summary>Flips an app from its current state (the mockup's click).</summary>
    public void Toggle(string app, bool shareHiding)
    {
        var hide = !IsHidden(app, shareHiding);
        if (hide == Default(app, shareHiding))
        {
            _overrides.Remove(app);
        }
        else
        {
            _overrides[app] = hide;
        }
    }

    public void SetAutoHide(string app, bool autoHide)
    {
        if (autoHide)
        {
            _autoHide.Add(app);
        }
        else
        {
            _autoHide.Remove(app);
        }
    }

    /// <summary>Call when a share ends: one-share "show" overrides expire.</summary>
    public void ShareEnded()
    {
        foreach (var app in _overrides.Where(o => !o.Value).Select(o => o.Key).ToList())
        {
            _overrides.Remove(app);
        }
    }

    /// <summary>Drops every per-app override, so all apps follow their defaults again.</summary>
    public void ClearOverrides() => _overrides.Clear();

    private bool Default(string app, bool shareHiding) => shareHiding && _autoHide.Contains(app);
}
