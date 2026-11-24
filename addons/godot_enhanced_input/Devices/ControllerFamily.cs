using System;
using Godot;

namespace GodotEnhancedInput;

/// <summary>Which controller a joypad is, as far as its USB ids and name tell.</summary>
public enum ControllerFamily
{
    Unknown,
    /// <summary>A pad that reports no recognised make (generic, arcade stick, older pads).</summary>
    Generic,
    Xbox360,
    XboxOne,
    XboxSeries,
    XboxElite,
    PlayStation3,
    PlayStation4,
    PlayStation5,
    PlayStation5Edge,
    SwitchPro,
    JoyConLeft,
    JoyConRight,
    JoyConPair,
    /// <summary>Other Nintendo pads (GameCube adapters, NSO pads…).</summary>
    NintendoOther,
    SteamDeck,
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
    None = 0,
    Rumble = 1 << 0,
    /// <summary>Motors in the triggers (Xbox One and later). Godot's joypad API cannot drive them.</summary>
    TriggerRumble = 1 << 1,
    /// <summary>Triggers with programmable resistance (DualSense). Godot's joypad API cannot drive them.</summary>
    AdaptiveTriggers = 1 << 2,
    Light = 1 << 3,
    MotionSensors = 1 << 4,
    /// <summary>A touchpad (Godot reports its click as a button; finger positions are not exposed).</summary>
    Touchpad = 1 << 5,
    /// <summary>Back paddles or grip buttons (Paddle1–4).</summary>
    BackButtons = 1 << 6,
    /// <summary>Trackpads (Steam Deck, Steam Controller), reachable only through Steam Input.</summary>
    Trackpads = 1 << 7,
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
    public const int VendorSony = 0x054C;
    public const int VendorMicrosoft = 0x045E;
    public const int VendorNintendo = 0x057E;
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
        switch (vendor)
        {
            case VendorValve:
                return product switch
                {
                    0x1205 => ControllerFamily.SteamDeck,
                    0x1102 or 0x1142 => ControllerFamily.SteamController,
                    0x11FF => ControllerFamily.SteamVirtual,
                    _ => FromName(name, ControllerFamily.SteamController),
                };
            case VendorSony:
                return product switch
                {
                    0x0268 => ControllerFamily.PlayStation3,
                    0x05C4 or 0x09CC or 0x0BA0 => ControllerFamily.PlayStation4,
                    0x0CE6 => ControllerFamily.PlayStation5,
                    0x0DF2 => ControllerFamily.PlayStation5Edge,
                    _ => FromName(name, ControllerFamily.PlayStation4),
                };
            case VendorMicrosoft:
                return product switch
                {
                    0x028E or 0x028F or 0x0719 => ControllerFamily.Xbox360,
                    0x02E3 or 0x0B00 or 0x0B05 or 0x0B22 => ControllerFamily.XboxElite,
                    0x0B12 or 0x0B13 => ControllerFamily.XboxSeries,
                    _ => FromName(name, ControllerFamily.XboxOne),
                };
            case VendorNintendo:
                return product switch
                {
                    0x2009 => ControllerFamily.SwitchPro,
                    0x2006 => ControllerFamily.JoyConLeft,
                    0x2007 => ControllerFamily.JoyConRight,
                    0x2008 => ControllerFamily.JoyConPair,
                    _ => FromName(name, ControllerFamily.NintendoOther),
                };
        }
        return FromName(name, ControllerFamily.Generic);
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
