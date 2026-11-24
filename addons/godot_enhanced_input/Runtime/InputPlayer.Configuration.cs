namespace GodotEnhancedInput;

/// <summary>Loading an <see cref="InputConfiguration"/> and switching its contexts by id or name.</summary>
public partial class InputPlayer
{
    /// <summary>The configuration loaded with <see cref="LoadConfiguration"/>, or null.</summary>
    public InputConfiguration Configuration { get; private set; }

    /// <summary>
    /// Sets the player up from a configuration: its control schemes become the player's modes (starting in
    /// the default one), its contexts become known for rebinding and for <see cref="ActivateContext"/>, its
    /// default settings apply where the profile and mode set nothing, and its startup contexts are
    /// activated (unless <paramref name="activateStartupContexts"/> is off). Contexts already active stay.
    /// </summary>
    public void LoadConfiguration(InputConfiguration configuration, bool activateStartupContexts = true)
    {
        Configuration = configuration;
        if (configuration != null)
        {
            foreach (var context in configuration.Contexts)
                if (context != null) RegisterRebindContext(context);
            foreach (var context in configuration.StartupContexts)
                if (context != null) RegisterRebindContext(context);
            if (configuration.Modes.Count > 0)
            {
                Modes = new Godot.Collections.Array<InputMode>(configuration.Modes);
                ActiveMode = configuration.DefaultMode ?? configuration.Modes[0];
            }
            if (activateStartupContexts)
                foreach (var context in configuration.StartupContexts)
                    if (context != null) AddContext(context, context.DefaultPriority);
        }
        RebuildNameIndex();
    }

    /// <summary>
    /// A context by id, name or former id: an active one, else one of the loaded configuration, else one
    /// registered for rebinding. Null when unknown.
    /// </summary>
    public BindingContext FindContext(string idOrName)
    {
        foreach (var entry in _entries)
            if (entry.Context.Matches(idOrName)) return entry.Context;
        var found = Configuration?.FindContext(idOrName);
        if (found != null) return found;
        foreach (var context in _rebindContexts)
            if (context != null && context.Matches(idOrName)) return context;
        return null;
    }

    /// <summary>Activates a context by id or name (at its <see cref="BindingContext.DefaultPriority"/> unless given). False when unknown.</summary>
    public bool ActivateContext(string idOrName, int? priority = null, ContextOptions options = default)
    {
        var context = FindContext(idOrName);
        if (context == null) return false;
        AddContext(context, priority ?? context.DefaultPriority, options);
        return true;
    }

    /// <summary>Deactivates a context by id or name. False when it was not active.</summary>
    public bool DeactivateContext(string idOrName)
    {
        var context = FindContext(idOrName);
        return context != null && RemoveContext(context);
    }

    /// <summary>Activates a context by id or name until the handle is disposed (null when unknown).</summary>
    public ContextHandle PushContext(string idOrName, int? priority = null, ContextOptions options = default)
    {
        var context = FindContext(idOrName);
        return context == null ? null : PushContext(context, priority ?? context.DefaultPriority, options);
    }
}
