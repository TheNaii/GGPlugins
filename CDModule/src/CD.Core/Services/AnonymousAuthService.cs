using System.Text;
using System.Text.Json;
using CD.Core.Models;

namespace CD.Core.Services;

/// <summary>
/// Firebase Anonymous Authentication via REST API.
/// No email/password needed — just get a temporary token for Firestore access.
/// </summary>
public sealed class AnonymousAuthService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private const string ApiKey = "AIzaSyDeAnFmCEZSejefChCer1V42TsMBSzFJPI";
    private const string SignUpUrl = "https://identitytoolkit.googleapis.com/v1/accounts:signUp";
    private const string RefreshUrl = "https://securetoken.googleapis.com/v1/token";

    /// <summary>
    /// Signs in anonymously. Returns a session with userId and idToken.
    /// </summary>
    public async Task<UserSession> SignInAnonymouslyAsync(CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { returnSecureToken = true });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"{SignUpUrl}?key={ApiKey}", content, ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        return new UserSession
        {
            UserId = doc.RootElement.GetProperty("localId").GetString() ?? "",
            IdToken = doc.RootElement.GetProperty("idToken").GetString() ?? "",
            RefreshToken = doc.RootElement.GetProperty("refreshToken").GetString() ?? ""
        };
    }

    /// <summary>
    /// Refreshes an expired ID token using the refresh token.
    /// </summary>
    public async Task<UserSession> RefreshAsync(UserSession session, CancellationToken ct = default)
    {
        var payload = $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(session.RefreshToken)}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await Http.PostAsync($"{RefreshUrl}?key={ApiKey}", content, ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        session.IdToken = doc.RootElement.GetProperty("id_token").GetString() ?? "";
        session.RefreshToken = doc.RootElement.GetProperty("refresh_token").GetString() ?? session.RefreshToken;
        session.UserId = doc.RootElement.GetProperty("user_id").GetString() ?? session.UserId;

        return session;
    }
}
