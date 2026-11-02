namespace AniTether.Api.Data;

/// <summary>Playback forms the in-page player can be switched to, as substituted into a
/// source's own template via the `{audio}` placeholder. Whichever form a source names in
/// its template it actually has.</summary>
public static class Audios
{
    public static readonly string[] Allowed = ["sub", "dub"];
}
