using Microsoft.AspNetCore.Identity;

namespace AniTether.Api.Data;

public class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public bool ProfilePublic { get; set; }
    public string? ProfileBio { get; set; }
    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    /// <summary>Whitelist in <see cref="Languages"/>. Null means "no preference set" —
    /// the frontend falls back to the browser's language, then English.</summary>
    public string? PreferredLanguage { get; set; }

    /// <summary>Whitelist in <see cref="Audios"/>. Null means "no preference set" — the
    /// frontend default is subtitles.</summary>
    public string? PreferredAudio { get; set; }

    /// <summary>Whitelist in <see cref="Themes.Allowed"/>. Null means never chosen — the
    /// browser keeps whatever it has in localStorage.</summary>
    public string? Theme { get; set; }

    /// <summary>Whitelist in <see cref="Themes.Modes"/>. Null like <see cref="Theme"/>.</summary>
    public string? ColorMode { get; set; }
}
