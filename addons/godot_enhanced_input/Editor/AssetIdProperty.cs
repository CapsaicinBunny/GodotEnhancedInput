#if TOOLS
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The editor of a string property holding an action or mapping context id
/// (<see cref="EnhancedInputHints"/>): a dropdown of the ids of the project's files of that kind.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class AssetIdProperty : EditorProperty
{
    private readonly OptionButton _picker = new() { FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly string _className;
    private bool _updating;

    /// <summary>For Godot (the editor recreates editors through their parameterless constructor).</summary>
    public AssetIdProperty() : this(nameof(EnhancedInputAction)) { }

    /// <summary>An editor listing the ids of one asset class.</summary>
    public AssetIdProperty(string className)
    {
        _className = className;
        AddChild(_picker);
        AddFocusable(_picker);
        _picker.ItemSelected += OnSelected;
    }

    private void Fill(string current)
    {
        _picker.Clear();
        _picker.AddItem("(none)");
        _picker.SetItemMetadata(0, string.Empty);
        bool found = string.IsNullOrEmpty(current);
        foreach (var path in EnhancedInputAssets.Paths(_className))
        {
            var id = ResourceLoader.Load(path) switch
            {
                EnhancedInputAction action => action.Id,
                EnhancedInputMappingContext context => context.Id,
                _ => null,
            };
            if (string.IsNullOrEmpty(id)) continue;
            int index = _picker.ItemCount;
            _picker.AddItem(id);
            _picker.SetItemTooltip(index, path);
            _picker.SetItemMetadata(index, id);
            if (id == current) { _picker.Selected = index; found = true; }
        }
        if (!found)
        {
            int index = _picker.ItemCount;
            _picker.AddItem($"{current} (not found)");
            _picker.SetItemMetadata(index, current);
            _picker.Selected = index;
        }
        if (string.IsNullOrEmpty(current)) _picker.Selected = 0;
    }

    /// <inheritdoc/>
    public override void _UpdateProperty()
    {
        _updating = true;
        Fill(GetEditedObject()?.Get(GetEditedProperty()).AsString() ?? string.Empty);
        _updating = false;
    }

    private void OnSelected(long index)
    {
        if (_updating) return;
        EmitChanged(GetEditedProperty(), _picker.GetItemMetadata((int)index).AsString());
    }
}
#endif
