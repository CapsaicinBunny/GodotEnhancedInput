using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// A player that runs on its own — in editor plugins and <c>@tool</c> scripts (where the autoload does not
/// run), or in a scene that should not depend on the autoload. It activates <see cref="Contexts"/> when
/// ready, ticks itself in <c>_Process</c> and takes the events it receives (with <see cref="ListenToInput"/>)
/// or that are fed to it. In an editor plugin, feed it the viewport's events:
/// <code>
/// public override int _Forward3DGuiInput(Camera3D camera, InputEvent e)
/// {
///     _input.FeedEvent(e);
///     return _input.WasPressed(snap) ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;
/// }
/// </code>
/// </summary>
[Tool]
[GlobalClass]
public partial class ToolInputPlayer : InputPlayer
{
    /// <summary>Mapping contexts activated when the node is ready.</summary>
    [Export] public Array<EnhancedInputMappingContext> Contexts { get; set; } = new();

    /// <summary>Tick in <c>_Process</c> (default). Off: call <see cref="InputPlayer.Tick"/> yourself.</summary>
    [Export] public bool TickInProcess { get; set; } = true;

    /// <summary>Take the input events this node receives (<c>_Input</c>). Editor plugins feed events instead.</summary>
    [Export] public bool ListenToInput { get; set; } = true;

    /// <summary>Creates a self-running player.</summary>
    public ToolInputPlayer()
    {
        ProcessMode = ProcessModeEnum.Always;
        PollMotionSensors = false;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        foreach (var context in Contexts)
            if (context != null && !HasContext(context)) AddContext(context, context.DefaultPriority);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (TickInProcess) Tick(delta);
        PublishFrameIfUsed(InputLoop.Process);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta) => PublishFrameIfUsed(InputLoop.Physics);

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (ListenToInput) FeedEvent(@event);
    }
}
