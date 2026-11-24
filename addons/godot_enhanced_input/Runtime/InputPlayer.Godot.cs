using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Loop frame views and the InputMap output bridge.</summary>
public partial class InputPlayer
{
    private InputFrameView _physicsFrame, _processFrame;

    /// <summary>
    /// What <c>_PhysicsProcess</c> code should read edges from: the events since the previous physics step,
    /// each seen by exactly one step (see <see cref="InputFrameView"/>). <c>player.PhysicsFrame.WasPressed(jump)</c>.
    /// </summary>
    public InputFrameView PhysicsFrame => _physicsFrame ??= new InputFrameView(this, InputLoop.Physics);

    /// <summary>What <c>_Process</c> code should read edges from: the events since the previous rendered frame.</summary>
    public InputFrameView ProcessFrame => _processFrame ??= new InputFrameView(this, InputLoop.Process);

    /// <summary>The view of one loop.</summary>
    public InputFrameView GetFrame(InputLoop loop) => loop == InputLoop.Physics ? PhysicsFrame : ProcessFrame;

    // Views are made on first use, so players nobody reads this way do no extra work per tick.
    internal void PublishFrameIfUsed(InputLoop loop) => (loop == InputLoop.Physics ? _physicsFrame : _processFrame)?.Publish();

    /// <summary>
    /// Split screen: the part of the screen that is this player's — usually the <see cref="SubViewportContainer"/>
    /// showing its view. Mouse and touch events reach this player only inside it, with their positions
    /// converted to it (to the SubViewport's pixels for a stretched container), so pointer sources and
    /// <see cref="PointerSpaceModifier"/> work per player. Null (default): every pointer event, as is.
    /// Keys and joypads are not affected (see <see cref="AcceptKeyboardMouse"/> and joypad assignment).
    /// </summary>
    [Export] public Control PointerRegion { get; set; }

    /// <summary>
    /// Whether a pointer event belongs to this player's <see cref="PointerRegion"/>, and the event in the
    /// region's coordinates. Non-pointer events, and players without a usable region, take every event as is.
    /// </summary>
    public bool TryLocalizePointer(InputEvent @event, out InputEvent local)
    {
        local = @event;
        var region = PointerRegion;
        if (region == null || !IsInstanceValid(region) || !region.IsInsideTree()) return true;
        Vector2 position;
        switch (@event)
        {
            case InputEventMouse mouse: position = mouse.Position; break;
            case InputEventScreenTouch touch: position = touch.Position; break;
            case InputEventScreenDrag drag: position = drag.Position; break;
            case InputEventGesture gesture: position = gesture.Position; break;
            default: return true;
        }
        if (!region.IsVisibleInTree() || !region.GetGlobalRect().HasPoint(position)) return false;
        var toLocal = region.GetGlobalTransformWithCanvas().AffineInverse();
        if (region is SubViewportContainer { Stretch: true } container && container.StretchShrink > 1)
            toLocal = Transform2D.Identity.Scaled(Vector2.One / container.StretchShrink) * toLocal;
        local = @event.XformedBy(toLocal);
        return true;
    }

    /// <summary>
    /// When on (default), actions with <see cref="EnhancedInputAction.MirrorToInputMap"/> press that Godot
    /// action while this player has them triggered (see <see cref="InputMapBridge"/>).
    /// </summary>
    [Export] public bool MirrorsToInputMap { get; set; } = true;

    private readonly HashSet<EnhancedInputAction> _mirrored = new();
    private readonly HashSet<EnhancedInputAction> _mirrorLoops = new();
    private int _mirrorPlan = -1;

    /// <summary>After a tick: hands this tick's events to the loop views and the mirrored actions to the bridge.</summary>
    private void AfterTickOutputs()
    {
        if (_physicsFrame != null || _processFrame != null)
        {
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (state.LastTick != _tick || state.Events == ActionEvents.None) continue;
                _physicsFrame?.Collect(pair.Key, state.Events);
                _processFrame?.Collect(pair.Key, state.Events);
            }
        }

        if (!MirrorsToInputMap || !InputMapBridge.Enabled) return;
        if (_mirrorPlan != PlanCompileCount) FindMirroredActions();
        if (_mirrored.Count == 0) return;
        foreach (var action in _mirrored)
        {
            if (_mirrorLoops.Contains(action) || string.IsNullOrEmpty(action.MirrorToInputMap)) continue;
            var state = _states.TryGetValue(action, out var s) ? s : null;
            float strength = 0f;
            if (state != null && state.IsTriggered)
            {
                float magnitude = state.Value.Magnitude;
                strength = magnitude > 0f ? Mathf.Min(1f, magnitude) : 1f;
            }
            InputMapBridge.Report(this, action.MirrorToInputMap, strength);
        }
        InputMapBridge.Flush();
    }

    // Mirrored actions of the active contexts (kept once seen, so a removed context still reports its
    // actions as released), and those that read the Godot action they would mirror into.
    private void FindMirroredActions()
    {
        _mirrorPlan = PlanCompileCount;
        _mirrorLoops.Clear();
        var keys = new List<SourceKey>();
        foreach (var entry in _entries)
            foreach (var binding in entry.Context.Bindings)
            {
                var action = binding?.Action;
                if (action == null || string.IsNullOrEmpty(action.MirrorToInputMap)) continue;
                _mirrored.Add(action);
                var source = GetEffectiveSource(binding);
                if (source == null) continue;
                keys.Clear();
                source.CollectKeys(keys);
                foreach (var key in keys)
                    if (key.Kind == RawKind.InputMapAction && key.Name == action.MirrorToInputMap) _mirrorLoops.Add(action);
            }
    }
}
