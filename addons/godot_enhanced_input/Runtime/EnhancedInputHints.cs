namespace GodotEnhancedInput;

/// <summary>
/// Hint strings that make the editor show a dropdown of the project's actions or mapping contexts for a
/// string property holding an id — for scripts that store ids rather than resources:
/// <code>
/// [Export(PropertyHint.PlaceholderText, EnhancedInputHints.ActionId)] public string InteractAction { get; set; } = "interact";
/// </code>
/// Properties typed as <see cref="EnhancedInputAction"/>, <see cref="EnhancedInputMappingContext"/> or
/// <see cref="EnhancedInputConfig"/> get a dropdown of the project's files without any hint.
/// </summary>
public static class EnhancedInputHints
{
    /// <summary>A string property holding an action id.</summary>
    public const string ActionId = "enhanced_input:action_id";

    /// <summary>A string property holding a mapping context id.</summary>
    public const string ContextId = "enhanced_input:context_id";
}
