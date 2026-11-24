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
    public InputFrameView PhysicsFrame => _physicsFrame ??= NewFrame(InputLoop.Physics);

    /// <summary>What <c>_Process</c> code should read edges from: the events since the previous rendered frame.</summary>
    public InputFrameView ProcessFrame => _processFrame ??= NewFrame(InputLoop.Process);

    /// <summary>
    /// Makes both loop views now, so they collect from the next tick on — call it at start-up when the
    /// first read may come after input already happened. (A view made later starts with the latest tick's events.)
    /// </summary>
    public void EnableFrameViews()
    {
        _ = PhysicsFrame;
        _ = ProcessFrame;
    }

    // A new view starts with the events of the latest tick, so a first read right after it is not empty.
    private InputFrameView NewFrame(InputLoop loop)
    {
        var view = new InputFrameView(this, loop);
        foreach (var pair in _states)
            if (pair.Value.LastTick == _tick && _tick > 0) view.Seed(pair.Key, pair.Value.Events);
        return view;
    }

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

    /// <summary>Whether this player has a pointer region in the tree (split screen).</summary>
    public bool HasPointerRegion => PointerRegion != null && IsInstanceValid(PointerRegion) && PointerRegion.IsInsideTree();

    private static bool PointerPosition(InputEvent @event, out Vector2 position)
    {
        switch (@event)
        {
            case InputEventMouse mouse: position = mouse.Position; return true;
            case InputEventScreenTouch touch: position = touch.Position; return true;
            case InputEventScreenDrag drag: position = drag.Position; return true;
            case InputEventGesture gesture: position = gesture.Position; return true;
            default: position = default; return false;
        }
    }

    /// <summary>
    /// Whether a pointer event belongs to this player's <see cref="PointerRegion"/>, and the event in the
    /// region's coordinates. Non-pointer events, and players without a usable region, take every event as is.
    /// (The autoload also keeps a pressed pointer with the player it was pressed for until it is released,
    /// see <see cref="EnhancedInput.FeedEvent(InputEvent, IntakeStage)"/>.)
    /// </summary>
    public bool TryLocalizePointer(InputEvent @event, out InputEvent local)
    {
        local = @event;
        if (!HasPointerRegion || !PointerPosition(@event, out var position)) return true;
        if (!PointerRegion.IsVisibleInTree() || !PointerRegion.GetGlobalRect().HasPoint(position)) return false;
        local = ToRegion(@event);
        return true;
    }

    /// <summary>A pointer event in this player's region's coordinates, wherever it is (for a captured pointer).</summary>
    public InputEvent ToRegion(InputEvent @event)
    {
        if (!HasPointerRegion || !PointerPosition(@event, out _)) return @event;
        var region = PointerRegion;
        var toLocal = region.GetGlobalTransformWithCanvas().AffineInverse();
        if (region is SubViewportContainer { Stretch: true } container && container.StretchShrink > 1)
            toLocal = Transform2D.Identity.Scaled(Vector2.One / container.StretchShrink) * toLocal;
        return @event.XformedBy(toLocal);
    }

    /// <summary>
    /// When on (default), actions with <see cref="EnhancedInputAction.MirrorToInputMap"/> press that Godot
    /// action while this player has them triggered (see <see cref="InputMapBridge"/>).
    /// </summary>
    [Export] public bool MirrorsToInputMap { get; set; } = true;

    private readonly HashSet<EnhancedInputAction> _mirrored = new();
    private readonly List<(EnhancedInputAction Action, string Target, float Strength)> _contributions = new();
    private int _mirrorPlan = -1;
    private bool _bridgeActive;

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
        UpdateMirrors();
    }

    /// <summary>
    /// Sends this player's mirrored actions to the bridge, replacing what it sent before — so turning
    /// mirroring off, or renaming or removing a target, releases what was pressed.
    /// </summary>
    private void UpdateMirrors()
    {
        if (!MirrorsToInputMap || !InputMapBridge.Enabled)
        {
            if (_bridgeActive) InputMapBridge.Forget(this);
            _bridgeActive = false;
            _mirrorPlan = -1;
            return;
        }
        if (_mirrorPlan != PlanCompileCount) FindMirroredActions();
        if (_mirrored.Count == 0 && !_bridgeActive) return;

        _contributions.Clear();
        foreach (var action in _mirrored)
        {
            if (string.IsNullOrEmpty(action.MirrorToInputMap) || InputMapBridge.IsBlocked(this, action)) continue;
            float strength = 0f;
            if (_states.TryGetValue(action, out var state) && state.IsTriggered)
            {
                float magnitude = state.Value.Magnitude;
                strength = magnitude > 0f ? Mathf.Min(1f, magnitude) : 1f;
            }
            _contributions.Add((action, action.MirrorToInputMap, strength));
        }
        InputMapBridge.SetContributions(this, _contributions);
        _bridgeActive = _contributions.Count > 0;
    }

    // Mirrored actions of the active contexts (kept once seen, so a removed context still reports its
    // actions as released), and the Godot actions they read, for the bridge's feedback check.
    private void FindMirroredActions()
    {
        _mirrorPlan = PlanCompileCount;
        var links = new List<(EnhancedInputAction Action, string From, string To)>();
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
                    if (key.Kind == RawKind.InputMapAction && !string.IsNullOrEmpty(key.Name)) links.Add((action, key.Name, action.MirrorToInputMap));
            }
        InputMapBridge.SetLinks(this, links);
    }
}
