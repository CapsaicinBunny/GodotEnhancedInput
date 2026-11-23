using Godot;

namespace GodotEnhancedInput;

/// <summary>How <see cref="SmoothingModifier"/> smooths.</summary>
public enum SmoothingMode
{
    /// <summary>The average of the last <see cref="SmoothingModifier.Samples"/> ticks.</summary>
    Average,
    /// <summary>Moves towards the input at <see cref="SmoothingModifier.Rate"/> per second (exponential).</summary>
    Exponential,
}

/// <summary>
/// Smooths a value over time, either by averaging the last few ticks or by following the input
/// exponentially. Memory is kept per binding and player.
/// </summary>
[Tool]
[GlobalClass]
public partial class SmoothingModifier : InputModifier
{
    private SmoothingMode _mode = SmoothingMode.Average;

    [Export]
    public SmoothingMode Mode
    {
        get => _mode;
        set { _mode = value; NotifyPropertyListChanged(); }
    }

    /// <summary>Number of ticks averaged (Average mode).</summary>
    [Export(PropertyHint.Range, "1,60,1")] public int Samples { get; set; } = 4;

    /// <summary>How quickly the output catches up, per second (Exponential mode). Higher is snappier.</summary>
    [Export(PropertyHint.Range, "0.1,60,0.1,or_greater")] public float Rate { get; set; } = 12f;

    public override Vector3 Apply(Vector3 value, in ModifierContext context)
    {
        var memory = context.Memory;
        if (_mode == SmoothingMode.Exponential)
        {
            if (!memory.HasPrevious)
            {
                memory.Previous = value;
                memory.HasPrevious = true;
                return value;
            }
            float t = 1f - Mathf.Exp(-Rate * (float)context.Delta);
            memory.Previous = memory.Previous.Lerp(value, t);
            return memory.Previous;
        }

        int samples = Mathf.Max(1, Samples);
        if (memory.Samples == null || memory.Samples.Length != samples)
        {
            memory.Samples = new Vector3[samples];
            memory.SampleCount = 0;
            memory.SampleIndex = 0;
        }

        memory.Samples[memory.SampleIndex] = value;
        memory.SampleIndex = (memory.SampleIndex + 1) % samples;
        if (memory.SampleCount < samples) memory.SampleCount++;

        var sum = Vector3.Zero;
        for (int i = 0; i < memory.SampleCount; i++) sum += memory.Samples[i];
        return sum / memory.SampleCount;
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string name = property["name"].AsString();
        if ((name == nameof(Samples) && _mode != SmoothingMode.Average) || (name == nameof(Rate) && _mode != SmoothingMode.Exponential))
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }
}
