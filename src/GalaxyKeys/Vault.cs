using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GalaxyKeys;

public sealed record Entry(string Id, string Kind, string Name, string Username, string Secret, string Website, DateTimeOffset Updated);
public sealed record Box(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
public sealed record Header(int Version, string Id, int Iterations, byte[] Salt, Box WrappedKey);
public sealed record Envelope(Header Header, Box Payload);

public static class Crypto
{
    public const int Iterations = 600_000;
    public static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
    public static Box Seal(byte[] key, byte[] data, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag, aad);
        return new(nonce, cipher, tag);
    }
    public static byte[] Open(byte[] key, Box box, byte[] aad)
    {
        if (box.Nonce.Length != 12 || box.Tag.Length != 16) throw new InvalidDataException("Invalid encrypted data.");
        var data = new byte[box.Ciphertext.Length];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(box.Nonce, box.Ciphertext, box.Tag, data, aad); return data; }
        catch { CryptographicOperations.ZeroMemory(data); throw; }
    }
    public static byte[] Context(string id) => Encoding.UTF8.GetBytes("GalaxyKeys/v1/key/" + id);
}

public sealed class Vault : IDisposable
{
    public const int MaxFileSize = 32 * 1024 * 1024;
    public string Path { get; }
    public Header? Header { get; private set; }
    byte[]? key;
    public List<Entry> Entries { get; private set; } = [];
    public bool IsOpen => key != null;
    public Vault(string path) => Path = path;
    public static Envelope Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxFileSize) throw new InvalidDataException("Vault is missing or too large.");
        var e = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Invalid vault.");
        if (e.Header.Version != 1 || e.Header.Iterations != Crypto.Iterations || e.Header.Salt.Length != 32 || !Guid.TryParseExact(e.Header.Id, "N", out _))
            throw new InvalidDataException("Unsupported or damaged vault header.");
        return e;
    }
    public void Create(string password)
    {
        if (File.Exists(Path)) throw new IOException("A vault already exists.");
        ValidatePassword(password);
        var candidate = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(32); var id = Guid.NewGuid().ToString("N");
        var derived = Crypto.Derive(password, salt, Crypto.Iterations);
        try
        {
            Header = new(1, id, Crypto.Iterations, salt, Crypto.Seal(derived, candidate, Crypto.Context(id)));
            key = candidate; Entries = []; Save();
        }
        catch { Lock(); throw; }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public void Unlock(string password)
    {
        var e = Read(Path); var derived = Crypto.Derive(password, e.Header.Salt, e.Header.Iterations);
        try
        {
            var candidate = Crypto.Open(derived, e.Header.WrappedKey, Crypto.Context(e.Header.Id));
            AcceptKey(e, candidate);
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public void UnlockWithKey(byte[] candidate) => AcceptKey(Read(Path), candidate.ToArray());
    void AcceptKey(Envelope e, byte[] candidate)
    {
        byte[]? plain = null;
        try
        {
            plain = Crypto.Open(candidate, e.Payload, JsonSerializer.SerializeToUtf8Bytes(e.Header));
            var entries = JsonSerializer.Deserialize<List<Entry>>(plain) ?? throw new InvalidDataException("Invalid vault contents.");
            if (entries.Any(x => x.Kind is not ("API Key" or "Login") || string.IsNullOrWhiteSpace(x.Name))) throw new InvalidDataException("Invalid entries.");
            Lock(); key = candidate; Header = e.Header; Entries = entries;
        }
        catch { CryptographicOperations.ZeroMemory(candidate); throw; }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }
    public byte[] CopyKey() => key?.ToArray() ?? throw new InvalidOperationException("Vault is locked.");
    public void Save()
    {
        if (key == null || Header == null) throw new InvalidOperationException("Vault is locked.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(Entries);
        try
        {
            var envelope = new Envelope(Header, Crypto.Seal(key, plain, JsonSerializer.SerializeToUtf8Bytes(Header)));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (bytes.Length > MaxFileSize) throw new IOException("Vault size limit reached. The previous save has been kept.");
            AtomicWrite(Path, bytes, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Put(Entry entry)
    {
        var before = Entries.ToList();
        Entries.RemoveAll(x => x.Id == entry.Id); Entries.Add(entry);
        try { Save(); } catch { Entries = before; throw; }
    }
    public void Delete(string id)
    {
        var before = Entries.ToList(); Entries.RemoveAll(x => x.Id == id);
        try { Save(); } catch { Entries = before; throw; }
    }
    public IEnumerable<Entry> Search(string query, string kind) => Entries
        .Where(x => (kind == "All items" || x.Kind == kind) &&
            (x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Username.Contains(query, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    public void ChangePassword(string oldPassword, string newPassword)
    {
        ValidatePassword(newPassword); if (key == null || Header == null) throw new InvalidOperationException("Vault is locked.");
        var oldHeader = Header; var oldDerived = Crypto.Derive(oldPassword, oldHeader.Salt, oldHeader.Iterations);
        byte[]? checkedKey = null; byte[]? derived = null;
        try
        {
            checkedKey = Crypto.Open(oldDerived, oldHeader.WrappedKey, Crypto.Context(oldHeader.Id));
            if (!CryptographicOperations.FixedTimeEquals(checkedKey, key)) throw new CryptographicException();
            var salt = RandomNumberGenerator.GetBytes(32); derived = Crypto.Derive(newPassword, salt, Crypto.Iterations);
            Header = oldHeader with { Salt = salt, WrappedKey = Crypto.Seal(derived, key, Crypto.Context(oldHeader.Id)) };
            try { Save(); } catch { Header = oldHeader; throw; }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldDerived);
            if (checkedKey != null) CryptographicOperations.ZeroMemory(checkedKey);
            if (derived != null) CryptographicOperations.ZeroMemory(derived);
        }
    }
    public static void ValidatePassword(string password)
    {
        if (password.Length < 14) throw new ArgumentException("Use at least 14 characters. A long, unique passphrase is recommended.");
        if (password.Length > 1024) throw new ArgumentException("Master password is too long.");
    }
    public static void AtomicWrite(string path, byte[] data, bool backup)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(data); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Lock()
    {
        if (key != null) CryptographicOperations.ZeroMemory(key);
        key = null; Entries.Clear(); Header = null;
    }
    public void Dispose() => Lock();
}
