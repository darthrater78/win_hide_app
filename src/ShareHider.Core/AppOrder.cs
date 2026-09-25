namespace ShareHider.Core;

/// <summary>
/// Keeps the taskbar mockup's buttons in a stable order: apps stay where they first
/// appeared and new apps go on the end, like the real taskbar. Ordering by window
/// z-order instead would make buttons jump around every time focus changed.
/// </summary>
public sealed class AppOrder
{
    private readonly List<string> _order = [];

    /// <summary>Updates the order for the apps running now and returns them in display order.</summary>
    public IReadOnlyList<string> Update(IEnumerable<string> runningApps)
    {
        var running = runningApps.ToHashSet();
        _order.RemoveAll(app => !running.Contains(app));
        var known = _order.ToHashSet();
        foreach (var app in running.Where(app => !known.Contains(app)).Order(StringComparer.Ordinal))
        {
            _order.Add(app);
        }

        return _order.ToList();
    }
}
