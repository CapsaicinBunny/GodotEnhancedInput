using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Shortcuts for the common case: an action asks the default player (player 0 of the
/// <see cref="EnhancedInput"/> autoload) about itself, so gameplay code needs no player reference.
/// Every shortcut takes an optional player for local multiplayer.
/// <code>
/// if (Jump.WasPressed()) Velocity += Vector3.Up * 6f;
/// Vector2 move = Move.ReadVector2();
/// Fire.OnTriggered(Shoot);
/// </code>
/// Reads return "nothing" (false, zero, the identity) when there is no player yet.
/// </summary>
public partial class EnhancedInputAction
{
    /// <summary>An on/off action ("jump", "fire").</summary>
    public static EnhancedInputAction Button(string name) => Create(name, ActionValueKind.Digital);

    /// <summary>A one-axis action ("throttle", "zoom").</summary>
    public static EnhancedInputAction Axis(string name) => Create(name, ActionValueKind.Axis1D);

    /// <summary>A two-axis action ("move", "look").</summary>
    public static EnhancedInputAction Axis2D(string name) => Create(name, ActionValueKind.Axis2D);

    /// <summary>A three-axis action ("fly", a tracked position).</summary>
    public static EnhancedInputAction Axis3D(string name) => Create(name, ActionValueKind.Axis3D);

    /// <summary>A rotation action (a controller's or the headset's orientation), read as a quaternion.</summary>
    public static EnhancedInputAction Rotation(string name) => Create(name, ActionValueKind.Quaternion);

    private static InputPlayer PlayerOr(InputPlayer player) => player ?? EnhancedInput.DefaultPlayer;

    /// <summary>True while the action is triggered (held, for an action without triggers).</summary>
    public bool IsTriggered(InputPlayer player = null) => PlayerOr(player)?.IsTriggered(this) ?? false;

    /// <summary>True on the frame the action fired (its triggers were met).</summary>
    public bool WasTriggered(InputPlayer player = null) => PlayerOr(player)?.WasTriggered(this) ?? false;

    /// <summary>True on the frame its input went down (the action started), whatever its triggers.</summary>
    public bool WasPressed(InputPlayer player = null) => PlayerOr(player)?.WasPressed(this) ?? false;

    /// <summary>True on the frame its input was let go after the action fired (completed; not when cut off).</summary>
    public bool WasReleased(InputPlayer player = null) => PlayerOr(player)?.WasReleased(this) ?? false;

    /// <summary>True on the frame the action ended without firing, or was cut off (see <see cref="ActionState.EndReason"/>).</summary>
    public bool WasCanceled(InputPlayer player = null) => PlayerOr(player)?.WasCanceled(this) ?? false;

    /// <summary>
    /// Input buffering: true once per activation, if the action became triggered within the last
    /// <paramref name="withinSeconds"/> (or is triggered now) — <c>if (IsOnFloor() &amp;&amp; jump.ConsumeTriggered(0.15)) Jump();</c>
    /// See <see cref="InputPlayer.ConsumeTriggered"/>.
    /// </summary>
    public bool ConsumeTriggered(double withinSeconds = 0.0, InputPlayer player = null) => PlayerOr(player)?.ConsumeTriggered(this, withinSeconds) ?? false;

    /// <summary>Whether the action became triggered within the last <paramref name="withinSeconds"/> and was not consumed (see <see cref="ConsumeTriggered"/>).</summary>
    public bool WasTriggeredWithin(double withinSeconds, InputPlayer player = null) => PlayerOr(player)?.WasTriggeredWithin(this, withinSeconds) ?? false;

    /// <summary>The action's value.</summary>
    public ActionValue Read(InputPlayer player = null) => PlayerOr(player)?.GetValue(this) ?? ActionValue.Zero(ValueKind);

    /// <summary>The value as on/off.</summary>
    public bool ReadBool(InputPlayer player = null) => Read(player).AsBool();

    /// <summary>The value's first axis (a trigger, a throttle).</summary>
    public float ReadFloat(InputPlayer player = null) => Read(player).AsFloat();

    /// <summary>The value's first two axes (a stick, movement, mouse look).</summary>
    public Vector2 ReadVector2(InputPlayer player = null) => Read(player).AsVector2();

    /// <summary>The value's first three axes.</summary>
    public Vector3 ReadVector3(InputPlayer player = null) => Read(player).AsVector3();

    /// <summary>The value as a rotation (identity when there is none).</summary>
    public Quaternion ReadRotation(InputPlayer player = null) => Read(player).AsQuaternion();

    /// <summary>The value with whether it is a real reading (see <see cref="InputSample"/>).</summary>
    public InputSample ReadSample(InputPlayer player = null)
        => PlayerOr(player)?.GetSample(this) ?? new InputSample(ActionValue.Zero(ValueKind), false, false, 0);

    /// <summary>Calls <paramref name="callback"/> for the given events. Dispose the result to stop.</summary>
    public InputPlayer.ActionSubscription On(ActionEvents events, Action<ActionEventArgs> callback, InputPlayer player = null)
    {
        var target = PlayerOr(player) ?? throw new InvalidOperationException(
            $"No input player for '{Name}': enable the Godot Enhanced Input plugin (it adds the EnhancedInput autoload) or pass a player.");
        return target.Bind(this, events, callback);
    }

    /// <summary>Calls <paramref name="callback"/> each time the action fires.</summary>
    public InputPlayer.ActionSubscription OnTriggered(Action callback, InputPlayer player = null) => On(ActionEvents.Triggered, _ => callback(), player);

    /// <summary>Calls <paramref name="callback"/> when the action's input goes down.</summary>
    public InputPlayer.ActionSubscription OnStarted(Action callback, InputPlayer player = null) => On(ActionEvents.Started, _ => callback(), player);

    /// <summary>Calls <paramref name="callback"/> when the input is let go after the action fired.</summary>
    public InputPlayer.ActionSubscription OnCompleted(Action callback, InputPlayer player = null) => On(ActionEvents.Completed, _ => callback(), player);

    /// <summary>Calls <paramref name="callback"/> when the action ends without firing or is cut off.</summary>
    public InputPlayer.ActionSubscription OnCanceled(Action callback, InputPlayer player = null) => On(ActionEvents.Canceled, _ => callback(), player);
}
