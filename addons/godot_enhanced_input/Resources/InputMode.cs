using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// A way of playing — "Keyboard &amp; mouse", "Gamepad", "Touch", "Wheel &amp; pedals", "Flight stick &amp;
/// throttle" — described by the device kinds it needs and may use, the binding groups it turns on,
/// and default settings (sensitivities, deadzones) for it. A player has a list of modes and one
/// active mode; it switches automatically to the mode that fits the device the player last used,
/// unless the mode is locked.
/// </summary>
[Tool]
[GlobalClass]
public partial class InputMode : Resource
{
    /// <summary>Name for menus and logs.</summary>
    [Export] public string Name { get; set; } = string.Empty;

    /// <summary>Device kinds the mode cannot work without (it is only chosen automatically when they are available).</summary>
    [Export] public DeviceKinds RequiredDevices { get; set; }

    /// <summary>
    /// Device roles that must each be present among the devices the player can use — e.g. Wheel and
    /// Pedals as two separate devices, or FlightStick and Throttle.
    /// </summary>
    [Export] public DeviceRoles RequiredRoles { get; set; }

    /// <summary>Capabilities at least one usable device must offer (e.g. motion sensors for gyro aiming).</summary>
    [Export] public ControllerFeatures RequiredFeatures { get; set; }

    /// <summary>Device kinds the mode can also use (motion sensors, a pen…).</summary>
    [Export] public DeviceKinds OptionalDevices { get; set; }

    /// <summary>
    /// Binding groups active in this mode. A binding with groups is evaluated only when one of them is
    /// active; a binding without groups is always evaluated.
    /// </summary>
    [Export] public string[] Groups { get; set; } = System.Array.Empty<string>();

    /// <summary>Using one of these device kinds switches an unlocked player to this mode.</summary>
    [Export] public DeviceKinds SwitchOn { get; set; }

    /// <summary>Default values of settings while this mode is active (a profile setting still wins).</summary>
    [Export] public Dictionary<string, double> SettingDefaults { get; set; } = new();

    /// <summary>Whether a binding group is active in this mode.</summary>
    public bool HasGroup(string group)
    {
        foreach (var g in Groups)
            if (string.Equals(g, group, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Whether a binding with these groups is evaluated in this mode.</summary>
    public bool Allows(string[] bindingGroups)
    {
        if (bindingGroups == null || bindingGroups.Length == 0) return true;
        foreach (var g in bindingGroups)
            if (HasGroup(g)) return true;
        return false;
    }

    /// <summary>Creates a mode in code.</summary>
    public static InputMode Create(string name, DeviceKinds required, string[] groups, DeviceKinds switchOn = DeviceKinds.None)
        => new() { Name = name, RequiredDevices = required, Groups = groups ?? System.Array.Empty<string>(), SwitchOn = switchOn };

    /// <inheritdoc/>
    public override string ToString() => string.IsNullOrEmpty(Name) ? "InputMode" : Name;
}
