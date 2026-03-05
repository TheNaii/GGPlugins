using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Auth;

namespace LoLAM.Core.Cloud;

public sealed class FirebaseAuthService : IAuthService
{
    private readonly FirebaseOptions _opts;
    private readonly FirebaseAuthProvider _provider;
    private readonly IAuthSessionStore _sessionStore;

    public FirebaseAuthService(FirebaseOptions opts, IAuthSessionStore sessionStore)
    {
        _opts = opts;
        _sessionStore = sessionStore;
        _provider = new FirebaseAuthProvider(new FirebaseConfig(_opts.ApiKey));
    }

    public async Task<AuthSession> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _provider.CreateUserWithEmailAndPasswordAsync(email, password);
        return await LoginAsync(email, password, ct);
    }

    public async Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var link = await _provider.SignInWithEmailAndPasswordAsync(email, password);
        link = await link.GetFreshAuthAsync();

        var session = new AuthSession
        {
            Email = email,
            UserId = link.User.LocalId,
            IdToken = link.FirebaseToken,
            RefreshToken = link.RefreshToken
        };

        _sessionStore.Save(session);
        return session;
    }

    public async Task<AuthSession?> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var cached = _sessionStore.Load();
        LogAuth($"TryRestore: cached={cached is not null}, hasRefresh={!string.IsNullOrWhiteSpace(cached?.RefreshToken)}");
        if (cached is null || string.IsNullOrWhiteSpace(cached.RefreshToken))
            return null;

        try
        {
            LogAuth($"TryRestore: refreshing for {cached.Email}...");
            // Refresh the session using the stored refresh token.
            var auth = new FirebaseAuth { RefreshToken = cached.RefreshToken };
            var refreshed = await _provider.RefreshAuthAsync(auth);

            LogAuth($"TryRestore: refreshed user={refreshed?.User is not null}, token={!string.IsNullOrWhiteSpace(refreshed?.FirebaseToken)}");
            if (string.IsNullOrWhiteSpace(refreshed?.FirebaseToken))
                return null;

            var session = new AuthSession
            {
                Email = refreshed.User?.Email ?? cached.Email ?? "",
                UserId = refreshed.User?.LocalId ?? cached.UserId ?? "",
                IdToken = refreshed.FirebaseToken,
                RefreshToken = refreshed.RefreshToken ?? cached.RefreshToken
            };

            // Persist updated refresh token (Firebase can rotate them).
            _sessionStore.Save(session);

            return session;
        }
        catch (Exception ex)
        {
            LogAuth($"TryRestore FAILED: {ex.Message}");
            return null;
        }
    }

    private static void LogAuth(string msg)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GGLauncherDev", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "firebase-auth-debug.log"),
                $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Clears the persisted session so the user will not be auto-restored.</summary>
    public void ClearSession() => _sessionStore.Clear();

    public async Task SendPasswordResetAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        await _provider.SendPasswordResetEmailAsync(email);
    }
}
