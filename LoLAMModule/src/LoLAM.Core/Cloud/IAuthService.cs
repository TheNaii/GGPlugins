using System.Threading;
using System.Threading.Tasks;

namespace LoLAM.Core.Cloud;

public interface IAuthService
{
    Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<AuthSession> RegisterAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Attempts to restore a previously persisted login session (if any). Returns null if not available/valid.</summary>
    Task<AuthSession?> TryRestoreSessionAsync(CancellationToken ct = default);

    Task SendPasswordResetAsync(string email);
}
