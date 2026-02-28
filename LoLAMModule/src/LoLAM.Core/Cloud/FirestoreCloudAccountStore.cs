using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LoLAM.Core.Cloud;

public sealed class FirestoreCloudAccountStore : ICloudAccountStore
{
    private readonly FirebaseOptions _opts;

    public FirestoreCloudAccountStore(FirebaseOptions opts)
    {
        _opts = opts;
    }

    public async Task<string> DownloadAccountsJsonAsync(AuthSession session, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var url =
            $"https://firestore.googleapis.com/v1/projects/{_opts.ProjectId}/databases/(default)/documents/users/{session.UserId}/accounts/main";

        using var client = CreateClient(session.IdToken);

        using var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return "[]"; // matches your current behavior

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Firestore read failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        dynamic doc = JsonConvert.DeserializeObject(body);
        if (doc == null || doc.fields == null || doc.fields.accountsData == null)
            return "[]";

        return (string)doc.fields.accountsData.stringValue ?? "[]";
    }

    public async Task UploadAccountsJsonAsync(AuthSession session, string accountsJson, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var client = CreateClient(session.IdToken);

        async Task<HttpResponseMessage> PatchAsync()
        {
            var urlPatch =
                $"https://firestore.googleapis.com/v1/projects/{_opts.ProjectId}/databases/(default)/documents/users/{session.UserId}/accounts/main";
            var data = new { fields = new { accountsData = new { stringValue = accountsJson } } };
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            return await client.PatchAsync(urlPatch, content, ct);
        }

        async Task<HttpResponseMessage> PostAsync()
        {
            var urlPost =
                $"https://firestore.googleapis.com/v1/projects/{_opts.ProjectId}/databases/(default)/documents/users/{session.UserId}/accounts?documentId=main";
            var data = new { fields = new { accountsData = new { stringValue = accountsJson } } };
            var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            return await client.PostAsync(urlPost, content, ct);
        }

        using var resp1 = await PatchAsync();

        if (resp1.StatusCode == HttpStatusCode.NotFound)
        {
            resp1.Dispose();
            using var resp2 = await PostAsync();
            var body2 = await resp2.Content.ReadAsStringAsync(ct);

            if (!resp2.IsSuccessStatusCode)
                throw new Exception($"Firestore write failed: {(int)resp2.StatusCode} {resp2.ReasonPhrase}\n{body2}");

            return;
        }

        var body1 = await resp1.Content.ReadAsStringAsync(ct);

        if (!resp1.IsSuccessStatusCode)
            throw new Exception($"Firestore write failed: {(int)resp1.StatusCode} {resp1.ReasonPhrase}\n{body1}");
    }

    private static HttpClient CreateClient(string idToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return client;
    }
}