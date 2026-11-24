using System;

namespace GodotEnhancedInput;

/// <summary>The shape of a game action's value.</summary>
public enum ActionValueKind
{
    /// <summary>On or off. The strength of the source is kept in X; the value reads as true when non-zero.</summary>
    Digital,
    /// <summary>One axis, in X.</summary>
    Axis1D,
    /// <summary>Two axes, in X and Y.</summary>
    Axis2D,
    /// <summary>Three axes.</summary>
    Axis3D,
}

/// <summary>Result of evaluating triggers for one tick. Ordered from weakest to strongest.</summary>
public enum TriggerPhase
{
    /// <summary>Nothing is happening.</summary>
    Idle = 0,
    /// <summary>The triggers are partway towards firing (e.g. a hold that has not reached its time yet).</summary>
    Ongoing = 1,
    /// <summary>The triggers fire this tick.</summary>
    Triggered = 2,
}

/// <summary>Events raised when an action's phase changes from one tick to the next.</summary>
[Flags]
public enum ActionEvents
{
    None = 0,
    /// <summary>The action left <see cref="TriggerPhase.Idle"/>.</summary>
    Started = 1 << 0,
    /// <summary>The action is <see cref="TriggerPhase.Ongoing"/> this tick.</summary>
    Ongoing = 1 << 1,
    /// <summary>The action is <see cref="TriggerPhase.Triggered"/> this tick.</summary>
    Triggered = 1 << 2,
    /// <summary>The action went from triggered back to idle.</summary>
    Completed = 1 << 3,
    /// <summary>The action went from ongoing back to idle without triggering.</summary>
    Canceled = 1 << 4,
    All = Started | Ongoing | Triggered | Completed | Canceled,
}

/// <summary>How a trigger takes part in deciding the phase of the set it belongs to.</summary>
public enum TriggerRole
{
    /// <summary>Any one explicit trigger firing is enough.</summary>
    Explicit,
    /// <summary>Every implicit trigger must fire.</summary>
    Implicit,
    /// <summary>When a blocker fires, the whole set stays idle.</summary>
    Blocker,
}

/// <summary>How the values of several bindings that drive the same action are combined.</summary>
public enum ValueAccumulation
{
    /// <summary>Per component, the value with the largest magnitude wins.</summary>
    Strongest,
    /// <summary>Components are added up, so opposite inputs cancel.</summary>
    Sum,
}

/// <summary>What an <see cref="InputSource"/> reads.</summary>
public enum SourceKind
{
    /// <summary>Reads nothing (always zero). Useful for bindings that only carry triggers, such as combos.</summary>
    None,
    /// <summary>One keyboard key (1 when held).</summary>
    Key,
    /// <summary>A positive and a negative key on one axis.</summary>
    KeyAxis1D,
    /// <summary>Four keys on two axes, up being −Y.</summary>
    KeyAxis2D,
    /// <summary>A mouse button; wheel notches give one tick of strength.</summary>
    MouseButton,
    /// <summary>Mouse movement in pixels, summed over the tick.</summary>
    MouseMotion,
    /// <summary>The mouse position in the viewport, in pixels.</summary>
    MousePosition,
    /// <summary>A joypad button.</summary>
    JoyButton,
    /// <summary>A single joypad axis (sticks or analog triggers).</summary>
    JoyAxis,
    /// <summary>Both axes of a joypad stick.</summary>
    JoyStick,
    /// <summary>An action from Godot's InputMap, by name.</summary>
    InputMapAction,
    /// <summary>A named control set from code (on-screen controls, touch, tests).</summary>
    Virtual,
    /// <summary>A finger on a touchscreen: pressed, position or drag.</summary>
    Touch,
}

/// <summary>
/// What kind of quantity a source produces. They are not processed the same way: a state is held, a
/// delta is an amount per tick (already "per frame" — do not multiply it by delta time again), an
/// absolute value is a position.
/// </summary>
public enum ValueNature
{
    /// <summary>Held buttons, stick and trigger positions, pedals.</summary>
    State,
    /// <summary>Mouse motion, scrolling, touch drags, per-tick deltas.</summary>
    Delta,
    /// <summary>Pointer and touch positions, tracked poses.</summary>
    Absolute,
}

/// <summary>What a <see cref="SourceKind.Touch"/> source reads.</summary>
public enum TouchChannel
{
    /// <summary>1 while the finger is down.</summary>
    Pressed,
    /// <summary>The finger's position in viewport pixels.</summary>
    Position,
    /// <summary>The finger's movement in pixels, summed over the tick.</summary>
    Drag,
}

/// <summary>How joypads that nobody owns become a player's.</summary>
public enum JoinMode
{
    /// <summary>Only <see cref="EnhancedInput.AssignJoypad"/> assigns joypads.</summary>
    Manual,
    /// <summary>A button press on an unowned joypad assigns it (creating a player if needed).</summary>
    OnButtonPress,
}

/// <summary>A family of input devices, for prompts and "last used device" tracking.</summary>
public enum InputDeviceKind
{
    /// <summary>Not known yet, or not tied to a device (InputMap actions).</summary>
    Other,
    /// <summary>Keyboard and mouse.</summary>
    KeyboardMouse,
    /// <summary>A joypad.</summary>
    Joypad,
    /// <summary>A touchscreen (including on-screen controls, which write virtual controls).</summary>
    Touch,
}

/// <summary>How joypad buttons and axes are named in prompts.</summary>
public enum JoypadStyle
{
    /// <summary>Position-based names (South, East, …), used when the pad is not recognised.</summary>
    Generic,
    /// <summary>A/B/X/Y, LB/RB, LT/RT, View/Menu.</summary>
    Xbox,
    /// <summary>Cross/Circle/Square/Triangle, L1/R1, L2/R2, Create/Options.</summary>
    PlayStation,
    /// <summary>B/A/Y/X by position, L/R, ZL/ZR, −/+.</summary>
    Nintendo,
}

/// <summary>When an on-screen control is shown.</summary>
public enum TouchControlVisibility
{
    /// <summary>Always.</summary>
    Always,
    /// <summary>Only when the device has a touchscreen.</summary>
    TouchscreenOnly,
}

/// <summary>How a key source matches keyboard events.</summary>
public enum KeyMatch
{
    /// <summary>By key position, whatever the keyboard layout (recommended for gameplay).</summary>
    Physical,
    /// <summary>By the key code the layout produces.</summary>
    Logical,
    /// <summary>By the label printed on the key.</summary>
    Label,
}

/// <summary>Which stick of a joypad.</summary>
public enum JoyStickSide
{
    Left,
    Right,
}

/// <summary>Which part of a joypad axis an <see cref="InputSource"/> reads.</summary>
public enum JoyAxisRange
{
    /// <summary>The whole axis, −1…1.</summary>
    Full,
    /// <summary>Only the positive half, as 0…1.</summary>
    Positive,
    /// <summary>Only the negative half, as 0…1.</summary>
    Negative,
}

/// <summary>What happens to other bindings that use the same input when a slot is rebound.</summary>
public enum RebindConflictPolicy
{
    /// <summary>Rebind anyway; conflicts are only reported.</summary>
    Allow,
    /// <summary>Do not rebind when there is any conflict.</summary>
    Reject,
    /// <summary>The first conflicting rebindable slot takes the slot's previous source.</summary>
    Swap,
    /// <summary>Conflicting rebindable slots are cleared (left with no source).</summary>
    ClearOthers,
}

/// <summary>Where conflicts are looked for.</summary>
public enum ConflictScope
{
    /// <summary>Only in contexts that contain the slot being rebound.</summary>
    SameContext,
    /// <summary>In every context the player knows (active or registered for rebinding).</summary>
    AllContexts,
}

/// <summary>When <see cref="EnhancedInput"/> ticks its players.</summary>
public enum TickMode
{
    /// <summary>In <c>_Process</c>.</summary>
    Process,
    /// <summary>In <c>_PhysicsProcess</c>.</summary>
    Physics,
    /// <summary>Only when code calls <see cref="EnhancedInput.Tick"/> or <see cref="InputPlayer.Tick"/>.</summary>
    Manual,
}

/// <summary>How event intake shares input with Godot's GUI.</summary>
public enum GuiPolicy
{
    /// <summary>
    /// Presses are read only when no GUI control accepted them (<c>_UnhandledInput</c>);
    /// anything that lowers a value is always read (<c>_Input</c>), so nothing sticks.
    /// </summary>
    RespectGui,
    /// <summary>Everything is read in <c>_Input</c>, even while a GUI control has focus.</summary>
    ReadEverything,
}

/// <summary>Which stage of Godot's input propagation an event is being offered from.</summary>
public enum IntakeStage
{
    /// <summary>Apply the whole event, whatever it is (direct feeds, tests, replays).</summary>
    Direct,
    /// <summary>Offered from <c>_Input</c>, before the GUI.</summary>
    Early,
    /// <summary>Offered from <c>_UnhandledInput</c>, after the GUI declined it.</summary>
    Unhandled,
}
