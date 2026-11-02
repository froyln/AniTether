namespace AniTether.Api.Data;

public static class Roles
{
    public const string Admin = "Admin";

    /// <summary>Can provision and manage role-less users only. Never grants a
    /// role — only an Admin can.</summary>
    public const string Manager = "Manager";

    public static readonly string[] All = [Admin, Manager];
}
