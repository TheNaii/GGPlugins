using Newtonsoft.Json;
using System;
using System.IO;

namespace LoLAM.Core.Cloud;

public interface IAuthSessionStore
{
    void Save(AuthSession session);
    AuthSession? Load();
    void Clear();
}

/// <summary>
/// Simple file-backed session store to keep users signed in.
/// Stores the Firebase refresh token on disk.
/// NOTE: This is plaintext to avoid extra crypto/package dependencies; you can swap to DPAPI later.
/// </summary>
public sealed class FileAuthSessionStore : IAuthSessionStore
{
    private readonly string _path;

    public FileAuthSessionStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    public void Save(AuthSession session)
    {
        var payload = JsonConvert.SerializeObject(new Persisted
        {
            Email = session.Email,
            UserId = session.UserId,
            RefreshToken = session.RefreshToken
        });

        File.WriteAllText(_path, payload);
    }

    public AuthSession? Load()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var json = File.ReadAllText(_path);
            var p = JsonConvert.DeserializeObject<Persisted>(json);
            if (p is null || string.IsNullOrWhiteSpace(p.RefreshToken)) return null;

            return new AuthSession
            {
                Email = p.Email ?? "",
                UserId = p.UserId ?? "",
                RefreshToken = p.RefreshToken ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch { }
    }

    private sealed class Persisted
    {
        public string? Email { get; set; }
        public string? UserId { get; set; }
        public string? RefreshToken { get; set; }
    }
}
