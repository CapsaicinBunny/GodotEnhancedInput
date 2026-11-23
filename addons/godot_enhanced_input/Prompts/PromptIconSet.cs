using System.Collections.Generic;
using Godot;

namespace GodotEnhancedInput;

/// <summary>
/// Maps prompt icon keys (see <see cref="InputPrompts.GetIconKey"/>) to textures. A key may be given
/// per style as <c>xbox/joy_a</c>, <c>playstation/joy_a</c>, <c>nintendo/joy_a</c>, <c>generic/joy_a</c>;
/// the style-specific entry is tried first, then the plain key. No artwork ships with the plugin:
/// fill a set with your own or a licensed icon pack.
/// </summary>
[Tool]
[GlobalClass]
public partial class PromptIconSet : Resource
{
    /// <summary>Icon key → texture.</summary>
    [Export] public Godot.Collections.Dictionary<string, Texture2D> Icons { get; set; } = new();

    /// <summary>Another set to ask when this one has no icon (e.g. a shared keyboard set).</summary>
    [Export] public PromptIconSet Fallback { get; set; }

    /// <summary>The icon for a key in a style, or null.</summary>
    public Texture2D GetIcon(string iconKey, JoypadStyle style = JoypadStyle.Generic)
    {
        if (string.IsNullOrEmpty(iconKey)) return null;
        var styled = $"{style.ToString().ToLowerInvariant()}/{iconKey}";
        if (Icons.TryGetValue(styled, out var texture) && texture != null) return texture;
        if (Icons.TryGetValue(iconKey, out texture) && texture != null) return texture;
        return Fallback?.GetIcon(iconKey, style);
    }

    /// <summary>The icon for a prompt, or null.</summary>
    public Texture2D GetIcon(ActionPrompt prompt) => GetIcon(prompt.IconKey, prompt.Style);

    /// <summary>
    /// BBCode for a <see cref="RichTextLabel"/>: the icon as an image (at <paramref name="height"/> pixels when
    /// given), or the prompt's text in bold when there is no icon or it has no file path.
    /// </summary>
    public string ToBBCode(ActionPrompt prompt, int height = 0)
    {
        var icon = GetIcon(prompt);
        if (icon != null && !string.IsNullOrEmpty(icon.ResourcePath))
            return height > 0 ? $"[img height={height}]{icon.ResourcePath}[/img]" : $"[img]{icon.ResourcePath}[/img]";
        return $"[b]{Escape(prompt.Text)}[/b]";
    }

    /// <summary>BBCode for several prompts joined with <paramref name="separator"/>.</summary>
    public string ToBBCode(IEnumerable<ActionPrompt> prompts, string separator = " / ", int height = 0)
    {
        var parts = new List<string>();
        foreach (var prompt in prompts) parts.Add(ToBBCode(prompt, height));
        return string.Join(separator, parts);
    }

    /// <summary>Plain text in BBCode: square brackets are escaped.</summary>
    public static string Escape(string text) => (text ?? string.Empty).Replace("[", "[lb]");
}
