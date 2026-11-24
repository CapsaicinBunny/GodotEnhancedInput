using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Which controller a joypad is, as far as its USB ids and name tell.</summary>
public enum ControllerFamily
{
    /// <summary>Not identified yet (the pad has not been read).</summary>
    Unknown,
    /// <summary>A pad that reports no recognised make (generic, arcade stick, older pads).</summary>
    Generic,
    /// <summary>Xbox 360 controller.</summary>
    Xbox360,
    /// <summary>Xbox One controller (including the One S over Bluetooth).</summary>
    XboxOne,
    /// <summary>Xbox Series X|S controller.</summary>
    XboxSeries,
    /// <summary>Xbox Elite controller (back paddles).</summary>
    XboxElite,
    /// <summary>DualShock 3.</summary>
    PlayStation3,
    /// <summary>DualShock 4.</summary>
    PlayStation4,
    /// <summary>DualSense.</summary>
    PlayStation5,
    /// <summary>DualSense Edge (back buttons).</summary>
    PlayStation5Edge,
    /// <summary>Nintendo Switch Pro Controller.</summary>
    SwitchPro,
    /// <summary>A single left Joy-Con.</summary>
    JoyConLeft,
    /// <summary>A single right Joy-Con.</summary>
    JoyConRight,
    /// <summary>Two Joy-Cons used as one pad.</summary>
    JoyConPair,
    /// <summary>Other Nintendo pads (GameCube adapters, NSO pads…).</summary>
    NintendoOther,
    /// <summary>The Steam Deck's built-in controls.</summary>
    SteamDeck,
    /// <summary>Valve's Steam Controller.</summary>
    SteamController,
    /// <summary>Steam Input's virtual pad: the real controller is hidden behind Steam's mapping.</summary>
    SteamVirtual,
    /// <summary>A pad Godot only sees through the browser's gamepad API.</summary>
    WebStandard,
}

/// <summary>What a controller has (in hardware) or what Godot can reach on it.</summary>
[Flags]
public enum ControllerFeatures
{
    /// <summary>No features.</summary>
    None = 0,
    /// <summary>Weak and strong rumble motors.</summary>
    Rumble = 1 << 0,
    /// <summary>Motors in the triggers (Xbox One and later). Godot's joypad API cannot drive them.</summary>
    TriggerRumble = 1 << 1,
    /// <summary>Triggers with programmable resistance (DualSense). Godot's joypad API cannot drive them.</summary>
    AdaptiveTriggers = 1 << 2,
    /// <summary>A light bar or player light that can be coloured.</summary>
    Light = 1 << 3,
    /// <summary>Gyroscope and accelerometer.</summary>
    MotionSensors = 1 << 4,
    /// <summary>A touchpad (Godot reports its click as a button; finger positions are not exposed).</summary>
    Touchpad = 1 << 5,
    /// <summary>Back paddles or grip buttons (Paddle1–4).</summary>
    BackButtons = 1 << 6,
    /// <summary>Trackpads (Steam Deck, Steam Controller), reachable only through Steam Input.</summary>
    Trackpads = 1 << 7,
    /// <summary>A built-in microphone.</summary>
    Microphone = 1 << 8,
    /// <summary>A share / capture / quick-access button (Misc1).</summary>
    ShareButton = 1 << 9,
    /// <summary>HD rumble (linear actuators) rather than plain motors.</summary>
    HDHaptics = 1 << 10,
}

/// <summary>
/// Recognises controllers from Godot's joypad info (USB vendor and product ids, raw and mapped names)
/// and knows what each family has and how its buttons are labelled.
/// </summary>
public static class ControllerDetection
{
    /// <summary>USB vendor id of Sony.</summary>
    public const int VendorSony = 0x054C;
    /// <summary>USB vendor id of Microsoft.</summary>
    public const int VendorMicrosoft = 0x045E;
    /// <summary>USB vendor id of Nintendo.</summary>
    public const int VendorNintendo = 0x057E;
    /// <summary>USB vendor id of Valve.</summary>
    public const int VendorValve = 0x28DE;

    /// <summary>Reads a connected joypad's USB vendor and product ids (0 when the platform does not say).</summary>
    public static (int Vendor, int Product) ReadIds(int device)
    {
        if (device < 0) return (0, 0);
        var info = Input.GetJoyInfo(device);
        int vendor = info.TryGetValue("vendor_id", out var v) ? ToInt(v) : 0;
        int product = info.TryGetValue("product_id", out var p) ? ToInt(p) : 0;
        return (vendor, product);
    }

    private static int ToInt(Variant v) => v.VariantType switch
    {
        Variant.Type.Int => (int)v.AsInt64(),
        Variant.Type.Float => (int)v.AsDouble(),
        Variant.Type.String => int.TryParse(v.AsString(), out var i) ? i : 0,
        _ => 0,
    };

    /// <summary>The family of a connected joypad.</summary>
    public static ControllerFamily Detect(int device)
    {
        if (device < 0) return ControllerFamily.Unknown;
        var (vendor, product) = ReadIds(device);
        var info = Input.GetJoyInfo(device);
        string raw = info.TryGetValue("raw_name", out var r) ? r.AsString() : string.Empty;
        return Detect(Input.GetJoyName(device) + " " + raw, vendor, product);
    }

    /// <summary>The family from a name and USB ids (ids win when known).</summary>
    public static ControllerFamily Detect(string name, int vendor = 0, int product = 0)
    {
        var known = FromIds(vendor, product);
        if (known != ControllerFamily.Unknown) return known;
        return vendor switch
        {
            VendorValve => FromName(name, ControllerFamily.SteamController),
            VendorSony => FromName(name, ControllerFamily.PlayStation4),
            VendorMicrosoft => FromName(name, ControllerFamily.XboxOne),
            VendorNintendo => FromName(name, ControllerFamily.NintendoOther),
            _ => FromName(name, ControllerFamily.Generic),
        };
    }

    /// <summary>
    /// The family of a pad from its USB vendor and product ids alone, or <see cref="ControllerFamily.Unknown"/>
    /// when the ids are not in the table. Cable and Bluetooth variants of one pad map to the same family.
    /// </summary>
    public static ControllerFamily FromIds(int vendor, int product) => vendor switch
    {
        VendorValve => product switch
        {
            0x1205 => ControllerFamily.SteamDeck,
            0x1102 or 0x1142 => ControllerFamily.SteamController,
            0x11FF => ControllerFamily.SteamVirtual,
            _ => ControllerFamily.Unknown,
        },
        VendorSony => product switch
        {
            0x0268 => ControllerFamily.PlayStation3,
            0x05C4 or 0x09CC or 0x0BA0 => ControllerFamily.PlayStation4,
            0x0CE6 => ControllerFamily.PlayStation5,
            0x0DF2 => ControllerFamily.PlayStation5Edge,
            _ => ControllerFamily.Unknown,
        },
        VendorMicrosoft => product switch
        {
            0x028E or 0x028F or 0x0719 => ControllerFamily.Xbox360,
            0x02E3 or 0x0B00 or 0x0B05 or 0x0B22 => ControllerFamily.XboxElite,
            0x0B12 or 0x0B13 => ControllerFamily.XboxSeries,
            0x02D1 or 0x02DD or 0x02EA or 0x02E0 or 0x02FD => ControllerFamily.XboxOne,
            _ => ControllerFamily.Unknown,
        },
        VendorNintendo => product switch
        {
            0x2009 => ControllerFamily.SwitchPro,
            0x2006 => ControllerFamily.JoyConLeft,
            0x2007 => ControllerFamily.JoyConRight,
            0x2008 => ControllerFamily.JoyConPair,
            _ => ControllerFamily.Unknown,
        },
        _ => ControllerFamily.Unknown,
    };

    /// <summary>
    /// Reads the connection bus and USB vendor and product ids out of an SDL-style joypad GUID (32 hex
    /// digits: bus, name checksum, vendor, 0, product, 0, version, driver). False when the GUID does
    /// not carry ids (other formats, or devices that report none).
    /// </summary>
    public static bool TryParseGuid(string guid, out int bus, out int vendor, out int product)
    {
        bus = vendor = product = 0;
        if (guid == null || guid.Length != 32) return false;
        var b = new byte[16];
        for (int i = 0; i < 16; i++)
            if (!byte.TryParse(guid.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out b[i])) return false;
        if (b[6] != 0 || b[7] != 0 || b[10] != 0 || b[11] != 0) return false;
        bus = b[0] | b[1] << 8;
        vendor = b[4] | b[5] << 8;
        product = b[8] | b[9] << 8;
        return vendor != 0;
    }

    /// <summary>
    /// What kind of pad this is, ignoring how it is connected: "vendor/family" for pads in the table
    /// (so a DualSense or an Xbox Series pad is the same model on a cable and over Bluetooth), else
    /// "vendor:product", else the GUID itself. Pads of one model without serial numbers cannot be told apart.
    /// </summary>
    public static string ModelKey(string guid, int vendor, int product)
    {
        if (vendor == 0 && !TryParseGuid(guid, out _, out vendor, out product)) return guid ?? string.Empty;
        var family = FromIds(vendor, product);
        return family != ControllerFamily.Unknown ? $"{vendor:x4}/{family}" : $"{vendor:x4}:{product:x4}";
    }

    private static ControllerFamily FromName(string name, ControllerFamily fallback)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("steam deck")) return ControllerFamily.SteamDeck;
        if (n.Contains("steam virtual") || n.Contains("steam input")) return ControllerFamily.SteamVirtual;
        if (n.Contains("steam controller") || n.Contains("valve")) return ControllerFamily.SteamController;
        if (n.Contains("dualsense edge")) return ControllerFamily.PlayStation5Edge;
        if (n.Contains("dualsense") || n.Contains("ps5")) return ControllerFamily.PlayStation5;
        if (n.Contains("dualshock 4") || n.Contains("ps4")) return ControllerFamily.PlayStation4;
        if (n.Contains("ps3") || n.Contains("dualshock 3") || n.Contains("sixaxis")) return ControllerFamily.PlayStation3;
        if (n.Contains("playstation") || n.Contains("dualshock")) return ControllerFamily.PlayStation4;
        if (n.Contains("elite")) return ControllerFamily.XboxElite;
        if (n.Contains("xbox series")) return ControllerFamily.XboxSeries;
        if (n.Contains("xbox 360") || n.Contains("x360")) return ControllerFamily.Xbox360;
        if (n.Contains("xbox") || n.Contains("xinput")) return ControllerFamily.XboxOne;
        if (n.Contains("pro controller")) return ControllerFamily.SwitchPro;
        if (n.Contains("joy-con (l)") || n.Contains("joycon l") || n.Contains("joy-con l")) return ControllerFamily.JoyConLeft;
        if (n.Contains("joy-con (r)") || n.Contains("joycon r") || n.Contains("joy-con r")) return ControllerFamily.JoyConRight;
        if (n.Contains("joy-con") || n.Contains("joycon")) return ControllerFamily.JoyConPair;
        if (n.Contains("nintendo") || n.Contains("switch")) return ControllerFamily.NintendoOther;
        if (n.Contains("standard gamepad")) return ControllerFamily.WebStandard;
        return fallback;
    }

    /// <summary>What a family has in hardware (whether or not Godot can reach it).</summary>
    public static ControllerFeatures HardwareFeatures(ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox360 => ControllerFeatures.Rumble,
        ControllerFamily.XboxOne => ControllerFeatures.Rumble | ControllerFeatures.TriggerRumble,
        ControllerFamily.XboxSeries => ControllerFeatures.Rumble | ControllerFeatures.TriggerRumble | ControllerFeatures.ShareButton,
        ControllerFamily.XboxElite => ControllerFeatures.Rumble | ControllerFeatures.TriggerRumble | ControllerFeatures.BackButtons,
        ControllerFamily.PlayStation3 => ControllerFeatures.Rumble | ControllerFeatures.MotionSensors,
        ControllerFamily.PlayStation4 => ControllerFeatures.Rumble | ControllerFeatures.Light | ControllerFeatures.MotionSensors | ControllerFeatures.Touchpad,
        ControllerFamily.PlayStation5 => ControllerFeatures.Rumble | ControllerFeatures.HDHaptics | ControllerFeatures.AdaptiveTriggers | ControllerFeatures.Light
            | ControllerFeatures.MotionSensors | ControllerFeatures.Touchpad | ControllerFeatures.Microphone,
        ControllerFamily.PlayStation5Edge => HardwareFeatures(ControllerFamily.PlayStation5) | ControllerFeatures.BackButtons,
        ControllerFamily.SwitchPro => ControllerFeatures.Rumble | ControllerFeatures.HDHaptics | ControllerFeatures.MotionSensors | ControllerFeatures.ShareButton,
        ControllerFamily.JoyConLeft or ControllerFamily.JoyConRight or ControllerFamily.JoyConPair
            => ControllerFeatures.Rumble | ControllerFeatures.HDHaptics | ControllerFeatures.MotionSensors,
        ControllerFamily.SteamDeck => ControllerFeatures.Rumble | ControllerFeatures.HDHaptics | ControllerFeatures.MotionSensors | ControllerFeatures.BackButtons
            | ControllerFeatures.Trackpads | ControllerFeatures.ShareButton,
        ControllerFamily.SteamController => ControllerFeatures.Rumble | ControllerFeatures.HDHaptics | ControllerFeatures.MotionSensors | ControllerFeatures.BackButtons
            | ControllerFeatures.Trackpads,
        ControllerFamily.SteamVirtual => ControllerFeatures.Rumble,
        _ => ControllerFeatures.None,
    };

    /// <summary>What Godot can reach on a connected joypad (rumble, light, motion sensors, and the family's extra buttons).</summary>
    public static ControllerFeatures AvailableFeatures(int device, ControllerFamily family)
    {
        var features = ControllerFeatures.None;
        if (device < 0) return features;
        if (Input.HasJoyVibration(device)) features |= ControllerFeatures.Rumble;
        if (Input.HasJoyLight(device)) features |= ControllerFeatures.Light;
        if (Input.HasJoyMotionSensors(device)) features |= ControllerFeatures.MotionSensors;
        var hardware = HardwareFeatures(family);
        // Buttons Godot reports through the standard mapping.
        features |= hardware & (ControllerFeatures.Touchpad | ControllerFeatures.BackButtons | ControllerFeatures.ShareButton);
        return features;
    }

    /// <summary>The prompt style that fits a family.</summary>
    public static JoypadStyle StyleOf(ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox360 or ControllerFamily.XboxOne or ControllerFamily.XboxSeries or ControllerFamily.XboxElite => JoypadStyle.Xbox,
        ControllerFamily.PlayStation3 or ControllerFamily.PlayStation4 or ControllerFamily.PlayStation5 or ControllerFamily.PlayStation5Edge => JoypadStyle.PlayStation,
        ControllerFamily.SwitchPro or ControllerFamily.JoyConLeft or ControllerFamily.JoyConRight or ControllerFamily.JoyConPair or ControllerFamily.NintendoOther => JoypadStyle.Nintendo,
        ControllerFamily.SteamDeck => JoypadStyle.SteamDeck,
        ControllerFamily.SteamController => JoypadStyle.Xbox,
        ControllerFamily.SteamVirtual => JoypadStyle.Xbox,
        _ => JoypadStyle.Generic,
    };

    /// <summary>A short display name for a family ("DualSense", "Steam Deck"…).</summary>
    public static string DisplayName(ControllerFamily family) => family switch
    {
        ControllerFamily.Xbox360 => "Xbox 360 Controller",
        ControllerFamily.XboxOne => "Xbox Controller",
        ControllerFamily.XboxSeries => "Xbox Series Controller",
        ControllerFamily.XboxElite => "Xbox Elite Controller",
        ControllerFamily.PlayStation3 => "DualShock 3",
        ControllerFamily.PlayStation4 => "DualShock 4",
        ControllerFamily.PlayStation5 => "DualSense",
        ControllerFamily.PlayStation5Edge => "DualSense Edge",
        ControllerFamily.SwitchPro => "Switch Pro Controller",
        ControllerFamily.JoyConLeft => "Joy-Con (L)",
        ControllerFamily.JoyConRight => "Joy-Con (R)",
        ControllerFamily.JoyConPair => "Joy-Con pair",
        ControllerFamily.NintendoOther => "Nintendo controller",
        ControllerFamily.SteamDeck => "Steam Deck",
        ControllerFamily.SteamController => "Steam Controller",
        ControllerFamily.SteamVirtual => "Steam Input controller",
        ControllerFamily.WebStandard => "Gamepad (browser)",
        _ => "Gamepad",
    };
}
