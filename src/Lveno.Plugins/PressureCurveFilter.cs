using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.Plugins;

/// <summary>Reshapes pen pressure before it reaches your apps.</summary>
[PluginName(PluginName)]
public class PressureCurveFilter : IPositionedPipelineElement<IDeviceReport>
{
    public const string PluginName = "Lveno: Pressure curve";

    [Property("Curve")]
    [ToolTip("startX,startY,controlX,controlY,endX,endY (0–1). Set by the Lveno app's curve editor.")]
    public string? Curve
    {
        get => _curve.ToString();
        set => _curve = PressureCurve.Parse(value) ?? PressureCurve.Linear;
    }

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport?>? Emit;

    private PressureCurve _curve = PressureCurve.Linear;

    public void Consume(IDeviceReport? report)
    {
        if (report is ITabletReport tablet)
        {
            float max = Tablet?.Properties.Specifications.Pen?.MaxPressure ?? 8191;
            if (max > 0)
                tablet.Pressure = (uint)MathF.Round(_curve.Apply(tablet.Pressure / max) * max);
        }
        Emit?.Invoke(report);
    }
}
