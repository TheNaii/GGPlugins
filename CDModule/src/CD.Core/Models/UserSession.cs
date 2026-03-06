namespace CD.Core.Models;

/// <summary>
/// Represents a local user session (anonymous Firebase auth).
/// </summary>
public sealed class UserSession
{
    public string UserId { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "fill";
}
