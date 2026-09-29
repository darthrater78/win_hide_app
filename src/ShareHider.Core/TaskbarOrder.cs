namespace ShareHider.Core;

/// <summary>
/// Works out how to put restored taskbar buttons back in their original places.
/// </summary>
/// <remarks>
/// Windows adds a re-added button at the end of the taskbar and has no API to place
/// one. The only move available is "take a button off and add it back", which sends
/// it to the end. So to restore an order, the buttons that belong after the restored
/// one are cycled to the end, in order. Buttons are identified by their app id, the
/// same id the taskbar groups by.
/// </remarks>
public static class TaskbarOrder
{
    /// <summary>
    /// Updates the remembered order with what the taskbar shows now. Apps that are
    /// hidden (so missing from <paramref name="observed"/>) keep their remembered place,
    /// right after the app they followed; apps that closed are forgotten.
    /// </summary>
    public static List<string> Merge(IReadOnlyList<string> known, IReadOnlyList<string> observed, IReadOnlySet<string> hidden)
    {
        var result = observed.Distinct().ToList();
        var placed = result.ToHashSet();
        var insertAt = 0;
        foreach (var id in known)
        {
            if (placed.Contains(id))
            {
                insertAt = result.IndexOf(id) + 1;
            }
            else if (hidden.Contains(id))
            {
                result.Insert(insertAt++, id);
                placed.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// The apps to cycle to the end, in order, so that <paramref name="current"/> ends up
    /// in the order of <paramref name="desired"/>. Apps missing from
    /// <paramref name="desired"/> (opened since) keep their place relative to each other,
    /// after the known ones. Returns an empty list when the order is already right.
    /// </summary>
    public static List<string> Plan(IReadOnlyList<string> desired, IReadOnlyList<string> current)
    {
        var items = current.Distinct().ToList();
        var rank = new Dictionary<string, int>();
        for (var i = 0; i < desired.Count; i++)
        {
            rank.TryAdd(desired[i], i);
        }

        var position = items.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var target = items
            .OrderBy(id => rank.GetValueOrDefault(id, int.MaxValue))
            .ThenBy(id => position[id])
            .ToList();

        // Buttons that stay put keep their relative order and end up first, so they must be
        // a prefix of the target that already appears in increasing position.
        var keep = 0;
        while (keep < target.Count && (keep == 0 || position[target[keep]] > position[target[keep - 1]]))
        {
            keep++;
        }

        return target.Skip(keep).ToList();
    }

    /// <summary>
    /// Like <see cref="Plan(IReadOnlyList{string}, IReadOnlyList{string})"/>, for a taskbar with pinned apps.
    /// A pinned app's button never moves (its windows go back to the pin), and a re-added
    /// button always lands after the last pin. So only the running, unpinned buttons after the
    /// last pin can be put in order. An unpinned app the user had dragged in among the pins
    /// can't be put back there: it is left at the end (<c>Displaced</c>), and nothing else
    /// is cycled on its account, since cycling would carry those buttons past the pins too.
    /// </summary>
    /// <param name="movable">Apps that have a window to cycle; others can't move either.</param>
    public static (List<string> Moves, List<string> Displaced) Plan(
        IReadOnlyList<string> desired,
        IReadOnlyList<string> current,
        IReadOnlySet<string> pinned,
        IReadOnlySet<string> movable)
    {
        var lastPin = LastIndexOf(current, pinned);
        var lastPinDesired = LastIndexOf(desired, pinned);
        var rank = new Dictionary<string, int>();
        for (var i = 0; i < desired.Count; i++)
        {
            rank.TryAdd(desired[i], i);
        }

        var tail = new List<string>();
        var displaced = new List<string>();
        for (var i = lastPin + 1; i < current.Count; i++)
        {
            var id = current[i];
            if (pinned.Contains(id) || !movable.Contains(id))
            {
                continue;
            }

            if (rank.TryGetValue(id, out var r) && r < lastPinDesired)
            {
                displaced.Add(id);
            }
            else
            {
                tail.Add(id);
            }
        }

        var inTail = tail.ToHashSet();
        return (Plan(desired.Where(inTail.Contains).ToList(), tail), displaced);
    }

    private static int LastIndexOf(IReadOnlyList<string> ids, IReadOnlySet<string> set)
    {
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (set.Contains(ids[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
