#if TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Inspector help for the plugin's resources. Only the three assets — <see cref="EnhancedInputAction"/>,
/// <see cref="EnhancedInputMappingContext"/> and <see cref="EnhancedInputConfig"/> — appear in the
/// <i>Create New Resource</i> dialog; everything that lives inside them (mappings, input sources,
/// modifiers, triggers, control schemes, haptic effects, touch layouts…) is created here, from an
/// "Add …" menu above the property that holds it. A mapping's input can also be typed as a source path.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputInspector : EditorInspectorPlugin
{
    private EditorPlugin _plugin;

    /// <summary>Connects the inspector to the plugin (for undo and redo).</summary>
    public void Setup(EditorPlugin plugin) => _plugin = plugin;

    /// <inheritdoc/>
    public override bool _CanHandle(GodotObject @object)
        => @object != null && @object.GetType().Namespace == typeof(EnhancedInputAction).Namespace;

    /// <inheritdoc/>
    public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
    {
        var property = @object.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null) return false;
        var propertyType = property.PropertyType;

        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Godot.Collections.Array<>))
        {
            var element = propertyType.GetGenericArguments()[0];
            if (IsHidden(element)) AddCustomControl(AddToArrayMenu(@object, name, element));
        }
        else if (IsHidden(propertyType))
        {
            if (propertyType == typeof(InputSource)) AddCustomControl(SourcePathEditor(@object, name));
            else if (@object.Get(name).AsGodotObject() == null) AddCustomControl(NewValueMenu(@object, name, propertyType));
        }
        return false;   // the default editor stays, to edit what was created
    }

    // ------------------------------------------------------------------
    //  Which types, under which names
    // ------------------------------------------------------------------

    /// <summary>A resource type of the plugin that is not in the Create New Resource dialog.</summary>
    private static bool IsHidden(Type type)
        => typeof(Resource).IsAssignableFrom(type) && type.Namespace == typeof(EnhancedInputAction).Namespace
           && type.GetCustomAttribute<GlobalClassAttribute>() == null;

    private static readonly Dictionary<Type, List<Type>> Concrete = new();

    /// <summary>The concrete types that can be created for a base type (itself, if concrete).</summary>
    private static List<Type> ConcreteTypes(Type baseType)
    {
        if (Concrete.TryGetValue(baseType, out var list)) return list;
        list = baseType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(FriendlyName, StringComparer.Ordinal)
            .ToList();
        // The project's own modifiers and triggers count too.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == baseType.Assembly) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            catch { continue; }
            foreach (var t in types)
                if (!t.IsAbstract && baseType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null && !list.Contains(t)) list.Add(t);
        }
        Concrete[baseType] = list;
        return list;
    }

    private static string FriendlyName(Type type)
    {
        if (type == typeof(ActionBinding)) return "Mapping";
        if (type == typeof(InputMode)) return "Control Scheme";
        if (type == typeof(InputSource)) return "Input Source";
        if (type == typeof(InputModifier)) return "Modifier";
        if (type == typeof(InputTrigger)) return "Trigger";
        var name = BindingEditing.ShortName(type);
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    // ------------------------------------------------------------------
    //  Controls
    // ------------------------------------------------------------------

    private Control AddToArrayMenu(GodotObject target, string property, Type element)
    {
        var types = ConcreteTypes(element);
        var label = $"Add {FriendlyName(element)}";
        if (types.Count == 1)
        {
            var button = new Button { Text = label, Icon = EditorIcon("Add") };
            button.Pressed += () => AppendNew(target, property, types[0]);
            return button;
        }
        var menu = new MenuButton { Text = label + "…", Icon = EditorIcon("Add"), Flat = false };
        var popup = menu.GetPopup();
        for (int i = 0; i < types.Count; i++) popup.AddItem(FriendlyName(types[i]), i);
        popup.IdPressed += id => AppendNew(target, property, types[(int)id]);
        return menu;
    }

    private Control NewValueMenu(GodotObject target, string property, Type baseType)
    {
        var types = ConcreteTypes(baseType);
        var label = $"New {FriendlyName(baseType)}";
        if (types.Count == 1)
        {
            var button = new Button { Text = label, Icon = EditorIcon("Add") };
            button.Pressed += () => Change(target, property, (Resource)Activator.CreateInstance(types[0]), label);
            return button;
        }
        var menu = new MenuButton { Text = label + "…", Icon = EditorIcon("Add"), Flat = false };
        var popup = menu.GetPopup();
        for (int i = 0; i < types.Count; i++) popup.AddItem(FriendlyName(types[i]), i);
        popup.IdPressed += id => Change(target, property, (Resource)Activator.CreateInstance(types[(int)id]), label);
        return menu;
    }

    /// <summary>A text box for the input as a source path ("key:space", "pad:a", "wasd"), checked as typed.</summary>
    private Control SourcePathEditor(GodotObject target, string property)
    {
        var box = new VBoxContainer();
        var row = new HBoxContainer();
        var current = target.Get(property).AsGodotObject() as InputSource;
        var edit = new LineEdit
        {
            Text = current == null ? string.Empty : InputSourcePath.Format(current),
            PlaceholderText = "key:space · pad:a · wasd · mouse:left · @xr:trigger",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "The input as a source path. Enter applies it.",
        };
        var error = new Label { Visible = false };
        error.AddThemeColorOverride("font_color", new Color(1f, 0.47f, 0.42f));
        edit.TextChanged += text =>
        {
            error.Text = BindingEditing.TryParsePath(text, out var message) == null ? message : string.Empty;
            error.Visible = !string.IsNullOrEmpty(error.Text);
        };
        edit.TextSubmitted += text =>
        {
            var source = BindingEditing.TryParsePath(text, out _);
            if (source != null) Change(target, property, source, "Set input");
        };
        row.AddChild(new Label { Text = "Input" });
        row.AddChild(edit);
        box.AddChild(row);
        box.AddChild(error);
        return box;
    }

    // ------------------------------------------------------------------
    //  Edits (undoable)
    // ------------------------------------------------------------------

    private void AppendNew(GodotObject target, string property, Type type)
    {
        var item = (Resource)Activator.CreateInstance(type);
        var current = target.Get(property).AsGodotArray();
        var next = current.Duplicate();
        next.Add(item);
        Change(target, property, next, $"Add {FriendlyName(type)}");
    }

    private void Change(GodotObject target, string property, Variant value, string label)
    {
        var undo = _plugin?.GetUndoRedo();
        if (undo == null)
        {
            target.Set(property, value);
            NotifyChanged(target);
            return;
        }
        undo.CreateAction(label);
        undo.AddDoProperty(target, property, value);
        undo.AddUndoProperty(target, property, target.Get(property));
        undo.AddDoMethod(this, MethodName.NotifyChanged, target);
        undo.AddUndoMethod(this, MethodName.NotifyChanged, target);
        undo.CommitAction();
    }

    private void NotifyChanged(GodotObject target)
    {
        if (target is EnhancedInputMappingContext context) context.NotifyBindingsChanged();
        target.NotifyPropertyListChanged();
    }

    private static Texture2D EditorIcon(string name)
    {
        var theme = EditorInterface.Singleton.GetEditorTheme();
        return theme != null && theme.HasIcon(name, "EditorIcons") ? theme.GetIcon(name, "EditorIcons") : null;
    }
}
#endif
