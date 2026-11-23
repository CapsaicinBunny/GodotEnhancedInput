#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Editor plugin: registers the <c>EnhancedInput</c> autoload while the plugin is enabled.
/// The resource types register themselves through <c>[GlobalClass]</c>.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputPlugin : EditorPlugin
{
    private const string AutoloadName = "EnhancedInput";
    private const string AutoloadPath = "res://addons/godot_enhanced_input/Runtime/EnhancedInput.cs";

    public override void _EnablePlugin()
    {
        if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            AddAutoloadSingleton(AutoloadName, AutoloadPath);
    }

    public override void _DisablePlugin()
    {
        if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            RemoveAutoloadSingleton(AutoloadName);
    }
}
#endif
