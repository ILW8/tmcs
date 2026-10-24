namespace Tmcs.Settings;

/// <summary>Keeps a saved window rectangle reachable when the monitors changed since it was saved.</summary>
internal static class WindowPlacement
{
    const int MinVisible = 50;

    /// <summary>Returns <paramref name="saved"/> unchanged when at least 50 x 50 px of it lies inside one working area. Otherwise it centres the rectangle on the first working area, shrinking it to fit.</summary>
    public static Rectangle Clamp(Rectangle saved, IReadOnlyList<Rectangle> workingAreas)
    {
        if (workingAreas.Count == 0) return saved;

        foreach (var area in workingAreas)
        {
            var shared = Rectangle.Intersect(saved, area);
            if (shared.Width >= MinVisible && shared.Height >= MinVisible) return saved;
        }

        var first = workingAreas[0];
        var width = Math.Min(saved.Width, first.Width);
        var height = Math.Min(saved.Height, first.Height);
        return new Rectangle(first.X + (first.Width - width) / 2, first.Y + (first.Height - height) / 2, width, height);
    }
}
