#if TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// The Enhanced Input editor dock (bottom panel).
/// <list type="bullet">
/// <item><b>Bindings</b>: every binding context in the project, searchable, with device icons; a side
/// panel edits the selected binding — action (or a new one), input path with live validation,
/// listen-to-assign, groups, rebind id, modifiers and triggers — all with undo, saved at once.</item>
/// <item><b>Actions</b>: every action with its value kind and where it is bound.</item>
/// <item><b>Problems</b>: what <see cref="BindingValidator"/> finds; double-click jumps to the binding.</item>
/// <item><b>Devices</b>: connected pads with family, identity and features, live axes and buttons, a
/// rumble test and the light colour; an input log that shows each event's path for binding odd hardware.</item>
/// </list>
/// </summary>
[Tool]
[ExcludeFromCodeCoverage]
public partial class EnhancedInputDock : VBoxContainer
{
    private const double ListenGraceSeconds = 0.3;

    private EditorPlugin _plugin;
    private Theme _theme;

    // Toolbar
    private LineEdit _search;
    private Button _listen, _addBinding, _removeBinding;
    private Label _status, _errorsBadge, _warningsBadge;
    private TabContainer _tabs;

    // Bindings tab
    private Tree _bindings;
    private VBoxContainer _detail;
    private Label _detailTitle;
    private OptionButton _actionPicker;
    private LineEdit _pathEdit, _groupsEdit, _rebindEdit, _newActionName;
    private Label _pathError;
    private OptionButton _newActionKind, _addModifier, _addTrigger;
    private ItemList _modifierList, _triggerList;

    // Other tabs
    private Tree _actions;
    private ItemList _problems;
    private ItemList _padList;
    private VBoxContainer _padDetail;
    private Label _padInfo;
    private readonly List<ProgressBar> _axisBars = new();
    private readonly List<Label> _buttonLights = new();
    private ColorPickerButton _lightColor;
    private CheckButton _logInput;
    private ItemList _log;

    // Data
    private readonly List<(string Path, BindingContext Context)> _contexts = new();
    private readonly List<BindingIssue> _issues = new();
    private string _selectedPath;
    private int _selectedIndex = -1;
    private int _selectedPad = -1;
    private bool _listening;
    private ulong _listenStartedMs;
    private bool _updatingDetail;

    public void Setup(EditorPlugin plugin)
    {
        _plugin = plugin;
        Name = "Enhanced Input";
        CustomMinimumSize = new Vector2(0, 260);
        _theme = EditorInterface.Singleton.GetEditorTheme();
        BuildUi();
    }

    public override void _Ready()
    {
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        EditorInterface.Singleton.GetResourceFilesystem().FilesystemChanged += OnFilesystemChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        if (fs != null) fs.FilesystemChanged -= OnFilesystemChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected) => RefreshPads();

    private bool _refreshQueued;
    private void OnFilesystemChanged()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        CallDeferred(MethodName.DeferredRefresh);
    }

    private void DeferredRefresh()
    {
        _refreshQueued = false;
        Refresh();
    }

    // ------------------------------------------------------------------
    //  Look
    // ------------------------------------------------------------------

    private Texture2D Icon(string name) => _theme != null && _theme.HasIcon(name, "EditorIcons") ? _theme.GetIcon(name, "EditorIcons") : null;

    private Color EditorColor(string name, Color fallback) => _theme != null && _theme.HasColor(name, "Editor") ? _theme.GetColor(name, "Editor") : fallback;

    private Color Accent => EditorColor("accent_color", new Color(0.44f, 0.73f, 0.98f));
    private Color ErrorColor => EditorColor("error_color", new Color(1f, 0.47f, 0.42f));
    private Color WarningColor => EditorColor("warning_color", new Color(1f, 0.87f, 0.4f));
    private Color DimColor => EditorColor("font_disabled_color", new Color(1, 1, 1, 0.45f));

    private Texture2D IconFor(InputDeviceKind kind) => kind switch
    {
        InputDeviceKind.Joypad => Icon("JoyButton") ?? Icon("InputEventJoypadButton"),
        InputDeviceKind.Touch => Icon("InputEventScreenTouch"),
        InputDeviceKind.Pen => Icon("InputEventMouseMotion"),
        InputDeviceKind.Custom => Icon("XRController3D") ?? Icon("Joypad"),
        InputDeviceKind.KeyboardMouse => Icon("Keyboard") ?? Icon("InputEventKey"),
        _ => Icon("GuiRadioUnchecked"),
    };

    private static PanelContainer Card(Control content, Color background)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = background, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        style.SetContentMarginAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(content);
        return panel;
    }

    private Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Accent);
        return label;
    }

    private Label Badge(Texture2D icon, Color color)
    {
        var label = new Label { Text = "0", TooltipText = "", VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // ------------------------------------------------------------------
    //  Layout
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 4);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        AddChild(toolbar);

        var title = new Label { Text = "Enhanced Input" };
        title.AddThemeColorOverride("font_color", Accent);
        toolbar.AddChild(title);
        toolbar.AddChild(new VSeparator());

        _search = new LineEdit { PlaceholderText = "Search actions, inputs, groups…", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(240, 0), RightIcon = Icon("Search") };
        _search.TextChanged += _ => RebuildBindings();
        toolbar.AddChild(_search);

        toolbar.AddChild(ToolButton("Reload", "Rescan the project for binding contexts.", Refresh));
        toolbar.AddChild(ToolButton("Add", "New binding context…", NewContext, "Context"));
        _addBinding = ToolButton("Add", "Add a binding to the selected context.", AddBinding, "Binding");
        toolbar.AddChild(_addBinding);
        _removeBinding = ToolButton("Remove", "Remove the selected binding.", RemoveBinding);
        _removeBinding.Disabled = true;
        toolbar.AddChild(_removeBinding);
        _listen = ToolButton("InputEventKey", "Assign the next key, button or stick movement to the selected binding (Esc cancels).", StartListening, "Listen");
        _listen.Disabled = true;
        toolbar.AddChild(_listen);

        toolbar.AddChild(new VSeparator());
        var errorIcon = new TextureRect { Texture = Icon("StatusError"), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        toolbar.AddChild(errorIcon);
        _errorsBadge = Badge(Icon("StatusError"), ErrorColor);
        toolbar.AddChild(_errorsBadge);
        var warningIcon = new TextureRect { Texture = Icon("StatusWarning"), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        toolbar.AddChild(warningIcon);
        _warningsBadge = Badge(Icon("StatusWarning"), WarningColor);
        toolbar.AddChild(_warningsBadge);

        _status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, ClipText = true };
        _status.AddThemeColorOverride("font_color", DimColor);
        toolbar.AddChild(_status);

        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_tabs);
        _tabs.AddChild(BuildBindingsTab());
        _tabs.AddChild(BuildActionsTab());
        _tabs.AddChild(BuildProblemsTab());
        _tabs.AddChild(BuildDevicesTab());
        _tabs.SetTabIcon(0, Icon("InputEventKey"));
        _tabs.SetTabIcon(1, Icon("InputEventAction"));
        _tabs.SetTabIcon(2, Icon("StatusWarning"));
        _tabs.SetTabIcon(3, Icon("JoyButton") ?? Icon("InputEventJoypadButton"));
    }

    private Button ToolButton(string icon, string tooltip, Action pressed, string text = null)
    {
        var button = new Button { Icon = Icon(icon), TooltipText = tooltip, Flat = true, Text = text ?? string.Empty };
        button.Pressed += pressed;
        return button;
    }

    private Control BuildBindingsTab()
    {
        var split = new HSplitContainer { Name = "Bindings", SplitOffsets = new[] { 520 } };

        _bindings = new Tree { Columns = 3, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _bindings.SetColumnTitle(0, "Context / action");
        _bindings.SetColumnTitle(1, "Input");
        _bindings.SetColumnTitle(2, "Modifiers · triggers · groups");
        _bindings.SetColumnExpandRatio(0, 2);
        _bindings.SetColumnExpandRatio(1, 2);
        _bindings.SetColumnExpandRatio(2, 3);
        _bindings.ItemSelected += OnBindingSelected;
        _bindings.ItemActivated += StartListening;
        split.AddChild(_bindings);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_detail);
        split.AddChild(scroll);

        _detailTitle = new Label { Text = "Select a binding", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _detailTitle.AddThemeColorOverride("font_color", Accent);
        _detail.AddChild(_detailTitle);

        // Action
        _detail.AddChild(Heading("Action"));
        _actionPicker = new OptionButton { FitToLongestItem = false };
        _actionPicker.ItemSelected += OnActionPicked;
        _detail.AddChild(_actionPicker);
        var newActionRow = new HBoxContainer();
        _newActionName = new LineEdit { PlaceholderText = "new action name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _newActionKind = new OptionButton();
        foreach (var kind in Enum.GetValues<ActionValueKind>()) _newActionKind.AddItem(kind.ToString(), (int)kind);
        var create = new Button { Text = "Create", Icon = Icon("Add") };
        create.Pressed += CreateAction;
        newActionRow.AddChild(_newActionName);
        newActionRow.AddChild(_newActionKind);
        newActionRow.AddChild(create);
        _detail.AddChild(newActionRow);

        // Input
        _detail.AddChild(Heading("Input"));
        var pathRow = new HBoxContainer();
        _pathEdit = new LineEdit { PlaceholderText = "key:space · pad:a · pad:left_stick · @xr:trigger", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pathEdit.TextChanged += ValidatePath;
        _pathEdit.TextSubmitted += ApplyPath;
        var listen = new Button { Icon = Icon("InputEventKey"), TooltipText = "Listen for input", Text = "Listen" };
        listen.Pressed += StartListening;
        pathRow.AddChild(_pathEdit);
        pathRow.AddChild(listen);
        _detail.AddChild(pathRow);
        _pathError = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _pathError.AddThemeColorOverride("font_color", ErrorColor);
        _detail.AddChild(_pathError);

        // Groups and rebinding
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Groups" });
        _groupsEdit = new LineEdit { PlaceholderText = "kbm, pad", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _groupsEdit.TextSubmitted += ApplyGroups;
        _groupsEdit.FocusExited += () => ApplyGroups(_groupsEdit.Text);
        grid.AddChild(_groupsEdit);
        grid.AddChild(new Label { Text = "Rebind id" });
        _rebindEdit = new LineEdit { PlaceholderText = "empty: not rebindable", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rebindEdit.TextSubmitted += ApplyRebindId;
        _rebindEdit.FocusExited += () => ApplyRebindId(_rebindEdit.Text);
        grid.AddChild(_rebindEdit);
        _detail.AddChild(grid);

        // Modifiers and triggers
        _detail.AddChild(Heading("Modifiers"));
        _modifierList = new ItemList { AutoHeight = true, CustomMinimumSize = new Vector2(0, 24) };
        _modifierList.ItemActivated += i => InspectItem(true, (int)i);
        _detail.AddChild(_modifierList);
        _detail.AddChild(StackEditor(true, out _addModifier));

        _detail.AddChild(Heading("Triggers"));
        _triggerList = new ItemList { AutoHeight = true, CustomMinimumSize = new Vector2(0, 24) };
        _triggerList.ItemActivated += i => InspectItem(false, (int)i);
        _detail.AddChild(_triggerList);
        _detail.AddChild(StackEditor(false, out _addTrigger));

        var inspect = new Button { Text = "Open binding in the Inspector", Icon = Icon("Edit") };
        inspect.Pressed += () => { var b = SelectedBinding(); if (b != null) EditorInterface.Singleton.EditResource(b); };
        _detail.AddChild(inspect);

        SetDetailEnabled(false);
        return split;
    }

    private HBoxContainer StackEditor(bool modifiers, out OptionButton picker)
    {
        var row = new HBoxContainer();
        var options = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var t in modifiers ? BindingEditing.ModifierTypes : BindingEditing.TriggerTypes) options.AddItem(BindingEditing.ShortName(t));
        var add = new Button { Icon = Icon("Add"), TooltipText = "Add" };
        add.Pressed += () => AddStackItem(modifiers, options.GetItemText(options.Selected));
        var remove = new Button { Icon = Icon("Remove"), TooltipText = "Remove the selected one" };
        remove.Pressed += () => RemoveStackItem(modifiers);
        var up = new Button { Icon = Icon("MoveUp"), TooltipText = "Move up" };
        up.Pressed += () => MoveStackItem(modifiers, -1);
        var down = new Button { Icon = Icon("MoveDown"), TooltipText = "Move down" };
        down.Pressed += () => MoveStackItem(modifiers, 1);
        row.AddChild(options);
        row.AddChild(add);
        row.AddChild(remove);
        row.AddChild(up);
        row.AddChild(down);
        picker = options;
        return row;
    }

    private Control BuildActionsTab()
    {
        _actions = new Tree { Name = "Actions", Columns = 4, ColumnTitlesVisible = true, HideRoot = true };
        _actions.SetColumnTitle(0, "Action");
        _actions.SetColumnTitle(1, "Value");
        _actions.SetColumnTitle(2, "Bindings");
        _actions.SetColumnTitle(3, "Contexts");
        _actions.SetColumnExpandRatio(3, 3);
        _actions.ItemActivated += () =>
        {
            if (_actions.GetSelected()?.GetMetadata(0).AsGodotObject() is GameAction action) EditorInterface.Singleton.EditResource(action);
        };
        return _actions;
    }

    private Control BuildProblemsTab()
    {
        _problems = new ItemList { Name = "Problems" };
        _problems.ItemActivated += OnProblemActivated;
        return _problems;
    }

    private Control BuildDevicesTab()
    {
        var split = new HSplitContainer { Name = "Devices", SplitOffsets = new[] { 300 } };
        var left = new VBoxContainer();
        _padList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _padList.ItemSelected += i => SelectPad((int)_padList.GetItemMetadata((int)i).AsInt32());
        left.AddChild(_padList);
        _logInput = new CheckButton { Text = "Log input (shows each event's path)" };
        left.AddChild(_logInput);
        _log = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        _log.ItemActivated += i =>
        {
            var path = _log.GetItemMetadata((int)i).AsString();
            if (!string.IsNullOrEmpty(path) && SelectedBinding() != null) { _pathEdit.Text = path; ApplyPath(path); }
        };
        left.AddChild(_log);
        split.AddChild(left);

        _padDetail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _padInfo = new Label { Text = "Select a joypad", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _padDetail.AddChild(_padInfo);

        _padDetail.AddChild(Heading("Axes"));
        var axes = new GridContainer { Columns = 4 };
        for (int a = 0; a < 6; a++)
        {
            axes.AddChild(new Label { Text = InputPrompts.JoyAxisName((JoyAxis)a) });
            var bar = new ProgressBar { MinValue = -1, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(120, 14), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _axisBars.Add(bar);
            axes.AddChild(bar);
        }
        _padDetail.AddChild(axes);

        _padDetail.AddChild(Heading("Buttons"));
        var buttons = new HFlowContainer();
        for (int b = 0; b < 21; b++)
        {
            var light = new Label { Text = InputPrompts.JoyButtonName((JoyButton)b), TooltipText = $"button {b}" };
            light.AddThemeColorOverride("font_color", DimColor);
            _buttonLights.Add(light);
            buttons.AddChild(Card(light, new Color(0, 0, 0, 0.15f)));
        }
        _padDetail.AddChild(buttons);

        var actions = new HBoxContainer();
        var rumble = new Button { Text = "Test rumble", Icon = Icon("AudioStreamPlayer") };
        rumble.Pressed += () => { if (_selectedPad >= 0) Input.StartJoyVibration(_selectedPad, 0.5f, 0.8f, 0.4f); };
        actions.AddChild(rumble);
        _lightColor = new ColorPickerButton { Color = Colors.Blue, CustomMinimumSize = new Vector2(48, 0), TooltipText = "Light colour (pads with a light)" };
        _lightColor.ColorChanged += c => { if (_selectedPad >= 0 && Input.HasJoyLight(_selectedPad)) Input.SetJoyLight(_selectedPad, c); };
        actions.AddChild(new Label { Text = "Light" });
        actions.AddChild(_lightColor);
        _padDetail.AddChild(actions);

        split.AddChild(_padDetail);
        return split;
    }

    // ------------------------------------------------------------------
    //  Data
    // ------------------------------------------------------------------

    private void Refresh()
    {
        StopListening(null);
        BindingEditing.Rescan();
        _contexts.Clear();
        var paths = new List<string>();
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        Scan(fs.GetFilesystem(), paths);
        foreach (var path in paths.OrderBy(p => p))
            if (ResourceLoader.Load(path) is BindingContext context) _contexts.Add((path, context));
        Revalidate();
        RefreshPads();
        SetStatus($"{_contexts.Count} context(s), {BindingEditing.ActionsIn(_contexts.Select(c => c.Context)).Count} action(s).");
    }

    private void Revalidate()
    {
        _issues.Clear();
        _issues.AddRange(BindingValidator.Validate(_contexts.Select(c => c.Context)));
        int errors = _issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = _issues.Count(i => i.Severity == IssueSeverity.Warning);
        _errorsBadge.Text = errors.ToString();
        _warningsBadge.Text = warnings.ToString();
        _tabs.SetTabTitle(2, _issues.Count == 0 ? "Problems" : $"Problems ({errors + warnings})");
        RebuildBindings();
        RebuildActions();
        RebuildProblems();
    }

    private static void Scan(EditorFileSystemDirectory dir, List<string> paths)
    {
        if (dir == null) return;
        for (int i = 0; i < dir.GetFileCount(); i++)
            if (IsBindingContextFile(dir.GetFilePath(i))) paths.Add(dir.GetFilePath(i));
        for (int i = 0; i < dir.GetSubdirCount(); i++) Scan(dir.GetSubdir(i), paths);
    }

    /// <summary>
    /// Whether a text resource is a binding context: its header names the script class (the editor's
    /// file index only knows script classes of script files, not of resources).
    /// </summary>
    private static bool IsBindingContextFile(string path)
    {
        if (!path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)) return false;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return false;
        var header = file.GetLine();
        return header.Contains($"script_class=\"{nameof(BindingContext)}\"", StringComparison.Ordinal);
    }

    private void RebuildBindings()
    {
        _bindings.Clear();
        var root = _bindings.CreateItem();
        TreeItem reselect = null;
        foreach (var (path, context) in _contexts)
        {
            var contextItem = _bindings.CreateItem(root);
            contextItem.SetText(0, context.Name);
            contextItem.SetIcon(0, Icon("Object"));
            contextItem.SetTooltipText(0, path);
            contextItem.SetText(1, path);
            contextItem.SetCustomColor(1, DimColor);
            contextItem.SetMetadata(0, path);
            contextItem.SetMetadata(1, -1);
            if (context.BlockLowerPriority) contextItem.SetText(2, "blocks lower contexts");

            var bindings = context.Bindings;
            int shown = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (!BindingEditing.Matches(binding, _search.Text)) continue;
                shown++;
                var item = _bindings.CreateItem(contextItem);
                item.SetText(0, binding?.Action?.Name ?? "(no action)");
                item.SetIcon(0, Icon("InputEventAction"));
                item.SetText(1, BindingEditing.PathOf(binding?.Source));
                item.SetIcon(1, IconFor(BindingEditing.DeviceKindOf(binding?.Source)));
                item.SetTooltipText(1, binding?.Source == null ? "no input" : InputPrompts.GetName(binding.Source));
                item.SetText(2, BindingEditing.Summary(binding));
                item.SetCustomColor(2, DimColor);
                item.SetMetadata(0, path);
                item.SetMetadata(1, i);
                var worst = _issues.Where(x => x.Context == context && x.BindingIndex == i).Select(x => x.Severity).DefaultIfEmpty(IssueSeverity.Info).Max();
                if (_issues.Any(x => x.Context == context && x.BindingIndex == i && x.Severity >= IssueSeverity.Warning))
                {
                    item.SetCustomColor(0, worst == IssueSeverity.Error ? ErrorColor : WarningColor);
                    item.SetTooltipText(0, string.Join("\n", _issues.Where(x => x.Context == context && x.BindingIndex == i).Select(x => x.Message)));
                }
                if (path == _selectedPath && i == _selectedIndex) reselect = item;
            }
            if (shown == 0 && !string.IsNullOrWhiteSpace(_search.Text)) contextItem.Free();
        }
        if (reselect != null)
        {
            _updatingDetail = true;
            reselect.Select(0);
            _bindings.ScrollToItem(reselect);
            _updatingDetail = false;
        }
        UpdateDetail();
    }

    private void RebuildActions()
    {
        _actions.Clear();
        var root = _actions.CreateItem();
        foreach (var action in BindingEditing.ActionsIn(_contexts.Select(c => c.Context)))
        {
            var where = _contexts.Where(c => c.Context.Bindings.Any(b => b?.Action == action)).Select(c => c.Context.Name).ToList();
            int count = _contexts.Sum(c => c.Context.Bindings.Count(b => b?.Action == action));
            var item = _actions.CreateItem(root);
            item.SetText(0, action.Name);
            item.SetIcon(0, Icon("InputEventAction"));
            item.SetText(1, action.ValueKind.ToString());
            item.SetText(2, count.ToString());
            item.SetText(3, string.Join(", ", where));
            item.SetMetadata(0, action);
        }
    }

    private void RebuildProblems()
    {
        _problems.Clear();
        foreach (var issue in _issues.OrderByDescending(i => i.Severity))
        {
            var icon = issue.Severity switch
            {
                IssueSeverity.Error => Icon("StatusError"),
                IssueSeverity.Warning => Icon("StatusWarning"),
                _ => Icon("NodeInfo"),
            };
            int index = _problems.AddItem(issue.ToString(), icon);
            if (issue.Severity == IssueSeverity.Info) _problems.SetItemCustomFgColor(index, DimColor);
            _problems.SetItemMetadata(index, issue.Context != null ? $"{issue.Context.ResourcePath}|{issue.BindingIndex}" : string.Empty);
        }
        if (_issues.Count == 0) _problems.AddItem("No problems found.", Icon("StatusSuccess"));
    }

    private void OnProblemActivated(long index)
    {
        var meta = _problems.GetItemMetadata((int)index).AsString();
        if (string.IsNullOrEmpty(meta)) return;
        var parts = meta.Split('|');
        _selectedPath = parts[0];
        _selectedIndex = parts.Length > 1 && int.TryParse(parts[1], out var i) ? i : -1;
        _search.Text = string.Empty;
        _tabs.CurrentTab = 0;
        RebuildBindings();
    }

    // ------------------------------------------------------------------
    //  Selection and the detail panel
    // ------------------------------------------------------------------

    private BindingContext SelectedContext() => _contexts.FirstOrDefault(c => c.Path == _selectedPath).Context;

    private ActionBinding SelectedBinding()
    {
        var context = SelectedContext();
        return context != null && _selectedIndex >= 0 && _selectedIndex < context.Bindings.Count ? context.Bindings[_selectedIndex] : null;
    }

    private void OnBindingSelected()
    {
        if (_updatingDetail) return;
        var item = _bindings.GetSelected();
        _selectedPath = item?.GetMetadata(0).AsString();
        _selectedIndex = item == null ? -1 : item.GetMetadata(1).AsInt32();
        UpdateDetail();
    }

    private void SetDetailEnabled(bool enabled)
    {
        foreach (var control in new Control[] { _actionPicker, _pathEdit, _groupsEdit, _rebindEdit, _newActionName, _modifierList, _triggerList })
            if (control is LineEdit line) line.Editable = enabled;
            else if (control is OptionButton option) option.Disabled = !enabled;
        if (_listen != null) _listen.Disabled = !enabled;
        if (_removeBinding != null) _removeBinding.Disabled = !enabled;
    }

    private void UpdateDetail()
    {
        var binding = SelectedBinding();
        var context = SelectedContext();
        _addBinding.Disabled = context == null;
        SetDetailEnabled(binding != null);
        _updatingDetail = true;
        try
        {
            _actionPicker.Clear();
            _modifierList.Clear();
            _triggerList.Clear();
            _pathError.Text = string.Empty;
            if (binding == null)
            {
                _detailTitle.Text = context != null ? $"{context.Name}: select a binding, or add one" : "Select a binding";
                _pathEdit.Text = _groupsEdit.Text = _rebindEdit.Text = string.Empty;
                return;
            }
            _detailTitle.Text = $"{context.Name} · #{_selectedIndex} · {binding.Action?.Name ?? "(no action)"}";
            var actions = BindingEditing.ActionsIn(_contexts.Select(c => c.Context));
            _actionPicker.AddItem("(no action)");
            for (int i = 0; i < actions.Count; i++)
            {
                _actionPicker.AddIconItem(Icon("InputEventAction"), $"{actions[i].Name}  ({actions[i].ValueKind})");
                _actionPicker.SetItemMetadata(i + 1, actions[i]);
                if (actions[i] == binding.Action) _actionPicker.Selected = i + 1;
            }
            if (binding.Action == null) _actionPicker.Selected = 0;
            _pathEdit.Text = BindingEditing.PathOf(binding.Source);
            _groupsEdit.Text = BindingEditing.GroupsText(binding);
            _rebindEdit.Text = binding.RebindId;
            foreach (var m in binding.Modifiers) _modifierList.AddItem(BindingEditing.ShortName(m), Icon("Tools"));
            foreach (var t in binding.Triggers) _triggerList.AddItem(BindingEditing.ShortName(t), Icon("Timer"));
        }
        finally
        {
            _updatingDetail = false;
        }
    }

    private void InspectItem(bool modifiers, int index)
    {
        var binding = SelectedBinding();
        if (binding == null) return;
        Resource item = modifiers ? (index < binding.Modifiers.Count ? binding.Modifiers[index] : null) : (index < binding.Triggers.Count ? binding.Triggers[index] : null);
        if (item != null) EditorInterface.Singleton.EditResource(item);
    }

    // ------------------------------------------------------------------
    //  Edits (all undoable, saved at once)
    // ------------------------------------------------------------------

    private void Change(string label, GodotObject target, StringName property, Variant value)
    {
        var undo = _plugin.GetUndoRedo();
        undo.CreateAction(label);
        undo.AddDoProperty(target, property, value);
        undo.AddUndoProperty(target, property, target.Get(property));
        undo.AddDoMethod(this, MethodName.SaveContext, _selectedPath);
        undo.AddUndoMethod(this, MethodName.SaveContext, _selectedPath);
        undo.CommitAction();
    }

    private void OnActionPicked(long index)
    {
        if (_updatingDetail) return;
        var binding = SelectedBinding();
        if (binding == null) return;
        var action = index <= 0 ? null : _actionPicker.GetItemMetadata((int)index).AsGodotObject() as GameAction;
        Change("Set binding action", binding, ActionBinding.PropertyName.Action, action);
    }

    private void CreateAction()
    {
        var binding = SelectedBinding();
        var name = _newActionName.Text.Trim();
        if (binding == null || string.IsNullOrEmpty(name)) { SetStatus("Type a name for the new action."); return; }
        var kind = (ActionValueKind)_newActionKind.GetSelectedId();
        var action = GameAction.Create(name, kind);
        var folder = _selectedPath.GetBaseDir();
        var path = $"{folder}/{name.ToLowerInvariant().Replace(' ', '_')}.tres";
        if (FileAccess.FileExists(path)) { SetStatus($"{path} already exists."); return; }
        var error = ResourceSaver.Save(action, path);
        if (error != Error.Ok) { SetStatus($"Could not save {path}: {error}"); return; }
        var saved = ResourceLoader.Load<GameAction>(path);
        _newActionName.Text = string.Empty;
        Change("Create action", binding, ActionBinding.PropertyName.Action, saved);
        SetStatus($"Created {path}");
    }

    private void ValidatePath(string text)
    {
        BindingEditing.TryParsePath(text, out var error);
        _pathError.Text = error ?? string.Empty;
    }

    private void ApplyPath(string text)
    {
        var binding = SelectedBinding();
        if (binding == null) return;
        var source = BindingEditing.TryParsePath(text, out var error);
        if (error != null) { _pathError.Text = error; return; }
        if (source != null && binding.Source != null && InputSourceJson.SameInput(source, binding.Source)) return;
        Change("Set binding input", binding, ActionBinding.PropertyName.Source, source);
    }

    private void ApplyGroups(string text)
    {
        if (_updatingDetail) return;
        var binding = SelectedBinding();
        if (binding == null) return;
        var groups = BindingEditing.ParseGroups(text);
        if (groups.SequenceEqual(binding.Groups ?? Array.Empty<string>())) return;
        Change("Set binding groups", binding, ActionBinding.PropertyName.Groups, groups);
    }

    private void ApplyRebindId(string text)
    {
        if (_updatingDetail) return;
        var binding = SelectedBinding();
        if (binding == null || (text ?? string.Empty).Trim() == binding.RebindId) return;
        Change("Set rebind id", binding, ActionBinding.PropertyName.RebindId, (text ?? string.Empty).Trim());
    }

    private void AddStackItem(bool modifiers, string typeName)
    {
        var binding = SelectedBinding();
        var item = BindingEditing.Create(typeName);
        if (binding == null || item == null) return;
        if (modifiers)
        {
            var list = new Godot.Collections.Array<InputModifier>(binding.Modifiers) { (InputModifier)item };
            Change($"Add {typeName}", binding, ActionBinding.PropertyName.Modifiers, list);
        }
        else
        {
            var list = new Godot.Collections.Array<InputTrigger>(binding.Triggers) { (InputTrigger)item };
            Change($"Add {typeName}", binding, ActionBinding.PropertyName.Triggers, list);
        }
    }

    private void RemoveStackItem(bool modifiers)
    {
        var binding = SelectedBinding();
        var selected = (modifiers ? _modifierList : _triggerList).GetSelectedItems();
        if (binding == null || selected.Length == 0) return;
        int index = selected[0];
        if (modifiers)
        {
            var list = new Godot.Collections.Array<InputModifier>(binding.Modifiers);
            list.RemoveAt(index);
            Change("Remove modifier", binding, ActionBinding.PropertyName.Modifiers, list);
        }
        else
        {
            var list = new Godot.Collections.Array<InputTrigger>(binding.Triggers);
            list.RemoveAt(index);
            Change("Remove trigger", binding, ActionBinding.PropertyName.Triggers, list);
        }
    }

    private void MoveStackItem(bool modifiers, int direction)
    {
        var binding = SelectedBinding();
        var selected = (modifiers ? _modifierList : _triggerList).GetSelectedItems();
        if (binding == null || selected.Length == 0) return;
        int from = selected[0], to = from + direction;
        if (modifiers)
        {
            var list = new Godot.Collections.Array<InputModifier>(binding.Modifiers);
            if (to < 0 || to >= list.Count) return;
            (list[from], list[to]) = (list[to], list[from]);
            Change("Reorder modifiers", binding, ActionBinding.PropertyName.Modifiers, list);
        }
        else
        {
            var list = new Godot.Collections.Array<InputTrigger>(binding.Triggers);
            if (to < 0 || to >= list.Count) return;
            (list[from], list[to]) = (list[to], list[from]);
            Change("Reorder triggers", binding, ActionBinding.PropertyName.Triggers, list);
        }
        (modifiers ? _modifierList : _triggerList).Select(to);
    }

    private void AddBinding()
    {
        var context = SelectedContext();
        if (context == null) { SetStatus("Select a context first."); return; }
        var current = SelectedBinding();
        var binding = ActionBinding.Create(current?.Action, null);
        var list = new Godot.Collections.Array<ActionBinding>(context.Bindings) { binding };
        _selectedIndex = list.Count - 1;
        Change("Add binding", context, BindingContext.PropertyName.Bindings, list);
    }

    private void RemoveBinding()
    {
        var context = SelectedContext();
        if (context == null || _selectedIndex < 0 || _selectedIndex >= context.Bindings.Count) return;
        var list = new Godot.Collections.Array<ActionBinding>(context.Bindings);
        list.RemoveAt(_selectedIndex);
        Change("Remove binding", context, BindingContext.PropertyName.Bindings, list);
        _selectedIndex = Math.Min(_selectedIndex, list.Count - 1);
    }

    private void NewContext()
    {
        var dialog = new EditorFileDialog
        {
            FileMode = EditorFileDialog.FileModeEnum.SaveFile,
            Access = EditorFileDialog.AccessEnum.Resources,
            Title = "New binding context",
            CurrentFile = "new_context.tres",
        };
        dialog.AddFilter("*.tres", "Binding context");
        dialog.FileSelected += path =>
        {
            var context = BindingContext.Create(path.GetFile().GetBaseName());
            var error = ResourceSaver.Save(context, path);
            SetStatus(error == Error.Ok ? $"Created {path}" : $"Could not save {path}: {error}");
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);
            _selectedPath = path;
            _selectedIndex = -1;
            Refresh();
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(800, 500));
    }

    private void SaveContext(string path)
    {
        var entry = _contexts.FirstOrDefault(c => c.Path == path);
        if (entry.Context == null) return;
        entry.Context.NotifyBindingsChanged();
        var error = ResourceSaver.Save(entry.Context, path);
        if (error != Error.Ok) SetStatus($"Could not save {path}: {error}");
        Revalidate();
    }

    // ------------------------------------------------------------------
    //  Listening, the input log, live pads
    // ------------------------------------------------------------------

    private void StartListening()
    {
        if (SelectedBinding() == null) return;
        _listening = true;
        _listenStartedMs = Time.GetTicksMsec();
        SetStatus("Listening… press a key or button, or move a stick (Esc cancels).");
        _status.AddThemeColorOverride("font_color", Accent);
    }

    private void StopListening(string message)
    {
        _listening = false;
        _status?.AddThemeColorOverride("font_color", DimColor);
        if (message != null) SetStatus(message);
    }

    public override void _Input(InputEvent @event)
    {
        bool actuated = @event switch
        {
            InputEventKey { Pressed: true, Echo: false } => true,
            InputEventMouseButton { Pressed: true } => true,
            InputEventJoypadButton { Pressed: true } => true,
            InputEventJoypadMotion motion => Mathf.Abs(motion.AxisValue) >= 0.5f,
            _ => false,
        };
        if (!actuated) return;

        if (_logInput != null && _logInput.ButtonPressed && IsVisibleInTree() && @event is not InputEventMouseButton)
        {
            var logged = InputSource.FromEvent(@event, anyJoypad: false);
            var path = logged == null ? string.Empty : InputSourcePath.Format(logged);
            int i = _log.AddItem($"{@event.GetType().Name.Replace("InputEvent", "")}  dev {@event.Device}  →  {path}", IconFor(BindingEditing.DeviceKindOf(logged)));
            _log.SetItemMetadata(i, path);
            _log.SetItemTooltip(i, "Double-click to use it for the selected binding");
            while (_log.ItemCount > 60) _log.RemoveItem(0);
            _log.EnsureCurrentIsVisible();
            _log.Select(_log.ItemCount - 1);
        }

        if (!_listening) return;
        if (@event is InputEventKey { Keycode: Key.Escape })
        {
            StopListening("Canceled.");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton && Time.GetTicksMsec() - _listenStartedMs < ListenGraceSeconds * 1000) return;
        var source = InputSource.FromEvent(@event);
        if (source == null) return;
        GetViewport().SetInputAsHandled();
        var binding = SelectedBinding();
        if (binding == null) { StopListening("The binding is gone."); return; }
        Change("Assign input", binding, ActionBinding.PropertyName.Source, source);
        StopListening($"{binding.Action?.Name ?? "binding"}  ←  {InputSourcePath.Format(source)}");
    }

    private void RefreshPads()
    {
        if (_padList == null) return;
        _padList.Clear();
        foreach (var device in Input.GetConnectedJoypads())
        {
            var family = ControllerDetection.Detect(device);
            int i = _padList.AddItem($"{device}: {ControllerDetection.DisplayName(family)}  —  {Input.GetJoyName(device)}", Icon("JoyButton") ?? Icon("InputEventJoypadButton"));
            _padList.SetItemMetadata(i, device);
            if (device == _selectedPad) _padList.Select(i);
        }
        if (_padList.ItemCount == 0)
        {
            _padList.AddItem("No joypads connected.");
            _padList.SetItemMetadata(0, -1);
            _padList.SetItemDisabled(0, true);
            SelectPad(-1);
        }
        else if (_selectedPad < 0) SelectPad(_padList.GetItemMetadata(0).AsInt32());
    }

    private void SelectPad(int device)
    {
        _selectedPad = device;
        if (device < 0) { _padInfo.Text = "Select a joypad"; return; }
        var family = ControllerDetection.Detect(device);
        var (vendor, product) = ControllerDetection.ReadIds(device);
        var available = ControllerDetection.AvailableFeatures(device, family);
        var hardware = ControllerDetection.HardwareFeatures(family);
        _padInfo.Text = $"{ControllerDetection.DisplayName(family)}   ({family}, prompts: {ControllerDetection.StyleOf(family)})\n"
            + $"Name: {Input.GetJoyName(device)}\nGUID: {Input.GetJoyGuid(device)}   USB: {vendor:X4}:{product:X4}\n"
            + $"Usable: {available}\nHardware: {hardware}";
        _lightColor.Disabled = !Input.HasJoyLight(device);
    }

    public override void _Process(double delta)
    {
        if (_selectedPad < 0 || _tabs == null || _tabs.CurrentTab != 3 || !IsVisibleInTree()) return;
        for (int a = 0; a < _axisBars.Count; a++) _axisBars[a].Value = Input.GetJoyAxis(_selectedPad, (JoyAxis)a);
        for (int b = 0; b < _buttonLights.Count; b++)
            _buttonLights[b].AddThemeColorOverride("font_color", Input.IsJoyButtonPressed(_selectedPad, (JoyButton)b) ? Accent : DimColor);
    }

    private void SetStatus(string text)
    {
        if (_status != null) _status.Text = text;
    }
}
#endif
