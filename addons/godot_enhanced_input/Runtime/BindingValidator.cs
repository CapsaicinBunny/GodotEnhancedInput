using System.Collections.Generic;

namespace GodotEnhancedInput;

/// <summary>How serious a problem found by <see cref="BindingValidator"/> is.</summary>
public enum IssueSeverity
{
    /// <summary>Worth a look; often intentional.</summary>
    Info,
    /// <summary>Probably a mistake.</summary>
    Warning,
    /// <summary>Cannot work as set up.</summary>
    Error,
}

/// <summary>One problem in a set of binding contexts.</summary>
public sealed class BindingIssue
{
    /// <summary>How serious it is.</summary>
    public IssueSeverity Severity { get; init; }
    /// <summary>What is wrong, for people.</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>The context it is in.</summary>
    public BindingContext Context { get; init; }
    /// <summary>The binding concerned, if any.</summary>
    public ActionBinding Binding { get; init; }
    /// <summary>The binding's index in its context (−1 when not about one binding).</summary>
    public int BindingIndex { get; init; } = -1;
    /// <summary>The action concerned, if any.</summary>
    public GameAction Action { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var where = Context == null ? string.Empty : BindingIndex >= 0 ? $"{Context.Name} #{BindingIndex}: " : $"{Context.Name}: ";
        return $"[{Severity}] {where}{Message}";
    }
}

/// <summary>
/// Looks for mistakes in binding contexts before the game runs: bindings without an action or input,
/// empty modifier and trigger slots, chords and combos that point nowhere, inputs that drive two
/// unrelated actions in one context, ambiguous rebind ids, unnamed or same-named actions, and actions
/// whose value kind cannot be filled by the input bound to them. Used by the editor dock; also handy
/// in a test that keeps a project's contexts clean.
/// </summary>
public static class BindingValidator
{
    /// <summary>Checks the given contexts together (chord actions may live in another context).</summary>
    public static List<BindingIssue> Validate(IEnumerable<BindingContext> contexts)
    {
        var issues = new List<BindingIssue>();
        var list = new List<BindingContext>();
        foreach (var context in contexts)
            if (context != null) list.Add(context);

        var bound = new HashSet<GameAction>();
        var actions = new List<GameAction>();
        foreach (var context in list)
            foreach (var binding in context.Bindings)
                if (binding?.Action != null && bound.Add(binding.Action)) actions.Add(binding.Action);

        foreach (var context in list) CheckContext(context, bound, issues);
        CheckActions(actions, bound, issues);
        return issues;
    }

    private static void CheckContext(BindingContext context, HashSet<GameAction> bound, List<BindingIssue> issues)
    {
        var bindings = context.Bindings;
        if (bindings.Count == 0)
            issues.Add(new BindingIssue { Severity = IssueSeverity.Info, Context = context, Message = "The context has no bindings." });

        var rebindIds = new Dictionary<string, int>();
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            void Add(IssueSeverity severity, string message)
                => issues.Add(new BindingIssue { Severity = severity, Context = context, Binding = binding, BindingIndex = i, Action = binding?.Action, Message = message });

            if (binding == null) { Add(IssueSeverity.Warning, "Empty binding slot."); continue; }
            if (binding.Action == null) Add(IssueSeverity.Error, "The binding has no action.");
            if (binding.Source == null || binding.Source.Kind == SourceKind.None)
                Add(binding.IsRebindable ? IssueSeverity.Info : IssueSeverity.Warning,
                    binding.IsRebindable ? "No default input (left for the player to choose)." : "The binding has no input.");

            CheckLists(binding.Modifiers, binding.Triggers, bound, Add);

            if (binding.Action != null && binding.Source != null && binding.Source.Kind != SourceKind.None)
            {
                int needed = Dimensions(binding.Action.ValueKind);
                int given = Dimensions(binding.Source);
                if (given < needed && !HasSwizzle(binding.Modifiers))
                    Add(IssueSeverity.Info, $"{Describe(binding.Source)} gives {given} axis value(s) but {binding.Action.Name} is {binding.Action.ValueKind}; only the first axis moves (add a swizzle to use another).");
            }

            if (binding.IsRebindable)
            {
                if (rebindIds.TryGetValue(binding.RebindId, out var first) && bindings[first]?.Action != binding.Action)
                    Add(IssueSeverity.Warning, $"Rebind id \"{binding.RebindId}\" is also used by binding #{first} for another action.");
                else rebindIds.TryAdd(binding.RebindId, i);
            }

            // Two plain bindings of one input to different actions in the same context both fire.
            for (int j = 0; j < i; j++)
            {
                var other = bindings[j];
                if (other?.Action == null || binding.Action == null || other.Source == null || binding.Source == null) continue;
                if (!binding.Source.Overlaps(other.Source)) continue;
                if (other.Action == binding.Action)
                {
                    if (InputSourceJson.SameInput(other.Source, binding.Source))
                        Add(IssueSeverity.Info, $"{Describe(binding.Source)} is bound to {binding.Action.Name} twice (also #{j}).");
                    continue;
                }
                if (IsPlain(binding) && IsPlain(other))
                    Add(IssueSeverity.Warning, $"{Describe(binding.Source)} drives both {binding.Action.Name} and {other.Action.Name} (#{j}) in this context.");
            }
        }
    }

    private static void CheckActions(List<GameAction> actions, HashSet<GameAction> bound, List<BindingIssue> issues)
    {
        var names = new Dictionary<string, GameAction>();
        foreach (var action in actions)
        {
            void Add(IssueSeverity severity, string message)
                => issues.Add(new BindingIssue { Severity = severity, Action = action, Message = message });

            if (string.IsNullOrEmpty(action.Name)) Add(IssueSeverity.Warning, "An action has no name (lookups by name, recordings and saved settings need one).");
            else if (names.TryGetValue(action.Name, out var other) && other != action) Add(IssueSeverity.Warning, $"Two different actions are named \"{action.Name}\".");
            else names[action.Name] = action;

            CheckLists(action.Modifiers, action.Triggers, bound, (severity, message) => Add(severity, $"{action.Name}: {message}"));
        }
    }

    private static void CheckLists(
        Godot.Collections.Array<InputModifier> modifiers,
        Godot.Collections.Array<InputTrigger> triggers,
        HashSet<GameAction> bound,
        System.Action<IssueSeverity, string> add)
    {
        if (modifiers != null)
            foreach (var modifier in modifiers)
                if (modifier == null) add(IssueSeverity.Warning, "Empty modifier slot.");
        if (triggers == null) return;
        foreach (var trigger in triggers)
        {
            switch (trigger)
            {
                case null:
                    add(IssueSeverity.Warning, "Empty trigger slot.");
                    break;
                case ChordTrigger chord when chord.ChordAction == null:
                    add(IssueSeverity.Error, "A chord trigger has no action to wait for.");
                    break;
                case ChordTrigger chord when !bound.Contains(chord.ChordAction):
                    add(IssueSeverity.Warning, $"Chord action {chord.ChordAction.Name} is not bound in any of these contexts.");
                    break;
                case ChordBlockerTrigger blocker when blocker.BlockingAction == null:
                    add(IssueSeverity.Error, "A chord blocker has no action.");
                    break;
                case ComboTrigger combo:
                    if (combo.Steps.Count == 0) add(IssueSeverity.Error, "A combo trigger has no steps.");
                    foreach (var step in combo.Steps)
                        if (step?.Action == null) add(IssueSeverity.Error, "A combo step has no action.");
                        else if (!bound.Contains(step.Action)) add(IssueSeverity.Warning, $"Combo step {step.Action.Name} is not bound in any of these contexts.");
                    break;
            }
        }
    }

    private static bool IsPlain(ActionBinding binding)
        => !HasAny(binding.Triggers) && !HasAny(binding.Action.Triggers);

    private static bool HasAny(Godot.Collections.Array<InputTrigger> triggers)
    {
        if (triggers == null) return false;
        foreach (var t in triggers)
            if (t != null) return true;
        return false;
    }

    private static bool HasSwizzle(Godot.Collections.Array<InputModifier> modifiers)
    {
        if (modifiers == null) return false;
        foreach (var m in modifiers)
            if (m is SwizzleModifier) return true;
        return false;
    }

    private static int Dimensions(ActionValueKind kind) => kind switch
    {
        ActionValueKind.Axis2D => 2,
        ActionValueKind.Axis3D => 3,
        _ => 1,
    };

    /// <summary>How many axes a source fills (1 for buttons and single axes).</summary>
    public static int Dimensions(InputSource source)
    {
        if (source == null) return 0;
        switch (source.Kind)
        {
            case SourceKind.KeyAxis2D:
            case SourceKind.MouseMotion:
            case SourceKind.MousePosition:
            case SourceKind.MouseWheel:
            case SourceKind.JoyStick:
                return 2;
            case SourceKind.Motion:
                return 3;
            case SourceKind.Composite:
            case SourceKind.Touch:
            case SourceKind.Gesture:
            case SourceKind.Virtual:
            case SourceKind.DeviceControl:
            case SourceKind.InputMapAction:
            case SourceKind.Pen:
                return 3;   // can carry any shape; not checked
            default:
                return 1;
        }
    }

    private static string Describe(InputSource source) => InputSourcePath.Format(source);
}
