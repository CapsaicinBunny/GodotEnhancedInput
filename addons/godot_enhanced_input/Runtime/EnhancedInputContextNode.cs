using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// A mapping context with a scene lifetime: active while this node is in the tree (and, with
/// <see cref="OnlyWhileParentVisible"/>, while its parent is visible) — put one in a vehicle, a menu or a
/// level, and its controls come and go with it, without code. Several nodes may hold the same context
/// (handles are counted, see <see cref="InputPlayer.PushContext"/>).
/// </summary>
[GlobalClass]
[Icon("res://addons/godot_enhanced_input/Icons/EnhancedInputMappingContext.svg")]
public partial class EnhancedInputContextNode : Node
{
    private EnhancedInputMappingContext _context;
    private bool _active = true;
    private ContextHandle _handle;
    private CanvasItem _watchedCanvas;
    private Node3D _watched3D;

    /// <summary>The mapping context to activate.</summary>
    [Export]
    public EnhancedInputMappingContext Context
    {
        get => _context;
        set
        {
            if (_context == value) return;
            Release();
            _context = value;
            Refresh();
        }
    }

    /// <summary>Use the context's own <see cref="EnhancedInputMappingContext.DefaultPriority"/> (default), or <see cref="Priority"/>.</summary>
    [Export] public bool UseContextPriority { get; set; } = true;

    /// <summary>The priority when <see cref="UseContextPriority"/> is off.</summary>
    [Export] public int Priority { get; set; }

    /// <summary>The player (index) whose input it is; see also <see cref="Player"/>.</summary>
    [Export(PropertyHint.Range, "0,15,1")] public int PlayerIndex { get; set; }

    /// <summary>Keys and buttons held when the context comes in read as zero until released (the key that opened a menu does not act in it).</summary>
    [Export] public bool IgnoreHeldSourcesUntilReleased { get; set; }

    /// <summary>Active only while the parent (a <see cref="CanvasItem"/> or <see cref="Node3D"/>) is visible — a menu's controls while it shows.</summary>
    [Export] public bool OnlyWhileParentVisible { get; set; }

    /// <summary>Turns the context off without removing the node.</summary>
    [Export]
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Refresh();
        }
    }

    /// <summary>A player to use instead of <see cref="PlayerIndex"/> (for players not owned by the autoload).</summary>
    public InputPlayer Player { get; set; }

    /// <summary>Whether the context is active through this node now.</summary>
    public bool IsHolding => _handle != null && _handle.IsActive;

    private InputPlayer TargetPlayer => Player ?? EnhancedInput.Instance?.GetPlayer(PlayerIndex);

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        Watch(GetParent());
        Refresh();
    }

    /// <inheritdoc/>
    public override void _Ready() => Refresh();   // the autoload's players exist by now

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        Watch(null);
        Release();
    }

    private void Watch(Node parent)
    {
        if (_watchedCanvas != null && IsInstanceValid(_watchedCanvas)) _watchedCanvas.VisibilityChanged -= Refresh;
        if (_watched3D != null && IsInstanceValid(_watched3D)) _watched3D.VisibilityChanged -= Refresh;
        _watchedCanvas = parent as CanvasItem;
        _watched3D = parent as Node3D;
        if (_watchedCanvas != null) _watchedCanvas.VisibilityChanged += Refresh;
        if (_watched3D != null) _watched3D.VisibilityChanged += Refresh;
    }

    private bool ParentVisible()
        => _watchedCanvas != null ? _watchedCanvas.IsVisibleInTree() : _watched3D == null || _watched3D.IsVisibleInTree();

    /// <summary>Activates or releases the context to match the node's state (called on every change).</summary>
    public void Refresh()
    {
        bool want = IsInsideTree() && _active && _context != null && (!OnlyWhileParentVisible || ParentVisible());
        if (!want) { Release(); return; }
        if (IsHolding) return;
        var player = TargetPlayer;
        if (player == null) return;
        _handle = player.PushContext(_context, UseContextPriority ? _context.DefaultPriority : Priority,
            new ContextOptions { IgnoreHeldSourcesUntilReleased = IgnoreHeldSourcesUntilReleased });
    }

    private void Release()
    {
        _handle?.Dispose();
        _handle = null;
    }
}
