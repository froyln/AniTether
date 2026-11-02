namespace AniTether.Api.Data;

/// <summary>UI colorways and light/dark modes. Mirrored by the palettes in
/// <c>web/src/index.css</c> and <c>web/public/theme.js</c> — change all three together.</summary>
public static class Themes
{
    public static readonly string[] Allowed = ["default", "crimson", "slate"];

    /// <summary>"system" follows the OS/browser setting.</summary>
    public static readonly string[] Modes = ["system", "dark", "light"];
}
