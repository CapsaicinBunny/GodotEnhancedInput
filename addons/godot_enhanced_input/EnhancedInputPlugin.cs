#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Editor plugin: registers the <c>EnhancedInput</c> autoload while the plugin is enabled and adds the
/// Enhanced Input dock (contexts, bindings, problems, listen-to-assign, devices) to the bottom panel.
/// The resource types register themselves through <c>[GlobalClass]</c>.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputPlugin : EditorPlugin
{
    private const string AutoloadName = "EnhancedInput";
    private const string AutoloadPath = "res://addons/godot_enhanced_input/Runtime/EnhancedInput.cs";

    private EditorDock _dock;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        var content = new EnhancedInputDock();
        content.Setup(this);
        _dock = new EditorDock { Title = "Enhanced Input", DefaultSlot = EditorDock.DockSlot.Bottom };
        _dock.AddChild(content);
        AddDock(_dock);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_dock == null) return;
        RemoveDock(_dock);
        _dock.QueueFree();
        _dock = null;
    }

    /// <inheritdoc/>
    public override void _EnablePlugin()
    {
        if (!ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            AddAutoloadSingleton(AutoloadName, AutoloadPath);
    }

    /// <inheritdoc/>
    public override void _DisablePlugin()
    {
        if (ProjectSettings.HasSetting($"autoload/{AutoloadName}"))
            RemoveAutoloadSingleton(AutoloadName);
    }
}
#endif
