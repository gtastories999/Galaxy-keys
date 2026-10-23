using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace GalaxyKeys;

// The private RSA key stays inside the Windows Passport KSP. No DPAPI shortcut.
public static class Hello
{
    static readonly CngProvider Provider = new("Microsoft Passport Key Storage Provider");
    sealed record Wrapper(int Version, string VaultId, string KeyName, byte[] Wrapped);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Oaep { [MarshalAs(UnmanagedType.LPWStr)] public string Algorithm; public IntPtr Label; public int LabelLength; }
    [DllImport("ncrypt.dll")]
    static extern int NCryptDecrypt(SafeNCryptKeyHandle key, byte[] input, int count, ref Oaep padding, byte[] output, int outputLength, out int resultLength, int flags);
    public static bool IsEnabled(string path) => File.Exists(path + ".hello");
    static string KeyName(string id) => WindowsIdentity.GetCurrent().User!.Value + "//GalaxyKeys/local/" + id;
    static void Context(CngKey key, IntPtr hwnd)
    {
        key.SetProperty(new CngProperty("HWND Handle", IntPtr.Size == 8 ? BitConverter.GetBytes(hwnd.ToInt64()) : BitConverter.GetBytes(hwnd.ToInt32()), CngPropertyOptions.None));
        key.SetProperty(new CngProperty("Use Context", Encoding.Unicode.GetBytes("Unlock your Galaxy Keys vault\0"), CngPropertyOptions.None));
    }
    static void Verify(CngKey key)
    {
        var cache = BitConverter.ToInt32(key.GetProperty("NgcCacheType", CngPropertyOptions.None).GetValue());
        var usage = BitConverter.ToInt32(key.GetProperty("Key Usage", CngPropertyOptions.None).GetValue());
        if (cache != 1 || (usage & 8) != 0 || key.ExportPolicy != CngExportPolicies.None)
            throw new CryptographicException("Windows Hello key policy does not meet the required protection level.");
    }
    static byte[] Decrypt(CngKey key, byte[] wrapped, IntPtr hwnd)
    {
        Verify(key); Context(key, hwnd);
        key.SetProperty(new CngProperty("PinCacheIsGestureRequired", BitConverter.GetBytes(1), CngPropertyOptions.None));
        var output = new byte[512];
        try
        {
            var padding = new Oaep { Algorithm = "SHA256" };
            var status = NCryptDecrypt(key.Handle, wrapped, wrapped.Length, ref padding, output, output.Length, out var count, 4);
            if (status != 0) throw new CryptographicException(status);
            if (count != 32) throw new CryptographicException("Invalid Windows Hello vault key.");
            return output.AsSpan(0, count).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(output); }
    }
    public static void Enable(Vault vault, IntPtr hwnd)
    {
        var id = vault.Header!.Id; var name = KeyName(id) + "/" + Guid.NewGuid().ToString("N");
        var parameters = new CngKeyCreationParameters { Provider = Provider, ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Decryption, ParentWindowHandle = hwnd };
        parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
        parameters.Parameters.Add(new CngProperty("NgcCacheType", BitConverter.GetBytes(1), CngPropertyOptions.None));
        using var key = CngKey.Create(CngAlgorithm.Rsa, name, parameters);
        var success = false; var raw = vault.CopyKey(); byte[]? check = null;
        try
        {
            Verify(key);
            using var rsa = new RSACng(key);
            var wrapped = rsa.Encrypt(raw, RSAEncryptionPadding.OaepSHA256);
            check = Decrypt(key, wrapped, hwnd);
            if (!CryptographicOperations.FixedTimeEquals(raw, check)) throw new CryptographicException("Windows Hello verification failed.");
            Vault.AtomicWrite(vault.Path + ".hello", JsonSerializer.SerializeToUtf8Bytes(new Wrapper(1, id, name, wrapped)), false);
            success = true;
        }
        finally { CryptographicOperations.ZeroMemory(raw); if (check != null) CryptographicOperations.ZeroMemory(check); if (!success) key.Delete(); }
    }
    public static void Unlock(Vault vault, IntPtr hwnd)
    {
        var file = vault.Path + ".hello";
        if (new FileInfo(file).Length > 8192) throw new InvalidDataException("Invalid Windows Hello enrollment.");
        var wrapper = JsonSerializer.Deserialize<Wrapper>(File.ReadAllBytes(file)) ?? throw new InvalidDataException();
        var envelope = Vault.Read(vault.Path);
        if (wrapper.Version != 1 || wrapper.VaultId != envelope.Header.Id || !wrapper.KeyName.StartsWith(KeyName(wrapper.VaultId) + "/", StringComparison.Ordinal)) throw new InvalidDataException("Windows Hello enrollment does not match this vault.");
        using var key = CngKey.Open(wrapper.KeyName, Provider);
        var raw = Decrypt(key, wrapper.Wrapped, hwnd);
        try { vault.UnlockWithKey(raw); } finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public static void Disable(string path)
    {
        var file = path + ".hello";
        if (!File.Exists(file)) return;
        var wrapper = JsonSerializer.Deserialize<Wrapper>(File.ReadAllBytes(file)) ?? throw new InvalidDataException();
        if (!wrapper.KeyName.StartsWith(KeyName(wrapper.VaultId) + "/", StringComparison.Ordinal)) throw new InvalidDataException();
        if (CngKey.Exists(wrapper.KeyName, Provider)) { using var key = CngKey.Open(wrapper.KeyName, Provider); key.Delete(); }
        File.Delete(file);
    }
}
