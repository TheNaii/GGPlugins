namespace LoLAM.Core.Cloud;

public sealed class AuthSession
{
    public string Email { get; init; } = "";
    public string UserId { get; init; } = "";
    public string IdToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
}
