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
    private readonly List<EnhancedInputMappingContext> _rebindContexts = new();
    private string _captureId;
    private CaptureOptions _captureOptions;

    /// <summary>The player's rebinding choices. Never null; assigning one applies it from the next tick.</summary>
    public InputProfile Profile
    {
        get
        {
            if (_profile == null)
            {
                // Created quietly: nothing changed from the bindings' point of view.
                _profile = new InputProfile();
                _profile.Changed += OnProfileChanged;
            }
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

    private void OnProfileChanged()
    {
        _planDirty = true;
        _planChangeReason = InputEndReason.BindingChanged;
        EmitSignal(SignalName.BindingsChanged);
    }

    /// <summary>The default file for this player's profile.</summary>
    public string DefaultProfilePath => $"user://input/{ProfileId}.json";

    private string _profileId;

    /// <summary>
    /// A stable id for this player's saved profile (default <c>player_&lt;n&gt;</c>, given when the player
    /// is created and never changed by re-indexing). Set it to tie a profile to a user or save slot.
    /// </summary>
    public string ProfileId
    {
        get => string.IsNullOrEmpty(_profileId) ? $"player_{PlayerIndex}" : _profileId;
        set => _profileId = value;
    }

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
    public void RegisterRebindContext(EnhancedInputMappingContext context)
    {
        if (context != null && !_rebindContexts.Contains(context)) _rebindContexts.Add(context);
    }

    /// <summary>Stops listing a context registered with <see cref="RegisterRebindContext"/>.</summary>
    public void UnregisterRebindContext(EnhancedInputMappingContext context) => _rebindContexts.Remove(context);

    /// <summary>The active contexts (by priority) followed by the registered ones, without duplicates.</summary>
    public List<EnhancedInputMappingContext> GetKnownContexts()
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
            contexts = new List<EnhancedInputMappingContext>();
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
    /// override. All changes (including a swap or clearing other slots) reach listeners as one change.
    /// Takes effect from the next tick.
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

        Profile.Batch(() =>
        {
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
        });

        result.Applied = true;
        RebindCompleted?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Rebinds one part of a slot whose source is made of parts — one key of a four-key composite, one
    /// part of a composite source — keeping the other parts (see <see cref="InputSource.GetParts"/>).
    /// </summary>
    public RebindResult RebindPart(string id, int part, InputSource source, RebindConflictPolicy policy = RebindConflictPolicy.Allow, ConflictScope scope = ConflictScope.SameContext)
    {
        var slot = GetRebindSlot(id);
        var current = slot?.CurrentSource ?? slot?.DefaultSource;
        if (current == null || part < 0 || part >= current.GetParts().Count)
        {
            var failed = new RebindResult(id) { Source = source, FailureReason = $"slot '{id}' has no part {part}" };
            RebindCompleted?.Invoke(failed);
            return failed;
        }
        return Rebind(id, current.WithPart(part, source), policy, scope);
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
    //  Named profiles, presets
    // ------------------------------------------------------------------

    /// <summary>The folder for this player's named profiles: <c>user://input/&lt;ProfileId&gt;</c>.</summary>
    public string ProfileFolder => $"user://input/{ProfileId}";

    /// <summary>The names of this player's saved named profiles.</summary>
    public List<string> ListProfiles()
    {
        var names = new List<string>();
        var absolute = ProjectSettings.GlobalizePath(ProfileFolder);
        if (!DirAccess.DirExistsAbsolute(absolute)) return names;
        foreach (var file in DirAccess.GetFilesAt(absolute))
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) names.Add(file[..^5]);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>Saves the current profile under a name (e.g. "default", "left-handed", "couch").</summary>
    public Error SaveProfileAs(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':', '.' }) >= 0) return Error.InvalidParameter;
        Profile.Name = name;
        return Profile.Save($"{ProfileFolder}/{name}.json");
    }

    /// <summary>Loads a named profile. Returns false (keeping the current one) if it is missing or unreadable.</summary>
    public bool LoadProfileNamed(string name) => !string.IsNullOrWhiteSpace(name) && LoadProfile($"{ProfileFolder}/{name}.json");

    /// <summary>Deletes a named profile file.</summary>
    public bool DeleteProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var absolute = ProjectSettings.GlobalizePath($"{ProfileFolder}/{name}.json");
        return FileAccess.FileExists(absolute) && DirAccess.RemoveAbsolute(absolute) == Error.Ok;
    }

    /// <summary>Applies a preset file (overrides and settings) on top of the current profile, or replacing it.</summary>
    public bool ImportPreset(string path, bool replace = false)
    {
        var preset = InputProfile.Load(path);
        if (preset == null) return false;
        Profile.Apply(preset, replace);
        return true;
    }

    /// <summary>Writes the current profile to any path, to share as a preset.</summary>
    public Error ExportProfile(string path) => Profile.Save(path);

    // ------------------------------------------------------------------
    //  Capture
    // ------------------------------------------------------------------

    /// <summary>Whether a capture is waiting for a press.</summary>
    public bool IsCapturing => _captureId != null;

    /// <summary>The slot being captured, or null.</summary>
    public string CaptureId => _captureId;

    /// <summary>The part being captured (see <see cref="RebindPart"/>), or −1 for the whole slot.</summary>
    public int CapturePart => _capturePart;

    /// <summary>Whether the last capture ended because its time ran out.</summary>
    public bool LastCaptureTimedOut { get; private set; }

    private int _capturePart = -1;
    private double _captureElapsed;
    private readonly HashSet<SourceKey> _captureHeld = new();
    private readonly List<Key> _captureModifiers = new();
    private readonly List<SourceKey> _captureScratch = new();

    /// <summary>
    /// Waits for the next press this player receives and binds it to slot <paramref name="id"/> (or to one
    /// <paramref name="part"/> of it). The captured press does not reach any action. A cancel key or button
    /// ends the capture without a change; so does <see cref="CaptureOptions.TimeoutSeconds"/>. Inputs already
    /// held, and sticks not at rest, are ignored until released (with <see cref="CaptureOptions.WaitForRelease"/>).
    /// </summary>
    public bool StartCapture(string id, CaptureOptions options = null, int part = -1)
    {
        var slot = GetRebindSlot(id);
        if (slot == null) return false;
        if (part >= 0 && part >= (slot.CurrentSource ?? slot.DefaultSource)?.GetParts().Count) return false;
        if (_captureId != null) CancelCapture();
        _captureId = id;
        _capturePart = part;
        _captureOptions = options ?? new CaptureOptions();
        _captureElapsed = 0;
        _captureModifiers.Clear();
        _captureHeld.Clear();
        LastCaptureTimedOut = false;
        if (_captureOptions.WaitForRelease)
        {
            _captureScratch.Clear();
            Raw.CollectActiveChannels(_captureScratch, _captureOptions.AxisThreshold * 0.5f);
            foreach (var key in _captureScratch) _captureHeld.Add(key);
        }
        return true;
    }

    /// <summary>Ends a capture without changing anything.</summary>
    public void CancelCapture()
    {
        if (_captureId == null) return;
        var id = _captureId;
        EndCapture();
        EmitSignal(SignalName.RebindCaptureCanceled, id);
    }

    private void EndCapture()
    {
        _captureId = null;
        _captureOptions = null;
        _capturePart = -1;
        _captureModifiers.Clear();
        _captureHeld.Clear();
    }

    /// <summary>Called at the start of every tick: ends a capture whose time ran out.</summary>
    private void UpdateCapture(double delta)
    {
        if (_captureId == null || _captureOptions == null || _captureOptions.TimeoutSeconds <= 0) return;
        _captureElapsed += delta;
        if (_captureElapsed < _captureOptions.TimeoutSeconds) return;
        CancelCapture();
        LastCaptureTimedOut = true;
    }

    private static bool IsModifierKey(Key key) => key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;

    /// <summary>The raw channel an event belongs to (for "held when the capture started").</summary>
    private static SourceKey? ChannelOf(InputEvent @event) => @event switch
    {
        InputEventKey key when key.PhysicalKeycode != Key.None => SourceKey.ForKey(key.PhysicalKeycode),
        InputEventKey key => SourceKey.ForKey(key.Keycode, KeyMatch.Logical),
        InputEventMouseButton mouse => SourceKey.ForMouseButton(mouse.ButtonIndex),
        InputEventJoypadButton button => SourceKey.ForJoyButton(button.ButtonIndex, button.Device),
        InputEventJoypadMotion motion => SourceKey.ForJoyAxis(motion.Axis, motion.Device),
        _ => null,
    };

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

        // Inputs held when the capture started (the key that opened the menu, a stick resting off-centre)
        // are ignored until they are released; the release still reaches the game.
        var channel = ChannelOf(@event);
        if (channel is SourceKey held && _captureHeld.Contains(held))
        {
            bool released = @event switch
            {
                InputEventKey k => !k.Pressed,
                InputEventMouseButton m => !m.Pressed,
                InputEventJoypadButton b => !b.Pressed,
                InputEventJoypadMotion j => Mathf.Abs(j.AxisValue) < options.AxisThreshold * 0.5f,
                _ => true,
            };
            if (released) _captureHeld.Remove(held);
            return false;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && (Array.IndexOf(options.CancelKeys ?? Array.Empty<Key>(), key.PhysicalKeycode) >= 0
                || Array.IndexOf(options.CancelKeys ?? Array.Empty<Key>(), key.Keycode) >= 0)
            || @event is InputEventJoypadButton { Pressed: true } button
            && Array.IndexOf(options.CancelJoyButtons ?? Array.Empty<JoyButton>(), button.ButtonIndex) >= 0)
        {
            CancelCapture();
            return true;
        }

        // Chords: modifier keys wait for the key they go with; released alone, they bind themselves.
        if (options.AllowChords && @event is InputEventKey { Echo: false } chordKey)
        {
            var code = chordKey.PhysicalKeycode != Key.None ? chordKey.PhysicalKeycode : chordKey.Keycode;
            if (IsModifierKey(code))
            {
                if (chordKey.Pressed)
                {
                    if (!_captureModifiers.Contains(code)) _captureModifiers.Add(code);
                    return true;
                }
                if (_captureModifiers.Contains(code))
                    return Finish(InputSource.ForKey(code, options.KeyMatch));
                return false;
            }
            if (chordKey.Pressed && _captureModifiers.Count > 0)
            {
                var chord = InputSource.FromEvent(@event, options.KeyMatch);
                if (chord == null) return false;
                var parts = new InputSource[_captureModifiers.Count + 1];
                for (int i = 0; i < _captureModifiers.Count; i++) parts[i] = InputSource.ForKey(_captureModifiers[i], options.KeyMatch);
                parts[^1] = chord;
                return Finish(InputSource.Composite(CompositeMode.WithModifiers, parts));
            }
        }

        var source = InputSource.FromEvent(@event, options.KeyMatch, options.AxisThreshold, options.AnyJoypad);
        if (source == null) return false;
        if (IsExcluded(source, options) || options.Filter != null && !options.Filter(source)) return true;
        return Finish(source);
    }

    private static bool IsExcluded(InputSource source, CaptureOptions options)
    {
        if (source.Kind == SourceKind.Key && options.ExcludedKeys != null && Array.IndexOf(options.ExcludedKeys, source.Key) >= 0) return true;
        if (source.Kind == SourceKind.JoyButton && options.ExcludedJoyButtons != null && Array.IndexOf(options.ExcludedJoyButtons, source.JoyButton) >= 0) return true;
        return false;
    }

    private bool Finish(InputSource source)
    {
        var id = _captureId;
        var options = _captureOptions;
        int part = _capturePart;
        EndCapture();
        EmitSignal(SignalName.RebindCaptured, id, source);
        if (part >= 0) RebindPart(id, part, source, options.ConflictPolicy, options.ConflictScope);
        else Rebind(id, source, options.ConflictPolicy, options.ConflictScope);
        return true;
    }
}
