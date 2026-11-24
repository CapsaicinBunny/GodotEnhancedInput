using System;
using Godot;
using Godot.Collections;

namespace GodotEnhancedInput;

/// <summary>
/// Project-wide settings (Project Settings → Enhanced Input), applied by the <see cref="EnhancedInput"/>
/// autoload when it starts: the config to load, ticking, GUI policy, joining, focus loss and the InputMap
/// output bridge. The plugin adds them while it is enabled; settings left at their defaults are not
/// written to <c>project.godot</c>. Properties set on the autoload node in code still win afterwards.
/// </summary>
public static class EnhancedInputSettings
{
    /// <summary>The <see cref="EnhancedInputConfig"/> every player loads (empty: none).</summary>
    public const string Config = "enhanced_input/general/config";

    /// <summary>When players are ticked (<see cref="TickMode"/>).</summary>
    public const string Ticking = "enhanced_input/general/tick_mode";

    /// <summary>How event intake shares input with the GUI (<see cref="GuiPolicy"/>).</summary>
    public const string Gui = "enhanced_input/general/gui_policy";

    /// <summary>Whether losing focus or being suspended releases all held input.</summary>
    public const string ResetOnFocusLoss = "enhanced_input/general/reset_input_on_focus_loss";

    /// <summary>How unowned joypads become a player's (<see cref="JoinMode"/>).</summary>
    public const string Joining = "enhanced_input/players/join_mode";

    /// <summary>Most players joining may create.</summary>
    public const string MaxPlayers = "enhanced_input/players/max_players";

    /// <summary>Whether actions with <see cref="EnhancedInputAction.MirrorToInputMap"/> press their Godot action.</summary>
    public const string MirrorActions = "enhanced_input/input_map/mirror_actions";

    private static (string Name, Variant Default, Variant.Type Type, PropertyHint Hint, string HintString)[] All => new[]
    {
        (Config, Variant.From(string.Empty), Variant.Type.String, PropertyHint.File, "*.tres,*.res"),
        (Ticking, Variant.From((int)TickMode.Auto), Variant.Type.Int, PropertyHint.Enum, string.Join(",", Enum.GetNames<TickMode>())),
        (Gui, Variant.From((int)GuiPolicy.RespectGui), Variant.Type.Int, PropertyHint.Enum, string.Join(",", Enum.GetNames<GuiPolicy>())),
        (ResetOnFocusLoss, Variant.From(true), Variant.Type.Bool, PropertyHint.None, string.Empty),
        (Joining, Variant.From((int)JoinMode.Manual), Variant.Type.Int, PropertyHint.Enum, string.Join(",", Enum.GetNames<JoinMode>())),
        (MaxPlayers, Variant.From(4), Variant.Type.Int, PropertyHint.Range, "1,16,1"),
        (MirrorActions, Variant.From(true), Variant.Type.Bool, PropertyHint.None, string.Empty),
    };

    /// <summary>Adds the settings to Project Settings with their defaults and editors (the plugin does this).</summary>
    public static void Register()
    {
        foreach (var (name, value, type, hint, hintString) in All)
        {
            if (!ProjectSettings.HasSetting(name)) ProjectSettings.SetSetting(name, value);
            ProjectSettings.SetInitialValue(name, value);
            ProjectSettings.AddPropertyInfo(new Dictionary
            {
                ["name"] = name,
                ["type"] = (int)type,
                ["hint"] = (int)hint,
                ["hint_string"] = hintString,
            });
            ProjectSettings.SetAsBasic(name, true);
        }
    }

    /// <summary>Applies the project's settings to the system (the autoload does this when it enters the tree).</summary>
    public static void ApplyTo(EnhancedInput system)
    {
        if (system == null) return;
        if (Has(Ticking)) system.TickMode = (TickMode)ProjectSettings.GetSetting(Ticking).AsInt32();
        if (Has(Gui)) system.GuiPolicy = (GuiPolicy)ProjectSettings.GetSetting(Gui).AsInt32();
        if (Has(ResetOnFocusLoss)) system.ResetInputOnFocusLoss = ProjectSettings.GetSetting(ResetOnFocusLoss).AsBool();
        if (Has(Joining)) system.JoinMode = (JoinMode)ProjectSettings.GetSetting(Joining).AsInt32();
        if (Has(MaxPlayers)) system.MaxPlayers = ProjectSettings.GetSetting(MaxPlayers).AsInt32();
        if (Has(MirrorActions)) InputMapBridge.Enabled = ProjectSettings.GetSetting(MirrorActions).AsBool();
        if (system.Configuration == null && Has(Config))
        {
            var path = ProjectSettings.GetSetting(Config).AsString();
            if (!string.IsNullOrEmpty(path))
            {
                var config = ResourceLoader.Load<EnhancedInputConfig>(path);
                if (config != null) system.Configuration = config;
                else GD.PushError($"Enhanced Input: the config in {Config} ({path}) could not be loaded.");
            }
        }
    }

    private static bool Has(string name) => ProjectSettings.HasSetting(name);
}
