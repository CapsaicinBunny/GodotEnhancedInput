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
    private EnhancedInputDock _content;
    private EnhancedInputInspector _inspector;

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        _content = new EnhancedInputDock();
        _content.Setup(this);
        _dock = new EditorDock { Title = "Enhanced Input", DefaultSlot = EditorDock.DockSlot.Bottom };
        _dock.AddChild(_content);
        AddDock(_dock);
        EnsureInspector();
    }

    /// <summary>
    /// Registers the inspector plugin if it is not registered yet. Also called by the dock each frame:
    /// when the editor reloads the C# assembly it keeps the plugin without entering the tree again, so
    /// parts added in a newer version of the plugin would otherwise stay unregistered until a restart.
    /// </summary>
    internal void EnsureInspector()
    {
        if (_inspector != null && IsInstanceValid(_inspector)) return;
        _inspector = new EnhancedInputInspector();
        _inspector.Setup(this);
        AddInspectorPlugin(_inspector);
    }

    /// <summary>Double-clicking an action, mapping context, config or mapping shows it in the dock.</summary>
    public override bool _Handles(GodotObject @object)
        => @object is EnhancedInputAction or EnhancedInputMappingContext or EnhancedInputConfig or ActionBinding;

    /// <inheritdoc/>
    public override void _Edit(GodotObject @object)
    {
        if (_content == null || !IsInstanceValid(_content))
            _content = _dock == null ? null : System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<EnhancedInputDock>(_dock.GetChildren()));
        if (@object == null || _content == null) return;
        if (_content.Reveal(@object)) _dock?.MakeVisible();
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
        _content = null;
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
