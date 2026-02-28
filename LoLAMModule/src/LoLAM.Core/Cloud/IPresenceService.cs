using System.Threading;
using System.Threading.Tasks;

namespace LoLAM.Core.Cloud;

public interface IPresenceService
{
    Task SetOnlineAsync(AuthSession session, CancellationToken ct = default);
    Task SetOfflineAsync(AuthSession session, CancellationToken ct = default);
}