using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Triggered while a 2D value (a stick, a swipe, movement) points into a sector: within
/// <see cref="SectorDegrees"/> of <see cref="Direction"/> and at least <see cref="InputTrigger.ActuationThreshold"/>
/// long — up-attacks, down-throws, a radial menu slice, 8-way gestures. With <see cref="Once"/>, it fires
/// for one tick when the value enters the sector. Directions follow Godot's screen convention: up is
/// (0, −1), right is (1, 0).
/// </summary>
[Tool]
public partial class DirectionTrigger : InputTrigger
{
    /// <summary>The sector's centre direction (need not be normalized).</summary>
    [Export] public Vector2 Direction { get; set; } = Vector2.Up;

    /// <summary>The sector's full width in degrees (90 for 4-way, 45 for 8-way).</summary>
    [Export(PropertyHint.Range, "1,360,1,suffix:°")] public float SectorDegrees { get; set; } = 90f;

    /// <summary>Fire for one tick on entering the sector, instead of every tick inside it.</summary>
    [Export] public bool Once { get; set; }

    /// <summary>Whether a 2D vector lies in the sector (length is not checked).</summary>
    public bool InSector(Vector2 value)
    {
        if (value == Vector2.Zero || Direction == Vector2.Zero) return false;
        float angle = Mathf.Abs(Mathf.RadToDeg(Direction.AngleTo(value)));
        return angle <= SectorDegrees * 0.5f + 1e-4f;
    }

    /// <inheritdoc/>
    protected override TriggerPhase Update(in TriggerContext context, bool actuated, bool wasActuated)
    {
        var m = context.Memory;   // Flag: inside last tick
        bool inside = actuated && InSector(new Vector2(context.Value.X, context.Value.Y));
        bool entered = inside && !m.Flag;
        m.Flag = inside;
        if (!inside) return TriggerPhase.Idle;
        return !Once || entered ? TriggerPhase.Triggered : TriggerPhase.Ongoing;
    }
}
