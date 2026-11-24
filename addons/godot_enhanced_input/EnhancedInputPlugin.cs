#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Editor plugin: registers the <c>EnhancedInput</c> autoload while the plugin is enabled and adds the
/// Enhanced Input dock (contexts, bindings, problems, listen-to-assign, devices) to the bottom panel.
/// Only the three assets — <see cref="EnhancedInputAction"/>, <see cref="EnhancedInputMappingContext"/> and
/// <see cref="EnhancedInputConfig"/> — are offered in <i>Create New Resource</i>; the inspector plugin
/// (<see cref="EnhancedInputInspector"/>) creates everything that lives inside them.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputPlugin : EditorPlugin
{
    private const string AutoloadName = "EnhancedInput";
    private const string AutoloadPath = "res://addons/godot_enhanced_input/Runtime/EnhancedInput.cs";

    private EditorDock _dock;
    private EnhancedInputInspector _inspector;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        var content = new EnhancedInputDock();
        content.Setup(this);
        _dock = new EditorDock { Title = "Enhanced Input", DefaultSlot = EditorDock.DockSlot.Bottom };
        _dock.AddChild(content);
        AddDock(_dock);
        _inspector = new EnhancedInputInspector();
        _inspector.Setup(this);
        AddInspectorPlugin(_inspector);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_inspector != null)
        {
            RemoveInspectorPlugin(_inspector);
            _inspector = null;
        }
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
