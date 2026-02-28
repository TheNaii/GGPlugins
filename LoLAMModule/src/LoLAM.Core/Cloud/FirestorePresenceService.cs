using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LoLAM.Core.Cloud;

public sealed class FirestorePresenceService : IPresenceService
{
    private readonly FirebaseOptions _opts;

    public FirestorePresenceService(FirebaseOptions opts)
    {
        _opts = opts;
    }

    public Task SetOnlineAsync(AuthSession session, CancellationToken ct = default)
        => SetPresenceAsync(session, "online", ct);

    public Task SetOfflineAsync(AuthSession session, CancellationToken ct = default)
        => SetPresenceAsync(session, "offline", ct);

    private async Task SetPresenceAsync(AuthSession session, string value, CancellationToken ct)
    {
        var url =
            $"https://firestore.googleapis.com/v1/projects/{_opts.ProjectId}/databases/(default)/documents/users/{session.UserId}?updateMask.fieldPaths=presence";

        var data = new { fields = new { presence = new { stringValue = value } } };
        var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.IdToken);
        await client.PatchAsync(url, content, ct);
    }
}