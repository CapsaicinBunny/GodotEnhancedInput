using System;
using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>Raised when the profile changes what a rebindable binding reads.</summary>
    [Signal] public delegate void BindingsChangedEventHandler();

    /// <summary>Raised when a capture bound a press to a slot (the source may be refused by the conflict policy; see <see cref="RebindCompleted"/>).</summary>
    [Signal] public delegate void RebindCapturedEventHandler(string rebindId, InputSource source);

    /// <summary>Raised when a capture was canceled.</summary>
    [Signal] public delegate void RebindCaptureCanceledEventHandler(string rebindId);

    /// <summary>Raised after every rebind (by code or by capture) with its full result.</summary>
    public event Action<RebindResult> RebindCompleted;

    private InputProfile _profile;
    private readonly List<BindingContext> _rebindContexts = new();
    private string _captureId;
    private CaptureOptions _captureOptions;

    /// <summary>The player's rebinding choices. Never null; assigning one applies it from the next tick.</summary>
    public InputProfile Profile
    {
        get
        {
            if (_profile == null) Profile = new InputProfile();
            return _profile;
        }
        set
        {
            if (_profile != null) _profile.Changed -= OnProfileChanged;
            _profile = value ?? new InputProfile();
            _profile.Changed += OnProfileChanged;
            OnProfileChanged();
        }
    }

    private void OnProfileChanged() => EmitSignal(SignalName.BindingsChanged);

    /// <summary>The default file for this player's profile.</summary>
    public string DefaultProfilePath => $"user://input/player_{PlayerIndex}.json";

    /// <summary>Saves the profile (to <see cref="DefaultProfilePath"/> when no path is given).</summary>
    public Error SaveProfile(string path = null) => Profile.Save(path ?? DefaultProfilePath);

    /// <summary>Loads a profile and uses it. Returns false (and keeps the current one) if the file is missing or unreadable.</summary>
    public bool LoadProfile(string path = null)
    {
        var loaded = InputProfile.Load(path ?? DefaultProfilePath);
        if (loaded == null) return false;
        Profile = loaded;
        return true;
    }

    /// <summary>
    /// The source a binding reads for this player: the profile's override for its rebind id (or for one
    /// of its former ids, which is then moved to the current id), else the binding's own source.
    /// </summary>
    public InputSource GetEffectiveSource(ActionBinding binding)
    {
        if (binding == null) return null;
        if (!binding.IsRebindable) return binding.Source;

        var profile = Profile;
        if (profile.TryGetOverride(binding.RebindId, out var source)) return source;

        var former = binding.RebindFormerIds;
        if (former != null && former.Length > 0)
        {
            foreach (var old in former)
                if (profile.Migrate(old, binding.RebindId) && profile.TryGetOverride(binding.RebindId, out source)) return source;
        }
        return binding.Source;
    }

    // ------------------------------------------------------------------
    //  Slots
    // ------------------------------------------------------------------

    /// <summary>
    /// Makes a context's rebindable bindings appear in <see cref="GetRebindSlots"/> even while it is not
    /// active (e.g. gameplay bindings listed in a menu).
    /// </summary>
    public void RegisterRebindContext(BindingContext context)
    {
        if (context != null && !_rebindContexts.Contains(context)) _rebindContexts.Add(context);
    }

    /// <summary>Stops listing a context registered with <see cref="RegisterRebindContext"/>.</summary>
    public void UnregisterRebindContext(BindingContext context) => _rebindContexts.Remove(context);

    /// <summary>The active contexts (by priority) followed by the registered ones, without duplicates.</summary>
    public List<BindingContext> GetKnownContexts()
    {
        var known = GetContexts();
        foreach (var context in _rebindContexts)
            if (!known.Contains(context)) known.Add(context);
        return known;
    }

    /// <summary>Every rebindable slot in the known contexts, in the order they are first found.</summary>
    public List<RebindSlot> GetRebindSlots()
    {
        var slots = new List<RebindSlot>();
        var byId = new System.Collections.Generic.Dictionary<string, RebindSlot>(StringComparer.Ordinal);
        foreach (var context in GetKnownContexts())
        {
            foreach (var binding in context.Bindings)
            {
                if (binding == null || !binding.IsRebindable) continue;
                if (!byId.TryGetValue(binding.RebindId, out var slot))
                {
                    var current = GetEffectiveSource(binding);
                    slot = new RebindSlot(binding.RebindId)
                    {
                        DisplayName = string.IsNullOrEmpty(binding.RebindDisplayName) ? binding.Action?.GetDisplayName() ?? binding.RebindId : binding.RebindDisplayName,
                        Category = binding.RebindCategory ?? string.Empty,
                        Action = binding.Action,
                        DefaultSource = binding.Source,
                        CurrentSource = current,
                        IsOverridden = Profile.HasOverride(binding.RebindId),
                    };
                    byId[binding.RebindId] = slot;
                    slots.Add(slot);
                }
                slot._bindings.Add((context, binding));
            }
        }
        return slots;
    }

    /// <summary>The slot with an id, or null.</summary>
    public RebindSlot GetRebindSlot(string id)
    {
        foreach (var slot in GetRebindSlots())
            if (slot.Id == id) return slot;
        return null;
    }

    // ------------------------------------------------------------------
    //  Conflicts and rebinding
    // ------------------------------------------------------------------

    /// <summary>
    /// Bindings, other than those of slot <paramref name="id"/>, whose current source reads any input
    /// <paramref name="source"/> reads. Rebindable bindings are reported once per id.
    /// </summary>
    public List<RebindConflict> FindConflicts(string id, InputSource source, ConflictScope scope = ConflictScope.SameContext)
    {
        var conflicts = new List<RebindConflict>();
        if (source == null || source.Kind == SourceKind.None) return conflicts;

        var known = GetKnownContexts();
        var contexts = known;
        if (scope == ConflictScope.SameContext)
        {
            contexts = new List<BindingContext>();
            foreach (var context in known)
                foreach (var binding in context.Bindings)
                    if (binding != null && binding.RebindId == id) { contexts.Add(context); break; }
        }

        var reportedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            foreach (var binding in context.Bindings)
            {
                if (binding == null || binding.Action == null) continue;
                if (binding.IsRebindable && (binding.RebindId == id || reportedIds.Contains(binding.RebindId))) continue;
                var current = GetEffectiveSource(binding);
                if (current == null || !current.Overlaps(source)) continue;
                if (binding.IsRebindable) reportedIds.Add(binding.RebindId);
                conflicts.Add(new RebindConflict(context, binding, current));
            }
        }
        return conflicts;
    }

    /// <summary>
    /// Gives slot <paramref name="id"/> a new source (null or a <see cref="SourceKind.None"/> source clears it),
    /// handling conflicts by <paramref name="policy"/>. Assigning the slot's default source removes the
    /// override. Takes effect from the next tick.
    /// </summary>
    public RebindResult Rebind(string id, InputSource source, RebindConflictPolicy policy = RebindConflictPolicy.Allow, ConflictScope scope = ConflictScope.SameContext)
    {
        var result = new RebindResult(id) { Source = source };
        var slot = GetRebindSlot(id);
        if (slot == null)
        {
            result.FailureReason = $"no rebindable binding with id '{id}' in the player's contexts";
            RebindCompleted?.Invoke(result);
            return result;
        }

        result.PreviousSource = slot.CurrentSource;
        result.Conflicts.AddRange(FindConflicts(id, source, scope));

        if (policy == RebindConflictPolicy.Reject && result.Conflicts.Count > 0)
        {
            result.FailureReason = "the input is already used";
            RebindCompleted?.Invoke(result);
            return result;
        }

        SetSlotSource(slot, source);

        bool swapped = false;
        foreach (var conflict in result.Conflicts)
        {
            if (conflict.RebindId == null) continue;
            if (policy == RebindConflictPolicy.Swap && !swapped)
            {
                var other = GetRebindSlot(conflict.RebindId);
                if (other != null) SetSlotSource(other, result.PreviousSource);
                result.ChangedSlots.Add(conflict.RebindId);
                swapped = true;
            }
            else if (policy == RebindConflictPolicy.ClearOthers)
            {
                Profile.SetOverride(conflict.RebindId, null);
                result.ChangedSlots.Add(conflict.RebindId);
            }
        }

        result.Applied = true;
        RebindCompleted?.Invoke(result);
        return result;
    }

    /// <summary>Leaves a slot with no source.</summary>
    public RebindResult ClearRebind(string id) => Rebind(id, null);

    /// <summary>Returns a slot to its default source.</summary>
    public bool ResetRebind(string id) => Profile.RemoveOverride(id);

    /// <summary>Returns every slot to its default source.</summary>
    public void ResetAllRebinds() => Profile.Clear();

    private void SetSlotSource(RebindSlot slot, InputSource source)
    {
        bool isDefault = source == null
            ? slot.DefaultSource == null || slot.DefaultSource.Kind == SourceKind.None
            : slot.DefaultSource != null && InputSourceJson.SameInput(source, slot.DefaultSource);
        if (isDefault) Profile.RemoveOverride(slot.Id);
        else Profile.SetOverride(slot.Id, source);
    }

    // ------------------------------------------------------------------
    //  Capture
    // ------------------------------------------------------------------

    /// <summary>Whether a capture is waiting for a press.</summary>
    public bool IsCapturing => _captureId != null;

    /// <summary>The slot being captured, or null.</summary>
    public string CaptureId => _captureId;

    /// <summary>
    /// Waits for the next press this player receives and binds it to slot <paramref name="id"/>. The
    /// captured press does not reach any action. A cancel key or button ends the capture without a change.
    /// </summary>
    public bool StartCapture(string id, CaptureOptions options = null)
    {
        if (GetRebindSlot(id) == null) return false;
        if (_captureId != null) CancelCapture();
        _captureId = id;
        _captureOptions = options ?? new CaptureOptions();
        return true;
    }

    /// <summary>Ends a capture without changing anything.</summary>
    public void CancelCapture()
    {
        if (_captureId == null) return;
        var id = _captureId;
        _captureId = null;
        _captureOptions = null;
        EmitSignal(SignalName.RebindCaptureCanceled, id);
    }

    /// <summary>Offers an event to a running capture. Returns true when the capture took it.</summary>
    private bool TryCapture(InputEvent @event, IntakeStage stage)
    {
        if (_captureId == null || stage == IntakeStage.Unhandled) return false;
        var options = _captureOptions;

        bool allowedDevice = @event switch
        {
            InputEventKey => options.Keyboard,
            InputEventMouseButton => options.Mouse,
            InputEventJoypadButton or InputEventJoypadMotion => options.Joypad,
            _ => false,
        };
        if (!allowedDevice) return false;

        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && (Array.IndexOf(options.CancelKeys ?? Array.Empty<Key>(), key.PhysicalKeycode) >= 0
                || Array.IndexOf(options.CancelKeys ?? Array.Empty<Key>(), key.Keycode) >= 0)
            || @event is InputEventJoypadButton { Pressed: true } button
            && Array.IndexOf(options.CancelJoyButtons ?? Array.Empty<JoyButton>(), button.ButtonIndex) >= 0)
        {
            CancelCapture();
            return true;
        }

        var source = InputSource.FromEvent(@event, options.KeyMatch, options.AxisThreshold, options.AnyJoypad);
        if (source == null) return false;

        var id = _captureId;
        _captureId = null;
        _captureOptions = null;
        EmitSignal(SignalName.RebindCaptured, id, source);
        Rebind(id, source, options.ConflictPolicy, options.ConflictScope);
        return true;
    }
}
