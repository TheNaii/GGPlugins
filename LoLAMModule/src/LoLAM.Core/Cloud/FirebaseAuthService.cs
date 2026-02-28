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
        // Most UX flows register -> login, so we return a logged-in session:
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
        if (cached is null || string.IsNullOrWhiteSpace(cached.RefreshToken))
            return null;

        try
        {
            async Task<FirebaseAuthLink?> InvokeRefreshAsync(object arg)
            {
                var mi = _provider.GetType().GetMethod("RefreshAuthAsync", new[] { arg.GetType() });
                if (mi is null) return null;

                var taskObj = mi.Invoke(_provider, new[] { arg });
                if (taskObj is not Task task) return null;

                await task.ConfigureAwait(false);

                // Task<T> -> read Result via reflection
                var resultProp = taskObj.GetType().GetProperty("Result");
                return resultProp?.GetValue(taskObj) as FirebaseAuthLink;
            }

            FirebaseAuthLink? refreshed = null;

            // Prefer string overload if available
            refreshed = await InvokeRefreshAsync(cached.RefreshToken);

            // Fallback to FirebaseAuth overload if needed
            refreshed ??= await InvokeRefreshAsync(new FirebaseAuth { RefreshToken = cached.RefreshToken });

            if (refreshed is null || refreshed.User is null || string.IsNullOrWhiteSpace(refreshed.FirebaseToken))
                return null;

            var session = new AuthSession
            {
                Email = refreshed.User.Email ?? cached.Email ?? "",
                UserId = refreshed.User.LocalId ?? cached.UserId ?? "",
                IdToken = refreshed.FirebaseToken,
                RefreshToken = refreshed.RefreshToken ?? cached.RefreshToken
            };

            // Persist updated refresh token (Firebase can rotate them).
            _sessionStore.Save(session);

            return session;
        }
        catch
        {
            // Do not clear cached token on transient failures; user can still be auto-restored later.
            return null;
        }
    }

    public async Task SendPasswordResetAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        await _provider.SendPasswordResetEmailAsync(email);
    }
}
