using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

public partial class InputPlayer
{
    /// <summary>Raised when the active mode changes.</summary>
    [Signal] public delegate void ModeChangedEventHandler(InputMode mode);

    /// <summary>The modes this player can be in (first one is the starting mode when none is set).</summary>
    [Export] public Array<InputMode> Modes { get; set; } = new();

    private InputMode _activeMode;

    /// <summary>
    /// The active mode, or null (then every binding is evaluated regardless of its groups). Setting it
    /// directly does not lock it; see <see cref="LockMode"/>.
    /// </summary>
    public InputMode ActiveMode
    {
        get => _activeMode;
        set
        {
            if (ReferenceEquals(_activeMode, value)) return;
            _activeMode = value;
            _planDirty = true;
            EmitSignal(SignalName.ModeChanged, value);
        }
    }

    /// <summary>When on (default), using a device switches to the first unlocked mode that lists it in <see cref="InputMode.SwitchOn"/>.</summary>
    [Export] public bool AutoSwitchModes { get; set; } = true;

    /// <summary>Whether the mode is locked (the player chose it; devices do not switch it).</summary>
    public bool IsModeLocked { get; private set; }

    /// <summary>Activates a mode and keeps it until <see cref="UnlockMode"/>.</summary>
    public void LockMode(InputMode mode)
    {
        ActiveMode = mode;
        IsModeLocked = true;
    }

    /// <summary>Lets devices switch the mode again, starting with the device used last.</summary>
    public void UnlockMode()
    {
        IsModeLocked = false;
        SwitchModeFor(LastDeviceKind);
    }

    /// <summary>Whether a binding is evaluated in the active mode.</summary>
    public bool IsBindingActive(ActionBinding binding) => _activeMode == null || _activeMode.Allows(binding?.Groups);

    private void SwitchModeFor(InputDeviceKind kind)
    {
        if (!AutoSwitchModes || IsModeLocked || Modes.Count == 0) return;
        foreach (var mode in Modes)
        {
            if (mode == null || !mode.SwitchOn.Has(kind)) continue;
            if (!RequiredDevicesAvailable(mode)) continue;
            ActiveMode = mode;
            return;
        }
    }

    /// <summary>Whether the device kinds a mode requires are available to this player now.</summary>
    public bool RequiredDevicesAvailable(InputMode mode)
    {
        if (mode == null) return false;
        foreach (var kind in new[] { InputDeviceKind.KeyboardMouse, InputDeviceKind.Joypad, InputDeviceKind.Touch, InputDeviceKind.Pen, InputDeviceKind.Custom })
        {
            if (!mode.RequiredDevices.Has(kind)) continue;
            bool ok = kind switch
            {
                InputDeviceKind.KeyboardMouse or InputDeviceKind.Pen => AcceptKeyboardMouse,
                InputDeviceKind.Touch => AcceptTouch,
                InputDeviceKind.Joypad => JoypadDevices.Count > 0 || LastJoypad >= 0 || Input.GetConnectedJoypads().Count > 0,
                InputDeviceKind.Custom => OwnedCustomDevices.Count > 0 || ReceivesUnownedJoypads,
                _ => true,
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// A setting's value for this player: the profile's value if set, else the active mode's default,
    /// else <paramref name="fallback"/>. Modifiers and triggers read sensitivities, deadzones and timing
    /// scales through this.
    /// </summary>
    public double GetSetting(string name, double fallback = 0)
    {
        if (string.IsNullOrEmpty(name)) return fallback;
        if (Profile.HasSetting(name)) return Profile.GetSetting(name);
        if (_activeMode != null && _activeMode.SettingDefaults.TryGetValue(name, out var value)) return value;
        return fallback;
    }

    /// <summary>A yes/no setting (see <see cref="GetSetting"/>).</summary>
    public bool GetFlag(string name, bool fallback = false) => GetSetting(name, fallback ? 1 : 0) != 0;

    // ------------------------------------------------------------------
    //  Hold-to-toggle (accessibility)
    // ------------------------------------------------------------------

    /// <summary>The setting that turns an action from "hold" into "press to toggle": "toggle:&lt;action name&gt;".</summary>
    public static string ToggleSettingName(GameAction action) => "toggle:" + action?.Name;

    /// <summary>Makes an action toggle on each press instead of being held (stored in the profile).</summary>
    public void SetHoldToToggle(GameAction action, bool toggle) => Profile.SetFlag(ToggleSettingName(action), toggle);

    /// <summary>Whether an action toggles instead of being held.</summary>
    public bool IsHoldToToggle(GameAction action)
    {
        if (action == null) return false;
        // Cached per action name: this runs for every action on every tick.
        var name = action.Name ?? string.Empty;
        if (!_toggleNames.TryGetValue(action, out var cached) || !ReferenceEquals(cached.Name, name))
            _toggleNames[action] = cached = (name, "toggle:" + name);
        return GetFlag(cached.Setting);
    }

    private readonly System.Collections.Generic.Dictionary<GameAction, (string Name, string Setting)> _toggleNames = new();
}

/// <summary>Ready-made accessibility and comfort settings to apply to a profile (<see cref="InputProfile.Apply"/>).</summary>
public static class AccessibilityPresets
{
    /// <summary>Taps twice as forgiving, holds half as long, repeats slower.</summary>
    public static InputProfile RelaxedTiming()
    {
        var p = new InputProfile { Name = "relaxed timing" };
        p.SetSetting(InputTrigger.TapTimeScaleSetting, 2.0);
        p.SetSetting(InputTrigger.HoldTimeScaleSetting, 0.5);
        p.SetSetting(InputTrigger.RepeatTimeScaleSetting, 1.5);
        return p;
    }

    /// <summary>No vibration at all.</summary>
    public static InputProfile NoVibration()
    {
        var p = new InputProfile { Name = "no vibration" };
        p.SetSetting("vibration_strength", 0);
        return p;
    }

    /// <summary>These actions toggle on each press instead of being held.</summary>
    public static InputProfile HoldToToggle(params GameAction[] actions)
    {
        var p = new InputProfile { Name = "hold to toggle" };
        foreach (var action in actions) p.SetFlag(InputPlayer.ToggleSettingName(action), true);
        return p;
    }
}
