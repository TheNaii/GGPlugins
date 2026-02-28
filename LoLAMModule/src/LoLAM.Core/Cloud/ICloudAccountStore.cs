using System.Threading;
using System.Threading.Tasks;

namespace LoLAM.Core.Cloud;

public interface ICloudAccountStore
{
    Task<string> DownloadAccountsJsonAsync(AuthSession session, CancellationToken ct = default);
    Task UploadAccountsJsonAsync(AuthSession session, string accountsJson, CancellationToken ct = default);
}