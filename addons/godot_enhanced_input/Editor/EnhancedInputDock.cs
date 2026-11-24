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
/// <item><b>Mappings</b>: every input mapping context in the project (or one config's), searchable, with
/// device icons; a side panel edits the selected mapping — action (or a new one), input path with live
/// validation, listen-to-assign, groups, rebind id, modifiers and triggers — all with undo, saved at
/// once. Mappings are dragged to reorder them or to move them to another mapping context; action files
/// (or rows of the Actions tab) dropped on a mapping set its action, on a mapping context add a mapping.</item>
/// <item><b>Actions</b>: every action file in the project with its value kind and where it is mapped.</item>
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
    private OptionButton _configFilter;
    private Button _generate;
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
    private readonly List<(string Path, EnhancedInputMappingContext Context)> _contexts = new();
    private readonly List<(string Path, EnhancedInputAction Action)> _actionFiles = new();
    private readonly List<(string Path, EnhancedInputConfig Config)> _configs = new();
    private EnhancedInputConfig _shownConfig;
    private bool _revalidateQueued;
    private readonly List<BindingIssue> _issues = new();
    private string _selectedPath;
    private int _selectedIndex = -1;
    private bool _groupSelected;
    private EnhancedInputAction _selectedGroupAction;
    private readonly HashSet<string> _collapsed = new();
    private bool _selfEdit;
    private int _selectedPad = -1;
    private bool _listening;
    private ulong _listenStartedMs;
    private bool _updatingDetail;

    /// <summary>Builds the dock's interface; called once by the plugin.</summary>
    public void Setup(EditorPlugin plugin)
    {
        _plugin = plugin;
        Name = "Enhanced Input";
        CustomMinimumSize = new Vector2(0, 260);
        _theme = EditorInterface.Singleton.GetEditorTheme();
        BuildUi();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        EditorInterface.Singleton.GetResourceFilesystem().FilesystemChanged += OnFilesystemChanged;
        EditorInterface.Singleton.GetInspector().PropertyEdited += OnInspectorPropertyEdited;
        if (_plugin != null) _plugin.ResourceSaved += OnResourceSaved;
        Refresh();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        if (fs != null) fs.FilesystemChanged -= OnFilesystemChanged;
        var inspector = EditorInterface.Singleton.GetInspector();
        if (inspector != null) inspector.PropertyEdited -= OnInspectorPropertyEdited;
        if (_plugin != null && IsInstanceValid(_plugin)) _plugin.ResourceSaved -= OnResourceSaved;
    }

    private static bool IsOurs(GodotObject o)
        => o is EnhancedInputAction or EnhancedInputMappingContext or EnhancedInputConfig or ActionBinding or InputSource or InputModifier or InputTrigger;

    // Edits made in the Inspector show up here at once.
    private void OnInspectorPropertyEdited(string property)
    {
        if (IsOurs(EditorInterface.Singleton.GetInspector().GetEditedObject())) QueueRevalidate();
    }

    private void OnResourceSaved(Resource resource)
    {
        if (resource is EnhancedInputAction action && _actionFiles.All(a => a.Action != action)
            || resource is EnhancedInputMappingContext context && _contexts.All(c => c.Context != context)
            || resource is EnhancedInputConfig config && _configs.All(c => c.Config != config))
            OnFilesystemChanged();
        else if (IsOurs(resource)) QueueRevalidate();
    }

    private void QueueRevalidate()
    {
        if (_revalidateQueued) return;
        _revalidateQueued = true;
        CallDeferred(MethodName.DeferredRevalidate);
    }

    private void DeferredRevalidate()
    {
        _revalidateQueued = false;
        Revalidate();
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

    private Label Badge(Color color)
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

        _configFilter = new OptionButton { TooltipText = "Show every mapping context, or only those of one input config." };
        _configFilter.ItemSelected += i =>
        {
            _shownConfig = i <= 0 ? null : _configFilter.GetItemMetadata((int)i).AsGodotObject() as EnhancedInputConfig;
            _generate.Visible = _shownConfig != null;
            RebuildBindings();
            RebuildActions();
        };
        toolbar.AddChild(_configFilter);
        _generate = ToolButton("CSharpScript", "Write a typed C# class for this config (input.Gameplay.Jump instead of strings).", () =>
        {
            if (_shownConfig != null) SetStatus(EnhancedInputInspector.WriteGeneratedClass(_shownConfig));
        }, "C#");
        _generate.Visible = false;
        toolbar.AddChild(_generate);

        toolbar.AddChild(ToolButton("Reload", "Rescan the project for actions, mapping contexts and configs.", Refresh));
        toolbar.AddChild(ToolButton("Add", "New input action…", NewAction, "Action"));
        toolbar.AddChild(ToolButton("Add", "New input mapping context…", NewContext, "Mapping Context"));
        _addBinding = ToolButton("Add", "Add a mapping to the selected mapping context.", () => AddBinding(), "Mapping");
        toolbar.AddChild(_addBinding);
        _removeBinding = ToolButton("Remove", "Remove the selected mapping.", RemoveBinding);
        _removeBinding.Disabled = true;
        toolbar.AddChild(_removeBinding);
        _listen = ToolButton("InputEventKey", "Assign the next key, button or stick movement to the selected mapping (Esc cancels).", StartListening, "Listen");
        _listen.Disabled = true;
        toolbar.AddChild(_listen);

        toolbar.AddChild(new VSeparator());
        var errorIcon = new TextureRect { Texture = Icon("StatusError"), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        toolbar.AddChild(errorIcon);
        _errorsBadge = Badge(ErrorColor);
        toolbar.AddChild(_errorsBadge);
        var warningIcon = new TextureRect { Texture = Icon("StatusWarning"), StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        toolbar.AddChild(warningIcon);
        _warningsBadge = Badge(WarningColor);
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
        var split = new HSplitContainer { Name = "Mappings", SplitOffsets = new[] { 520 } };

        _bindings = new Tree
        {
            Columns = 3, ColumnTitlesVisible = true, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            DropModeFlags = (int)(Tree.DropModeFlagsEnum.OnItem | Tree.DropModeFlagsEnum.Inbetween),
            TooltipText = "Mappings are grouped by action. Drag an input into another action's group to give it that action, or onto a mapping context to move it there; drag an action's row to move all its inputs. Drop an action file (or an Actions row) on a mapping to set its action, or on a mapping context or action to add a mapping.",
        };
        _bindings.SetDragForwarding(
            Callable.From<Vector2, Variant>(GetMappingDragData),
            Callable.From<Vector2, Variant, bool>(CanDropOnMappings),
            Callable.From<Vector2, Variant>(DropOnMappings));
        _bindings.SetColumnTitle(0, "Mapping context / action");
        _bindings.SetColumnTitle(1, "Input");
        _bindings.SetColumnTitle(2, "Modifiers · triggers · groups");
        _bindings.SetColumnExpandRatio(0, 2);
        _bindings.SetColumnExpandRatio(1, 2);
        _bindings.SetColumnExpandRatio(2, 3);
        _bindings.ItemSelected += OnBindingSelected;
        _bindings.ItemActivated += OnMappingActivated;
        _bindings.ItemCollapsed += item =>
        {
            var key = CollapseKey(item);
            if (key == null) return;
            if (item.Collapsed) _collapsed.Add(key); else _collapsed.Remove(key);
        };
        split.AddChild(_bindings);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_detail);
        split.AddChild(scroll);

        _detailTitle = new Label { Text = "Select a mapping", AutowrapMode = TextServer.AutowrapMode.WordSmart };
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

        var inspect = new Button { Text = "Open mapping in the Inspector", Icon = Icon("Edit") };
        inspect.Pressed += () => EditInInspector(SelectedBinding());
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
        _actions.SetColumnTitle(2, "Mappings");
        _actions.SetColumnTitle(3, "Mapping contexts");
        _actions.TooltipText = "Double-click to open an action in the Inspector; drag it onto a mapping or a mapping context.";
        _actions.ItemActivated += () =>
        {
            if (_actions.GetSelected()?.GetMetadata(0).AsGodotObject() is EnhancedInputAction action) EditInInspector(action);
        };
        _actions.SetDragForwarding(
            Callable.From<Vector2, Variant>(at =>
            {
                if (_actions.GetItemAtPosition(at)?.GetMetadata(0).AsGodotObject() is not EnhancedInputAction action) return default;
                _actions.SetDragPreview(new Label { Text = action.Name });
                return new Godot.Collections.Dictionary { ["type"] = "gei_action", ["action"] = action };
            }),
            Callable.From<Vector2, Variant, bool>((_, _) => false),
            Callable.From<Vector2, Variant>((_, _) => { }));
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
        _actionFiles.Clear();
        _configs.Clear();
        var files = new List<(string Path, string Class)>();
        var fs = EditorInterface.Singleton.GetResourceFilesystem();
        Scan(fs.GetFilesystem(), files);
        foreach (var (path, _) in files.OrderBy(f => f.Path))
        {
            var resource = ResourceLoader.Load(path);
            if (resource is EnhancedInputMappingContext context) _contexts.Add((path, context));
            else if (resource is EnhancedInputAction action) _actionFiles.Add((path, action));
            else if (resource is EnhancedInputConfig config) _configs.Add((path, config));
        }
        if (_shownConfig != null && _configs.All(c => c.Config != _shownConfig)) _shownConfig = null;
        RebuildConfigFilter();
        Revalidate();
        RefreshPads();
        SetStatus($"{_contexts.Count} mapping context(s), {AllActions().Count} action(s), {_configs.Count} config(s).");
    }

    private void RebuildConfigFilter()
    {
        _configFilter.Clear();
        _configFilter.AddItem("All mapping contexts");
        foreach (var (path, config) in _configs)
        {
            int i = _configFilter.ItemCount;
            _configFilter.AddItem($"Config: {(string.IsNullOrEmpty(config.Name) ? path.GetFile().GetBaseName() : config.Name)}");
            _configFilter.SetItemMetadata(i, config);
            _configFilter.SetItemTooltip(i, path);
            if (config == _shownConfig) _configFilter.Selected = i;
        }
        if (_shownConfig == null) _configFilter.Selected = 0;
        _configFilter.Visible = _configs.Count > 0;
        if (_generate != null) _generate.Visible = _shownConfig != null;
    }

    /// <summary>The mapping contexts shown: all, or those of the chosen config.</summary>
    private IEnumerable<(string Path, EnhancedInputMappingContext Context)> ShownContexts()
        => _shownConfig == null ? _contexts : _contexts.Where(c => _shownConfig.Contexts.Contains(c.Context) || _shownConfig.StartupContexts.Contains(c.Context));

    /// <summary>Every action: the action files, and actions only found inside mapping contexts.</summary>
    private List<EnhancedInputAction> AllActions()
    {
        var list = _actionFiles.Select(a => a.Action).ToList();
        foreach (var action in BindingEditing.ActionsIn(_contexts.Select(c => c.Context)))
            if (!list.Contains(action)) list.Add(action);
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private EnhancedInputMappingContext ContextAt(string path) => _contexts.FirstOrDefault(c => c.Path == path).Context;

    /// <summary>
    /// Shows an asset in the dock (double-clicked in the FileSystem dock, or opened in the Inspector):
    /// a mapping context or mapping is selected in the Mappings tab, an action in the Actions tab, a
    /// config becomes the filter. Returns false for objects the dock does not show.
    /// </summary>
    public bool Reveal(GodotObject target)
    {
        if (_selfEdit) return false;
        switch (target)
        {
            case EnhancedInputMappingContext context:
            {
                if (_contexts.All(c => c.Context != context)) Refresh();
                var entry = _contexts.FirstOrDefault(c => c.Context == context);
                if (entry.Context == null) return false;
                if (_shownConfig != null && !ShownContexts().Any(c => c.Context == context)) _shownConfig = null;
                SelectMapping(entry.Path, -1);
                return true;
            }
            case ActionBinding binding:
            {
                var entry = _contexts.FirstOrDefault(c => c.Context.Bindings.Contains(binding));
                if (entry.Context == null) return false;
                if (_shownConfig != null && !ShownContexts().Any(c => c.Context == entry.Context)) _shownConfig = null;
                SelectMapping(entry.Path, entry.Context.Bindings.IndexOf(binding));
                return true;
            }
            case EnhancedInputAction action:
            {
                if (!AllActions().Contains(action)) Refresh();
                _tabs.CurrentTab = 1;
                RebuildActions();
                for (var item = _actions.GetRoot()?.GetFirstChild(); item != null; item = item.GetNext())
                    if (item.GetMetadata(0).AsGodotObject() == action) { item.Select(0); _actions.ScrollToItem(item); }
                return true;
            }
            case EnhancedInputConfig config:
            {
                if (_configs.All(c => c.Config != config)) Refresh();
                if (_configs.All(c => c.Config != config)) return false;
                _shownConfig = config;
                RebuildConfigFilter();
                _tabs.CurrentTab = 0;
                RebuildBindings();
                RebuildActions();
                return true;
            }
            default:
                return false;
        }
    }

    private void SelectMapping(string path, int index)
    {
        RebuildConfigFilter();
        _selectedPath = path;
        _selectedIndex = index;
        _groupSelected = false;
        _search.Text = string.Empty;
        _tabs.CurrentTab = 0;
        RebuildBindings();
        if (index < 0)
            for (var item = _bindings.GetRoot()?.GetFirstChild(); item != null; item = item.GetNext())
                if (item.GetMetadata(0).AsString() == path)
                {
                    _updatingDetail = true;
                    item.Select(0);
                    _updatingDetail = false;
                    _bindings.ScrollToItem(item);
                }
        UpdateDetail();
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

    private static readonly string[] AssetClasses = { nameof(EnhancedInputMappingContext), nameof(EnhancedInputAction), nameof(EnhancedInputConfig) };

    private static void Scan(EditorFileSystemDirectory dir, List<(string Path, string Class)> files)
    {
        if (dir == null) return;
        for (int i = 0; i < dir.GetFileCount(); i++)
        {
            var cls = AssetClassOf(dir.GetFilePath(i));
            if (cls != null) files.Add((dir.GetFilePath(i), cls));
        }
        for (int i = 0; i < dir.GetSubdirCount(); i++) Scan(dir.GetSubdir(i), files);
    }

    /// <summary>
    /// Which of the three assets a text resource is, from the script class its header names (the
    /// editor's file index only knows script classes of script files, not of resources); null if none.
    /// </summary>
    private static string AssetClassOf(string path)
    {
        if (!path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)) return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return null;
        var header = file.GetLine();
        foreach (var cls in AssetClasses)
            if (header.Contains($"script_class=\"{cls}\"", StringComparison.Ordinal)) return cls;
        return null;
    }

    // Rows of the Mappings tree. Column 0 metadata: the mapping context's path; column 1: what the row
    // is (RowContext, RowGroup, or the mapping's index); column 2 (groups): the action.
    private const int RowContext = -1;
    private const int RowGroup = -2;

    private static int RowKind(TreeItem item) => item == null ? RowContext : item.GetMetadata(1).AsInt32();

    private static string GroupKey(string path, EnhancedInputAction action) => $"{path}|{(action == null ? "none" : action.GetInstanceId().ToString())}";

    private string CollapseKey(TreeItem item) => RowKind(item) switch
    {
        RowContext => "ctx|" + item.GetMetadata(0).AsString(),
        RowGroup => GroupKey(item.GetMetadata(0).AsString(), item.GetMetadata(2).AsGodotObject() as EnhancedInputAction),
        _ => null,
    };

    /// <summary>
    /// Builds the Mappings tree: each mapping context, then one row per action (in the order the action
    /// first appears) with that action's mappings — its inputs — under it.
    /// </summary>
    private void RebuildBindings()
    {
        _bindings.Clear();
        var root = _bindings.CreateItem();
        TreeItem reselect = null;
        var contextIcon = AssetIcon(ContextIconPath, "Object");
        var actionIcon = AssetIcon(ActionIconPath, "InputEventAction");
        foreach (var (path, context) in ShownContexts())
        {
            var contextItem = _bindings.CreateItem(root);
            contextItem.SetText(0, context.Name);
            contextItem.SetIcon(0, contextIcon);
            contextItem.SetTooltipText(0, path);
            contextItem.SetText(1, path);
            contextItem.SetCustomColor(1, DimColor);
            contextItem.SetMetadata(0, path);
            contextItem.SetMetadata(1, RowContext);
            if (context.BlockLowerPriority) contextItem.SetText(2, "blocks lower contexts");
            contextItem.Collapsed = _collapsed.Contains(CollapseKey(contextItem));
            if (path == _selectedPath && _selectedIndex < 0 && !_groupSelected) reselect = contextItem;

            var bindings = context.Bindings;
            var groups = new List<(EnhancedInputAction Action, List<int> Indices)>();
            for (int i = 0; i < bindings.Count; i++)
            {
                if (!BindingEditing.Matches(bindings[i], _search.Text)) continue;
                var action = bindings[i]?.Action;
                int g = groups.FindIndex(x => x.Action == action);
                if (g < 0) groups.Add((action, new List<int> { i }));
                else groups[g].Indices.Add(i);
            }
            if (groups.Count == 0 && !string.IsNullOrWhiteSpace(_search.Text)) { contextItem.Free(); continue; }

            foreach (var (action, indices) in groups)
            {
                var group = _bindings.CreateItem(contextItem);
                group.SetText(0, action?.Name ?? "(no action)");
                group.SetIcon(0, actionIcon);
                group.SetText(1, indices.Count == 1 ? "1 input" : $"{indices.Count} inputs");
                group.SetCustomColor(1, DimColor);
                group.SetText(2, action == null ? "drop an action here" : action.ValueKind.ToString());
                group.SetCustomColor(2, DimColor);
                group.SetTooltipText(0, action == null ? "Mappings without an action" : $"{action.Name} ({action.ValueKind}). Double-click to open the action; drop a mapping here to give it this action.");
                group.SetMetadata(0, path);
                group.SetMetadata(1, RowGroup);
                group.SetMetadata(2, action);
                group.Collapsed = _collapsed.Contains(CollapseKey(group));
                if (path == _selectedPath && _groupSelected && _selectedIndex < 0 && action == _selectedGroupAction) reselect = group;
                var groupWorst = IssueSeverity.Info;

                foreach (int i in indices)
                {
                    var binding = bindings[i];
                    var item = _bindings.CreateItem(group);
                    item.SetText(0, binding?.Source == null ? "(no input)" : InputPrompts.GetName(binding.Source));
                    item.SetCustomColor(0, DimColor);
                    item.SetText(1, BindingEditing.PathOf(binding?.Source));
                    item.SetIcon(1, IconFor(BindingEditing.DeviceKindOf(binding?.Source)));
                    item.SetTooltipText(1, binding?.Source == null ? "no input" : InputPrompts.GetName(binding.Source));
                    item.SetText(2, BindingEditing.Summary(binding));
                    item.SetCustomColor(2, DimColor);
                    item.SetMetadata(0, path);
                    item.SetMetadata(1, i);
                    var mine = _issues.Where(x => x.Context == context && x.BindingIndex == i).ToList();
                    var worst = mine.Select(x => x.Severity).DefaultIfEmpty(IssueSeverity.Info).Max();
                    if (worst >= IssueSeverity.Warning)
                    {
                        item.SetCustomColor(0, worst == IssueSeverity.Error ? ErrorColor : WarningColor);
                        item.SetTooltipText(0, string.Join("\n", mine.Select(x => x.Message)));
                        if (worst > groupWorst) groupWorst = worst;
                    }
                    if (path == _selectedPath && i == _selectedIndex) reselect = item;
                }
                if (groupWorst >= IssueSeverity.Warning) group.SetCustomColor(0, groupWorst == IssueSeverity.Error ? ErrorColor : WarningColor);
            }
        }
        if (reselect != null)
        {
            _updatingDetail = true;
            for (var parent = reselect.GetParent(); parent != null; parent = parent.GetParent()) parent.Collapsed = false;
            reselect.Select(0);
            _bindings.ScrollToItem(reselect);
            _updatingDetail = false;
        }
        UpdateDetail();
    }

    private const string ActionIconPath = "res://addons/godot_enhanced_input/Icons/EnhancedInputAction.svg";
    private const string ContextIconPath = "res://addons/godot_enhanced_input/Icons/EnhancedInputMappingContext.svg";

    private Texture2D AssetIcon(string path, string fallback)
        => ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : Icon(fallback);

    private void RebuildActions()
    {
        _actions.Clear();
        var root = _actions.CreateItem();
        var shown = ShownContexts().ToList();
        var actions = AllActions();
        if (_shownConfig != null)
            actions = actions.Where(a => _shownConfig.Actions.Contains(a) || shown.Any(c => c.Context.Bindings.Any(b => b?.Action == a))).ToList();
        var icon = AssetIcon(ActionIconPath, "InputEventAction");
        foreach (var action in actions)
        {
            var where = shown.Where(c => c.Context.Bindings.Any(b => b?.Action == action)).Select(c => c.Context.Name).ToList();
            int count = shown.Sum(c => c.Context.Bindings.Count(b => b?.Action == action));
            var item = _actions.CreateItem(root);
            item.SetText(0, action.Name);
            item.SetIcon(0, icon);
            item.SetTooltipText(0, string.IsNullOrEmpty(action.ResourcePath) || action.ResourcePath.Contains("::") ? "(inside a mapping context)" : action.ResourcePath);
            item.SetText(1, action.ValueKind.ToString());
            item.SetText(2, count.ToString());
            item.SetText(3, where.Count == 0 ? "not mapped yet: drag it onto a mapping context" : string.Join(", ", where));
            if (where.Count == 0) item.SetCustomColor(3, DimColor);
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

    private EnhancedInputMappingContext SelectedContext() => _contexts.FirstOrDefault(c => c.Path == _selectedPath).Context;

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
        int kind = RowKind(item);
        _groupSelected = item != null && kind == RowGroup;
        _selectedGroupAction = _groupSelected ? item.GetMetadata(2).AsGodotObject() as EnhancedInputAction : null;
        _selectedIndex = kind >= 0 ? kind : -1;
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
                _detailTitle.Text = context == null ? "Select a mapping"
                    : _groupSelected ? $"{context.Name} · {_selectedGroupAction?.Name ?? "(no action)"}: {context.Bindings.Count(b => b?.Action == _selectedGroupAction)} input(s). + Mapping adds another input for it; double-click opens the action."
                    : $"{context.Name}: select a mapping, or add one (+ Mapping)";
                _pathEdit.Text = _groupsEdit.Text = _rebindEdit.Text = string.Empty;
                return;
            }
            _detailTitle.Text = $"{context.Name} · #{_selectedIndex} · {binding.Action?.Name ?? "(no action)"}";
            var actions = AllActions();
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
        if (item != null) EditInInspector(item);
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
        var action = index <= 0 ? null : _actionPicker.GetItemMetadata((int)index).AsGodotObject() as EnhancedInputAction;
        Change("Set binding action", binding, ActionBinding.PropertyName.Action, action);
    }

    private void CreateAction()
    {
        var binding = SelectedBinding();
        var name = _newActionName.Text.Trim();
        if (binding == null || string.IsNullOrEmpty(name)) { SetStatus("Type a name for the new action."); return; }
        var kind = (ActionValueKind)_newActionKind.GetSelectedId();
        var action = EnhancedInputAction.Create(name, kind);
        var folder = _selectedPath.GetBaseDir();
        var path = $"{folder}/{name.ToLowerInvariant().Replace(' ', '_')}.tres";
        if (FileAccess.FileExists(path)) { SetStatus($"{path} already exists."); return; }
        var error = ResourceSaver.Save(action, path);
        if (error != Error.Ok) { SetStatus($"Could not save {path}: {error}"); return; }
        var saved = ResourceLoader.Load<EnhancedInputAction>(path);
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
        var source = BindingEditing.KeepSettings(BindingEditing.TryParsePath(text, out var error), binding.Source);
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

    private void AddBinding(EnhancedInputAction action = null)
    {
        var context = SelectedContext();
        if (context == null) { SetStatus("Select a mapping context first."); return; }
        var chosen = action ?? SelectedBinding()?.Action ?? (_groupSelected ? _selectedGroupAction : null);
        var list = new Godot.Collections.Array<ActionBinding>(context.Bindings);
        int at = InsertAfterGroup(list, chosen);
        list.Insert(at, ActionBinding.Create(chosen, null));
        _selectedIndex = at;
        _groupSelected = false;
        Change("Add mapping", context, EnhancedInputMappingContext.PropertyName.Bindings, list);
    }

    /// <summary>Where a new mapping for an action goes: after that action's last mapping, else at the end.</summary>
    private static int InsertAfterGroup(Godot.Collections.Array<ActionBinding> list, EnhancedInputAction action)
    {
        if (action == null) return list.Count;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i]?.Action == action) return i + 1;
        return list.Count;
    }

    private void EditInInspector(Resource resource)
    {
        if (resource == null) return;
        _selfEdit = true;
        try { EditorInterface.Singleton.EditResource(resource); }
        finally { _selfEdit = false; }
    }

    private void OnMappingActivated()
    {
        var item = _bindings.GetSelected();
        if (item == null) return;
        switch (RowKind(item))
        {
            case RowContext: EditInInspector(ContextAt(item.GetMetadata(0).AsString())); break;
            case RowGroup: EditInInspector(item.GetMetadata(2).AsGodotObject() as EnhancedInputAction); break;
            default: StartListening(); break;
        }
    }

    // ------------------------------------------------------------------
    //  Drag and drop
    // ------------------------------------------------------------------

    private Variant GetMappingDragData(Vector2 at)
    {
        var item = _bindings.GetItemAtPosition(at);
        if (item == null || RowKind(item) == RowContext) return default;
        var path = item.GetMetadata(0).AsString();
        var context = ContextAt(path);
        if (context == null) return default;
        int[] indices;
        bool group = RowKind(item) == RowGroup;
        if (group)
        {
            var action = item.GetMetadata(2).AsGodotObject() as EnhancedInputAction;
            indices = Enumerable.Range(0, context.Bindings.Count).Where(i => context.Bindings[i]?.Action == action).ToArray();
            _bindings.SetDragPreview(new Label { Text = $"{item.GetText(0)}  ({indices.Length} inputs)" });
        }
        else
        {
            indices = new[] { RowKind(item) };
            _bindings.SetDragPreview(new Label { Text = $"{item.GetParent()?.GetText(0)}   {item.GetText(1)}" });
        }
        return new Godot.Collections.Dictionary { ["type"] = "gei_mappings", ["path"] = path, ["indices"] = indices, ["group"] = group };
    }

    /// <summary>The actions a drop carries: an Actions row, a dragged resource, or action files.</summary>
    private static List<EnhancedInputAction> DroppedActions(Godot.Collections.Dictionary data)
    {
        var actions = new List<EnhancedInputAction>();
        var type = data.TryGetValue("type", out var t) ? t.AsString() : string.Empty;
        if (type == "gei_action" && data["action"].AsGodotObject() is EnhancedInputAction a) actions.Add(a);
        else if (type == "resource" && data["resource"].AsGodotObject() is EnhancedInputAction r) actions.Add(r);
        else if (type == "files")
            foreach (var path in data["files"].AsStringArray())
                if (AssetClassOf(path) == nameof(EnhancedInputAction) && ResourceLoader.Load(path) is EnhancedInputAction f) actions.Add(f);
        return actions;
    }

    private bool CanDropOnMappings(Vector2 at, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary || _bindings.GetItemAtPosition(at) == null) return false;
        var dict = data.AsGodotDictionary();
        return dict.TryGetValue("type", out var t) && t.AsString() == "gei_mappings" || DroppedActions(dict).Count > 0;
    }

    private void DropOnMappings(Vector2 at, Variant data)
    {
        var target = _bindings.GetItemAtPosition(at);
        if (target == null || data.VariantType != Variant.Type.Dictionary) return;
        var dict = data.AsGodotDictionary();
        if (dict.TryGetValue("type", out var t) && t.AsString() == "gei_mappings")
        {
            MoveMappings(dict["path"].AsString(), dict["indices"].AsInt32Array(), target, _bindings.GetDropSectionAtPosition(at), joinGroup: !dict["group"].AsBool());
            return;
        }
        var actions = DroppedActions(dict);
        if (actions.Count == 0) return;
        string targetPath = target.GetMetadata(0).AsString();
        var context = ContextAt(targetPath);
        if (context == null) return;
        _selectedPath = targetPath;
        _groupSelected = false;
        if (RowKind(target) >= 0)
        {
            _selectedIndex = RowKind(target);
            var binding = SelectedBinding();
            if (binding != null) Change("Set mapping action", binding, ActionBinding.PropertyName.Action, actions[0]);
            SetStatus($"The mapping now triggers {actions[0].Name}.");
        }
        else
        {
            var list = new Godot.Collections.Array<ActionBinding>(context.Bindings);
            foreach (var action in actions)
            {
                _selectedIndex = InsertAfterGroup(list, action);
                list.Insert(_selectedIndex, ActionBinding.Create(action, null));
            }
            Change(actions.Count == 1 ? "Add mapping" : "Add mappings", context, EnhancedInputMappingContext.PropertyName.Bindings, list);
            SetStatus($"Added {actions.Count} mapping(s) to {context.Name}; set the input with Listen or a path.");
        }
    }

    /// <summary>
    /// Moves mappings (one, or an action's whole group) to where they were dropped: onto a mapping
    /// context (its end), onto or next to an action's group, or before / after a mapping — in the same
    /// mapping context or another. A single mapping dropped into another action's group takes that
    /// action (that is the group it then shows in); a dragged group keeps its action and only moves.
    /// One undo step; both files are saved.
    /// </summary>
    private void MoveMappings(string fromPath, int[] indices, TreeItem target, int section, bool joinGroup)
    {
        var from = ContextAt(fromPath);
        string toPath = target.GetMetadata(0).AsString();
        var to = ContextAt(toPath);
        if (from == null || to == null) return;
        var moving = indices.Where(i => i >= 0 && i < from.Bindings.Count).Select(i => from.Bindings[i]).ToList();
        if (moving.Count == 0) return;

        int kind = RowKind(target);
        var targetBinding = kind >= 0 && kind < to.Bindings.Count ? to.Bindings[kind] : null;
        if (targetBinding != null && moving.Contains(targetBinding)) return;
        var targetAction = kind == RowGroup ? target.GetMetadata(2).AsGodotObject() as EnhancedInputAction : targetBinding?.Action;

        var source = new Godot.Collections.Array<ActionBinding>(from.Bindings);
        foreach (var m in moving) source.Remove(m);
        var dest = from == to ? source : new Godot.Collections.Array<ActionBinding>(to.Bindings);
        int insert;
        if (kind == RowContext) insert = dest.Count;
        else if (targetBinding != null && joinGroup) insert = dest.IndexOf(targetBinding) + (section < 0 ? 0 : 1);
        else
        {
            var members = Enumerable.Range(0, dest.Count).Where(i => dest[i]?.Action == targetAction).ToList();
            insert = members.Count == 0 ? dest.Count : section < 0 ? members[0] : members[^1] + 1;
        }
        insert = Math.Clamp(insert, 0, dest.Count);
        for (int k = 0; k < moving.Count; k++) dest.Insert(insert + k, moving[k]);

        bool retarget = joinGroup && kind != RowContext && moving.Any(m => m?.Action != targetAction);
        if (from == to && !retarget && dest.SequenceEqual(from.Bindings)) return;

        var undo = _plugin.GetUndoRedo();
        string what = moving.Count == 1 ? "mapping" : $"{moving[0]?.Action?.Name ?? "unassigned"} mappings";
        undo.CreateAction(from == to ? (retarget ? $"Move {what} to {targetAction?.Name ?? "no action"}" : $"Reorder {what}") : $"Move {what} to {to.Name}");
        undo.AddDoProperty(from, EnhancedInputMappingContext.PropertyName.Bindings, source);
        undo.AddUndoProperty(from, EnhancedInputMappingContext.PropertyName.Bindings, from.Bindings);
        if (from != to)
        {
            undo.AddDoProperty(to, EnhancedInputMappingContext.PropertyName.Bindings, dest);
            undo.AddUndoProperty(to, EnhancedInputMappingContext.PropertyName.Bindings, to.Bindings);
        }
        if (retarget)
            foreach (var m in moving)
                if (m != null && m.Action != targetAction)
                {
                    undo.AddDoProperty(m, ActionBinding.PropertyName.Action, targetAction);
                    undo.AddUndoProperty(m, ActionBinding.PropertyName.Action, m.Action);
                }
        if (from != to)
        {
            undo.AddDoMethod(this, MethodName.SaveContext, toPath);
            undo.AddUndoMethod(this, MethodName.SaveContext, toPath);
        }
        undo.AddDoMethod(this, MethodName.SaveContext, fromPath);
        undo.AddUndoMethod(this, MethodName.SaveContext, fromPath);

        _selectedPath = toPath;
        _groupSelected = !joinGroup;
        _selectedGroupAction = joinGroup ? null : moving[0]?.Action;
        _selectedIndex = joinGroup ? insert : -1;
        undo.CommitAction();
        SetStatus(retarget ? $"Mapping now triggers {targetAction?.Name ?? "nothing"}." : from == to ? "Moved." : $"Moved to {to.Name}.");
    }

    private void RemoveBinding()
    {
        var context = SelectedContext();
        if (context == null || _selectedIndex < 0 || _selectedIndex >= context.Bindings.Count) return;
        var list = new Godot.Collections.Array<ActionBinding>(context.Bindings);
        list.RemoveAt(_selectedIndex);
        Change("Remove mapping", context, EnhancedInputMappingContext.PropertyName.Bindings, list);
        _selectedIndex = Math.Min(_selectedIndex, list.Count - 1);
    }

    private void NewContext()
    {
        var dialog = new EditorFileDialog
        {
            FileMode = EditorFileDialog.FileModeEnum.SaveFile,
            Access = EditorFileDialog.AccessEnum.Resources,
            Title = "New input mapping context",
            CurrentFile = "new_mapping_context.tres",
        };
        dialog.AddFilter("*.tres", "Input mapping context");
        dialog.FileSelected += path =>
        {
            var context = EnhancedInputMappingContext.Create(path.GetFile().GetBaseName());
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

    private void NewAction()
    {
        var dialog = new EditorFileDialog
        {
            FileMode = EditorFileDialog.FileModeEnum.SaveFile,
            Access = EditorFileDialog.AccessEnum.Resources,
            Title = "New input action",
            CurrentFile = "new_action.tres",
        };
        if (!string.IsNullOrEmpty(_selectedPath)) dialog.CurrentDir = _selectedPath.GetBaseDir();
        dialog.AddFilter("*.tres", "Input action");
        dialog.FileSelected += path =>
        {
            var action = EnhancedInputAction.Create(path.GetFile().GetBaseName(), ActionValueKind.Digital);
            var error = ResourceSaver.Save(action, path);
            SetStatus(error == Error.Ok ? $"Created {path}. Drag it from the Actions tab onto a mapping context." : $"Could not save {path}: {error}");
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);
            Refresh();
            if (error == Error.Ok && ResourceLoader.Load(path) is EnhancedInputAction saved)
            {
                Reveal(saved);
                EditorInterface.Singleton.EditResource(saved);
            }
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

    /// <inheritdoc/>
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
        if (binding == null) { StopListening("The mapping is gone."); return; }
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

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        (_plugin as EnhancedInputPlugin)?.EnsureInspector();
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
