using Godot;

namespace GodotEnhancedInput;

/// <summary>One step of a <see cref="ComboTrigger"/>.</summary>
[Tool]
[GlobalClass]
public partial class ComboStep : Resource
{
    /// <summary>The action to perform.</summary>
    [Export] public GameAction Action { get; set; }

    /// <summary>Which of the action's events completes the step (any of the flags).</summary>
    [Export(PropertyHint.Flags, "Started:1,Ongoing:2,Triggered:4,Completed:8,Canceled:16")]
    public ActionEvents CompletesOn { get; set; } = ActionEvents.Triggered;

    /// <summary>Seconds allowed since the previous step; 0 for no limit. Ignored on the first step.</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater,suffix:s")] public float TimeLimit { get; set; } = 0.5f;

    /// <summary>A step: <paramref name="action"/> raising <paramref name="completesOn"/> within <paramref name="timeLimit"/> seconds of the previous step (0: no limit).</summary>
    public static ComboStep Create(GameAction action, ActionEvents completesOn = ActionEvents.Triggered, float timeLimit = 0.5f)
        => new() { Action = action, CompletesOn = completesOn, TimeLimit = timeLimit };
}
