using System.Globalization;

namespace Lveno.Plugins;

/// <summary>
/// A pressure curve: a smooth bend from <see cref="Start"/> to <see cref="End"/>, pulled by <see cref="Control"/>.
/// Coordinates are 0–1: X is how hard you press, Y is how strong the line comes out.
/// Start sits on the bottom edge (a click threshold) or the left edge (a minimum line).
/// End sits on the top edge (full line before full press) or the right edge (a maximum line).
/// </summary>
public readonly record struct PressureCurve(float StartX, float StartY, float ControlX, float ControlY, float EndX, float EndY)
{
    public static readonly PressureCurve Linear = new(0, 0, 0.5f, 0.5f, 1, 1);

    /// <summary>Seven presets, softest first.</summary>
    public static readonly PressureCurve[] Presets =
        new[] { 0.9f, 0.6f, 0.3f, 0f, -0.3f, -0.6f, -0.9f }
            .Select(k => new PressureCurve(0, 0, 0.5f - k * 0.5f, 0.5f + k * 0.5f, 1, 1))
            .ToArray();

    /// <summary>Makes the curve valid: points on their edges and a control that keeps it rising.</summary>
    public PressureCurve Normalized()
    {
        float sx = Math.Clamp(StartX, 0, 0.9f), sy = sx > 0 ? 0 : Math.Clamp(StartY, 0, 0.9f);
        float ex = Math.Clamp(EndX, sx + 0.05f, 1), ey = ex < 1 ? 1 : Math.Clamp(EndY, Math.Max(sy + 0.05f, 0.1f), 1);
        return new(sx, sy, Math.Clamp(ControlX, sx, ex), Math.Clamp(ControlY, 0, 1), ex, ey);
    }

    /// <summary>Line strength (0–1) for a press (0–1).</summary>
    public float Apply(float input)
    {
        if (input <= 0) return 0; // never draw while hovering
        var c = Normalized();
        if (input <= c.StartX) return c.StartX > 0 ? 0 : c.StartY;
        if (input >= c.EndX) return c.EndY;

        // x(t) only rises because the control sits between start and end, so bisection is exact enough.
        float lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            float mid = (lo + hi) / 2;
            if (Bezier(c.StartX, c.ControlX, c.EndX, mid) < input) lo = mid; else hi = mid;
        }
        return Math.Clamp(Bezier(c.StartY, c.ControlY, c.EndY, (lo + hi) / 2), 0, 1);
    }

    private static float Bezier(float a, float b, float c, float t) =>
        (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

    public override string ToString() => string.Join(",",
        new[] { StartX, StartY, ControlX, ControlY, EndX, EndY }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));

    public static PressureCurve? Parse(string? text)
    {
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 6 }) return null;
        var v = new float[6];
        for (int i = 0; i < 6; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return null;
        return new PressureCurve(v[0], v[1], v[2], v[3], v[4], v[5]).Normalized();
    }

    public bool IsClose(PressureCurve other) =>
        Math.Abs(StartX - other.StartX) + Math.Abs(StartY - other.StartY) + Math.Abs(ControlX - other.ControlX) +
        Math.Abs(ControlY - other.ControlY) + Math.Abs(EndX - other.EndX) + Math.Abs(EndY - other.EndY) < 0.02f;
}
