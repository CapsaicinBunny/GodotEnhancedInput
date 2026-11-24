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
    /// <summary>Four axes (X, Y, Z, W).</summary>
    Axis4D,
    /// <summary>
    /// A rotation as a quaternion (X, Y, Z, W): tracked controllers and headsets, device orientation.
    /// "No input" is the identity rotation; magnitude is the rotation angle as a fraction of half a turn.
    /// </summary>
    Quaternion,
    /// <summary>A rotation as Euler angles in radians (X pitch, Y yaw, Z roll, Godot's YXZ order).</summary>
    Euler,
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
    /// <summary>No events.</summary>
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
    /// <summary>Every event.</summary>
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
    /// <summary>Scrolling (mouse wheel notches and trackpad pans) as a 2D amount per tick, down and right positive.</summary>
    MouseWheel,
    /// <summary>A pen or stylus: pressure, tilt or eraser.</summary>
    Pen,
    /// <summary>A motion sensor of a joypad or of the handheld device itself.</summary>
    Motion,
    /// <summary>Several sources combined (an axis from two inputs, a vector from four, a press that needs held modifiers, or the strongest of several).</summary>
    Composite,
    /// <summary>A named control of a device registered by a custom backend (wheels, flight sticks, XR controllers, anything).</summary>
    DeviceControl,
    /// <summary>A touch gesture: pinch, rotate, two-finger drag or swipe (per tick).</summary>
    Gesture,
}

/// <summary>What a <see cref="SourceKind.Gesture"/> source reads, per tick.</summary>
public enum GestureChannel
{
    /// <summary>How much two fingers (or a trackpad pinch) spread this tick: 0.1 = 10 % further apart; negative when pinching in.</summary>
    Pinch,
    /// <summary>How far two fingers turned this tick, in radians (clockwise positive on screen).</summary>
    Rotate,
    /// <summary>How far two fingers moved together this tick, in pixels (their midpoint).</summary>
    TwoFingerDrag,
    /// <summary>On the tick a quick one-finger swipe ends: its direction as a unit vector (up is −Y).</summary>
    Swipe,
}

/// <summary>What a <see cref="SourceKind.Pen"/> source reads.</summary>
public enum PenChannel
{
    /// <summary>Pressure 0…1.</summary>
    Pressure,
    /// <summary>Tilt in X and Y, −1…1.</summary>
    Tilt,
    /// <summary>1 while the eraser end touches.</summary>
    Eraser,
}

/// <summary>Which motion sensor a <see cref="SourceKind.Motion"/> source reads.</summary>
public enum MotionSensor
{
    /// <summary>Acceleration including gravity, m/s².</summary>
    Accelerometer,
    /// <summary>Rotation rate, rad/s.</summary>
    Gyroscope,
    /// <summary>Gravity direction, m/s².</summary>
    Gravity,
    /// <summary>Magnetic field (handheld only).</summary>
    Magnetometer,
    /// <summary>
    /// Orientation as a quaternion, fused by the player from the gyroscope and the accelerometer (pads) or
    /// gravity sensor (handheld): the gyroscope turns it, gravity keeps its tilt from drifting. Heading
    /// (yaw) has no reference and drifts slowly; <see cref="InputPlayer.ResetOrientation"/> recentres it.
    /// </summary>
    Orientation,
}

/// <summary>
/// What an axis made of two opposing inputs reads when both are held (left and right together) — the
/// "simultaneous opposing cardinal directions" rule of arcade sticks and hitboxes.
/// </summary>
public enum OpposingInputs
{
    /// <summary>They cancel out: 0 (the default).</summary>
    Neutral,
    /// <summary>The one pressed last wins (smooth strafing: hold A, tap D to switch).</summary>
    LastWins,
    /// <summary>The one pressed first wins; the other is ignored until it is released.</summary>
    FirstWins,
    /// <summary>The negative side wins (left; on a 2D axis's vertical, up — Godot's up is −Y).</summary>
    NegativeWins,
    /// <summary>The positive side wins (right; on a 2D axis's vertical, down).</summary>
    PositiveWins,
}

/// <summary>How a <see cref="SourceKind.Composite"/> source combines its parts.</summary>
public enum CompositeMode
{
    /// <summary>Parts: negative, positive. X = strength of positive − strength of negative.</summary>
    Axis1D,
    /// <summary>Parts: up, down, left, right (Godot screen convention, up = −Y).</summary>
    Axis2D,
    /// <summary>The last part is the value; it only counts while every other part is held (Ctrl + S, Shift + click).</summary>
    WithModifiers,
    /// <summary>The strongest part.</summary>
    AnyOf,
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
    /// <summary>Only <see cref="EnhancedInput.AssignJoypad(InputPlayer, int)"/> assigns joypads.</summary>
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
    /// <summary>A touchscreen (including on-screen controls, which write virtual controls) and the handheld device's own sensors.</summary>
    Touch,
    /// <summary>A pen or stylus.</summary>
    Pen,
    /// <summary>A device provided by a custom backend (see <see cref="InputBackend"/>).</summary>
    Custom,
}

/// <summary>A set of device families (for <see cref="InputMode"/>).</summary>
[Flags]
public enum DeviceKinds
{
    /// <summary>No device kinds.</summary>
    None = 0,
    /// <summary>Keyboard and mouse.</summary>
    KeyboardMouse = 1 << 0,
    /// <summary>Joypads.</summary>
    Joypad = 1 << 1,
    /// <summary>The touchscreen.</summary>
    Touch = 1 << 2,
    /// <summary>Pens.</summary>
    Pen = 1 << 3,
    /// <summary>Backend devices.</summary>
    Custom = 1 << 4,
}

/// <summary>Conversions between <see cref="InputDeviceKind"/> and <see cref="DeviceKinds"/>.</summary>
public static class DeviceKindsExtensions
{
    /// <summary>The flag of one device family (none for <see cref="InputDeviceKind.Other"/>).</summary>
    public static DeviceKinds ToFlag(this InputDeviceKind kind) => kind switch
    {
        InputDeviceKind.KeyboardMouse => DeviceKinds.KeyboardMouse,
        InputDeviceKind.Joypad => DeviceKinds.Joypad,
        InputDeviceKind.Touch => DeviceKinds.Touch,
        InputDeviceKind.Pen => DeviceKinds.Pen,
        InputDeviceKind.Custom => DeviceKinds.Custom,
        _ => DeviceKinds.None,
    };

    /// <summary>Whether the set contains a family.</summary>
    public static bool Has(this DeviceKinds set, InputDeviceKind kind) => kind != InputDeviceKind.Other && (set & kind.ToFlag()) != 0;
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
    /// <summary>Steam Deck: A/B/X/Y, L1/R1, L2/R2, View/Menu, L4/L5/R4/R5 back buttons.</summary>
    SteamDeck,
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
    /// <summary>The left stick.</summary>
    Left,
    /// <summary>The right stick.</summary>
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
    /// <summary>
    /// Once per frame, at the first moment after Godot has delivered the frame's input: the first physics
    /// step if the frame has one, otherwise <c>_Process</c>. Code in <c>_PhysicsProcess</c> and in
    /// <c>_Process</c> both see this frame's input (no frame of delay), and one-tick events (a press)
    /// stay visible for the whole frame. Time steps are measured on the real clock (times
    /// <c>Engine.TimeScale</c>). The default.
    /// </summary>
    Auto,
    /// <summary>In <c>_Process</c>. Code in <c>_PhysicsProcess</c> then sees the previous frame's input.</summary>
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

/// <summary>How <see cref="EnhancedInput"/> decides whose pad a reconnected pad without a serial number is.</summary>
public enum ReconnectPolicy
{
    /// <summary>
    /// Give it back: to the only player who lost a pad of that model, or — when several did — to the
    /// one who lost theirs first. <see cref="EnhancedInput.JoypadReconnectAmbiguous"/> fires in the
    /// second case so the game can correct it with <see cref="EnhancedInput.ResolveJoypadReconnect"/>.
    /// </summary>
    Automatic,

    /// <summary>
    /// Never guess for pads without a serial number: such a pad that could be a player's missing pad
    /// stays unassigned and its input reaches nobody until the game calls
    /// <see cref="EnhancedInput.ResolveJoypadReconnect"/> (for example after "Player 2, press a button").
    /// Pads with serial numbers still return on their own.
    /// </summary>
    Ask,
}

/// <summary>How an action reports while it is held (<see cref="EnhancedInputAction.Evaluation"/>).</summary>
public enum ActionEvaluation
{
    /// <summary>Movement, looking, steering: triggered every frame while held (default).</summary>
    Continuous,
    /// <summary>Raises <see cref="ActionEvents.Triggered"/> only on frames where its value changed.</summary>
    OnChange,
    /// <summary>Buttons: only presses, releases and phase changes are raised, never a steady triggered per frame.</summary>
    EventDriven,
    /// <summary>
    /// Every input event on any of its bindings is reported on its own, with that binding's value and
    /// source (not the strongest of all) — for drawing, multi-touch and rhythm input.
    /// </summary>
    PassThrough,
    /// <summary>Poses: triggered exactly while tracked, whatever the value (an XR controller at the identity is still there).</summary>
    Tracked,
}

/// <summary>How analog channels are sampled between ticks (<see cref="RawInputState.AnalogSampling"/>).</summary>
public enum AnalogSampling
{
    /// <summary>Only crossings of zero are kept; other movement is coalesced into the latest value (cheapest).</summary>
    Latest,
    /// <summary>Crossings of zero and every turning point (peaks and dips): thresholds crossed and recrossed within one frame are seen (default).</summary>
    Extremes,
    /// <summary>Every sample is evaluated on its own (precise, more steps per tick).</summary>
    All,
}

/// <summary>Why held input ended without being released by the player.</summary>
public enum InputEndReason
{
    /// <summary>A genuine release (or the action simply stopped firing).</summary>
    Released,
    /// <summary>The device the input came from disconnected.</summary>
    DeviceDisconnected,
    /// <summary>The window or application lost focus.</summary>
    FocusLost,
    /// <summary>The application was suspended (mobile).</summary>
    ApplicationSuspended,
    /// <summary>The context that bound the input was removed.</summary>
    ContextRemoved,
    /// <summary>The device went to another player.</summary>
    DeviceReassigned,
    /// <summary>The binding was rebound or its modifiers or triggers changed.</summary>
    BindingChanged,
    /// <summary>The player's input mode changed and the binding's group is no longer active.</summary>
    ModeChanged,
    /// <summary>Blocked by a higher context, consumed, or ignored until released.</summary>
    Blocked,
    /// <summary>The scene tree was paused and the action does not run while paused.</summary>
    Paused,
    /// <summary>Raw input was cleared by code (<see cref="InputPlayer.ClearRawInput"/>).</summary>
    Cleared,
    /// <summary>A replay started or stopped.</summary>
    Replay,
    /// <summary>A tracked pose, gaze or position stopped being tracked (XR, eye trackers).</summary>
    TrackingLost,
    /// <summary>The operating system took the input away (a touch canceled by a system gesture).</summary>
    SystemCanceled,
}
