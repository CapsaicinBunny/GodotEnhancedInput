#if TOOLS
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Editor dock (bottom panel): every binding context in the project with its bindings, the problems
/// <see cref="BindingValidator"/> finds in them, a "listen" button that assigns the next key, button
/// or stick movement to the selected binding, and the joypads connected right now.
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputDock : VBoxContainer
{
    private const double ListenGraceSeconds = 0.3;   // ignore the click that started listening

    private EditorPlugin _plugin;
    private Tree _bindings;
    private ItemList _problems;
    private Tree _devices;
    private Label _status;
    private Button _listen;
    private TabContainer _tabs;

    private readonly List<(string Path, BindingContext Context)> _contexts = new();
    private readonly List<BindingIssue> _issues = new();
    private string _targetPath;
    private int _targetIndex = -1;
    private bool _listening;
    private ulong _listenStartedMs;

    public void Setup(EditorPlugin plugin)
    {
        _plugin = plugin;
        Name = "Enhanced Input";
        CustomMinimumSize = new Vector2(0, 220);
        BuildUi();
    }

    public override void _Ready()
    {
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected) => RefreshDevices();

    // ------------------------------------------------------------------
    //  UI
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        var toolbar = new HBoxContainer();
        AddChild(toolbar);

        var refresh = new Button { Text = "Refresh", TooltipText = "Scan the project for binding contexts again." };
        refresh.Pressed += Refresh;
        toolbar.AddChild(refresh);

        _listen = new Button { Text = "Listen for input", Disabled = true, TooltipText = "Assign the next key, mouse button, joypad button or stick movement to the selected binding (Esc cancels)." };
        _listen.Pressed += StartListening;
        toolbar.AddChild(_listen);

        _status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        toolbar.AddChild(_status);

        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_tabs);

        _bindings = new Tree { Name = "Bindings", Columns = 3, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row };
        _bindings.SetColumnTitle(0, "Context / action");
        _bindings.SetColumnTitle(1, "Input");
        _bindings.SetColumnTitle(2, "Modifiers and triggers");
        _bindings.SetColumnExpand(0, true);
        _bindings.SetColumnExpand(1, true);
        _bindings.SetColumnExpand(2, true);
        _bindings.ItemSelected += OnBindingSelected;
        _bindings.ItemActivated += StartListening;
        _tabs.AddChild(_bindings);

        _problems = new ItemList { Name = "Problems" };
        _problems.ItemActivated += OnProblemActivated;
        _tabs.AddChild(_problems);

        _devices = new Tree { Name = "Devices", Columns = 3, ColumnTitlesVisible = true, HideRoot = true };
        _devices.SetColumnTitle(0, "Joypad");
        _devices.SetColumnTitle(1, "Identity (GUID)");
        _devices.SetColumnTitle(2, "Features");
        _tabs.AddChild(_devices);
    }

    // ------------------------------------------------------------------
    //  Data
    // ------------------------------------------------------------------

    private void Refresh()
    {
        StopListening(null);
        _contexts.Clear();
        var paths = new List<string>();
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        Scan(fs.GetFilesystem(), paths);
        foreach (var path in paths.OrderBy(p => p))
            if (ResourceLoader.Load(path) is BindingContext context) _contexts.Add((path, context));

        _issues.Clear();
        _issues.AddRange(BindingValidator.Validate(_contexts.Select(c => c.Context)));
        RebuildBindings();
        RebuildProblems();
        RefreshDevices();

        int errors = _issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = _issues.Count(i => i.Severity == IssueSeverity.Warning);
        _tabs.SetTabTitle(1, _issues.Count == 0 ? "Problems" : $"Problems ({errors} errors, {warnings} warnings)");
        SetStatus($"{_contexts.Count} context(s).");
    }

    private static void Scan(EditorFileSystemDirectory dir, List<string> paths)
    {
        if (dir == null) return;
        for (int i = 0; i < dir.GetFileCount(); i++)
            if (dir.GetFileScriptClassName(i) == nameof(BindingContext)) paths.Add(dir.GetFilePath(i));
        for (int i = 0; i < dir.GetSubdirCount(); i++) Scan(dir.GetSubdir(i), paths);
    }

    private void RebuildBindings()
    {
        _bindings.Clear();
        var root = _bindings.CreateItem();
        foreach (var (path, context) in _contexts)
        {
            var contextItem = _bindings.CreateItem(root);
            contextItem.SetText(0, context.Name);
            contextItem.SetTooltipText(0, path);
            contextItem.SetText(1, path);
            contextItem.SetSelectable(0, false);
            contextItem.SetSelectable(1, false);
            contextItem.SetSelectable(2, false);

            var bindings = context.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var item = _bindings.CreateItem(contextItem);
                item.SetText(0, binding?.Action?.Name ?? "(no action)");
                item.SetText(1, binding?.Source == null ? "none" : InputSourcePath.Format(binding.Source));
                item.SetText(2, binding == null ? string.Empty : Extras(binding));
                item.SetMetadata(0, path);
                item.SetMetadata(1, i);
                var worst = _issues.Where(x => x.Context == context && x.BindingIndex == i).Select(x => x.Severity).DefaultIfEmpty(IssueSeverity.Info).Max();
                if (_issues.Any(x => x.Context == context && x.BindingIndex == i && x.Severity >= IssueSeverity.Warning))
                    item.SetCustomColor(0, worst == IssueSeverity.Error ? Colors.IndianRed : Colors.Goldenrod);
            }
        }
    }

    private static string Extras(ActionBinding binding)
    {
        var names = new List<string>();
        foreach (var m in binding.Modifiers) if (m != null) names.Add(Short(m.GetType().Name, "Modifier"));
        foreach (var t in binding.Triggers) if (t != null) names.Add(Short(t.GetType().Name, "Trigger"));
        if (binding.Groups is { Length: > 0 }) names.Add($"groups: {string.Join(",", binding.Groups)}");
        if (binding.IsRebindable) names.Add($"rebind: {binding.RebindId}");
        return string.Join("  ·  ", names);
    }

    private static string Short(string name, string suffix) => name.EndsWith(suffix) ? name[..^suffix.Length] : name;

    private void RebuildProblems()
    {
        _problems.Clear();
        foreach (var issue in _issues.OrderByDescending(i => i.Severity))
        {
            int index = _problems.AddItem(issue.ToString());
            _problems.SetItemCustomFgColor(index, issue.Severity switch
            {
                IssueSeverity.Error => Colors.IndianRed,
                IssueSeverity.Warning => Colors.Goldenrod,
                _ => Colors.Gray,
            });
            _problems.SetItemMetadata(index, issue.Context?.ResourcePath ?? string.Empty);
        }
        if (_issues.Count == 0) _problems.AddItem("No problems found.");
    }

    private void OnProblemActivated(long index)
    {
        var path = _problems.GetItemMetadata((int)index).AsString();
        if (!string.IsNullOrEmpty(path)) EditorInterface.Singleton.EditResource(ResourceLoader.Load(path));
    }

    private void RefreshDevices()
    {
        if (_devices == null) return;
        _devices.Clear();
        var root = _devices.CreateItem();
        var pads = Input.GetConnectedJoypads();
        foreach (var device in pads)
        {
            var item = _devices.CreateItem(root);
            var role = InputDeviceInfo.GuessJoypadRole(Input.GetJoyName(device));
            item.SetText(0, $"{device}: {Input.GetJoyName(device)}");
            item.SetText(1, Input.GetJoyGuid(device));
            var features = new List<string> { role.ToString() };
            if (Input.HasJoyLight(device)) features.Add("light");
            if (Input.HasJoyMotionSensors(device)) features.Add("motion sensors");
            item.SetText(2, string.Join(", ", features));
        }
        if (pads.Count == 0) _devices.CreateItem(root).SetText(0, "No joypads connected.");
    }

    // ------------------------------------------------------------------
    //  Listening
    // ------------------------------------------------------------------

    private void OnBindingSelected()
    {
        var item = _bindings.GetSelected();
        if (item == null || item.GetMetadata(0).VariantType == Variant.Type.Nil)
        {
            _targetPath = null;
            _targetIndex = -1;
            _listen.Disabled = true;
            return;
        }
        _targetPath = item.GetMetadata(0).AsString();
        _targetIndex = item.GetMetadata(1).AsInt32();
        _listen.Disabled = false;
    }

    private void StartListening()
    {
        if (_targetPath == null || _targetIndex < 0) return;
        _listening = true;
        _listenStartedMs = Time.GetTicksMsec();
        SetStatus("Listening… press a key or button, or move a stick (Esc cancels).");
    }

    private void StopListening(string message)
    {
        _listening = false;
        if (message != null) SetStatus(message);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_listening) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            StopListening("Canceled.");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton && Time.GetTicksMsec() - _listenStartedMs < ListenGraceSeconds * 1000) return;
        bool actuated = @event switch
        {
            InputEventKey { Pressed: true, Echo: false } => true,
            InputEventMouseButton { Pressed: true } => true,
            InputEventJoypadButton { Pressed: true } => true,
            InputEventJoypadMotion motion => Mathf.Abs(motion.AxisValue) >= 0.5f,
            _ => false,
        };
        if (!actuated) return;
        var source = InputSource.FromEvent(@event);
        if (source == null) return;
        GetViewport().SetInputAsHandled();
        Assign(source);
    }

    private void Assign(InputSource source)
    {
        var entry = _contexts.FirstOrDefault(c => c.Path == _targetPath);
        var context = entry.Context;
        if (context == null || _targetIndex >= context.Bindings.Count || context.Bindings[_targetIndex] == null)
        {
            StopListening("The binding is gone; refresh.");
            return;
        }
        var binding = context.Bindings[_targetIndex];
        var undo = _plugin.GetUndoRedo();
        undo.CreateAction("Assign input to binding");
        undo.AddDoProperty(binding, ActionBinding.PropertyName.Source, source);
        undo.AddUndoProperty(binding, ActionBinding.PropertyName.Source, binding.Source);
        undo.AddDoMethod(this, MethodName.SaveContext, _targetPath);
        undo.AddUndoMethod(this, MethodName.SaveContext, _targetPath);
        undo.CommitAction();
        StopListening($"{binding.Action?.Name ?? "binding"} ← {InputSourcePath.Format(source)}");
    }

    private void SaveContext(string path)
    {
        var entry = _contexts.FirstOrDefault(c => c.Path == path);
        if (entry.Context == null) return;
        entry.Context.NotifyBindingsChanged();
        var error = ResourceSaver.Save(entry.Context, path);
        if (error != Error.Ok) SetStatus($"Could not save {path}: {error}");
        _issues.Clear();
        _issues.AddRange(BindingValidator.Validate(_contexts.Select(c => c.Context)));
        RebuildBindings();
        RebuildProblems();
    }

    private void SetStatus(string text)
    {
        if (_status != null) _status.Text = text;
    }
}
#endif
