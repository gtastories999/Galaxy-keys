using System.Security.Cryptography;
using System.Text.Json;
using GalaxyKeys;

var root = Path.Combine(Path.GetTempPath(), "GalaxyKeys-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("FAIL: " + name); }
const string password = "test-only unique passphrase 456!";
var path = Path.Combine(root, "vault.gkv");
try
{
    using var v = new Vault(path);
    Reject(() => v.Create("short"), "Reject weak master password");
    v.Create(password);
    var first = new Entry(Guid.NewGuid().ToString("N"), "API Key", "OpenAI", "", "sk-test-not-real-12345", "", DateTimeOffset.UtcNow);
    var second = new Entry(Guid.NewGuid().ToString("N"), "Login", "Website", "ilja@example.test", "test-only-secret-password", "https://example.test", DateTimeOffset.UtcNow);
    v.Put(first); v.Put(second);
    var disk = File.ReadAllText(path);
    Check(!disk.Contains(first.Secret) && !disk.Contains(second.Secret) && !disk.Contains(second.Username) && !disk.Contains(first.Name), "Names, usernames, and secrets encrypted on disk");
    Check(v.Search("OPENAI", "All items").Count() == 1 && v.Search("ilja", "All items").Count() == 1, "Case insensitive shared name / username search");
    Check(v.Search("", "API Key").Count() == 1 && v.Search("", "Login").Count() == 1, "Separate API / login lists");
    v.Lock(); Check(!v.IsOpen && v.Entries.Count == 0, "Lock removes accessible entries");
    Reject(() => v.Unlock("incorrect-password"), "Reject wrong password"); Check(!v.IsOpen, "Wrong password keeps vault locked");
    v.Unlock(password); Check(v.Entries.Count == 2, "Reopen persisted vault");
    var bytes1 = File.ReadAllBytes(path); v.Save(); Check(!bytes1.SequenceEqual(File.ReadAllBytes(path)), "Fresh encryption nonce for every save");
    var backup = path + ".bak"; using (var b = new Vault(backup)) { b.Unlock(password); Check(b.Entries.Count == 2, "Previous-save backup decrypts"); }
    var original = File.ReadAllBytes(path); var e = Vault.Read(path);
    var corrupted = e.Payload.Ciphertext.ToArray(); corrupted[0] ^= 1;
    File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(e with { Payload = e.Payload with { Ciphertext = corrupted } }));
    v.Lock(); Reject(() => v.Unlock(password), "Reject tampered payload");
    File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(e with { Header = e.Header with { Id = Guid.NewGuid().ToString("N") } }));
    Reject(() => v.Unlock(password), "Reject tampered header");
    File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(e with { Header = e.Header with { Iterations = int.MaxValue } }));
    Reject(() => v.Unlock(password), "Reject hostile KDF parameters before expensive derivation");
    File.WriteAllBytes(path, original); v.Unlock(password);
    var raw = v.CopyKey(); v.Lock(); v.UnlockWithKey(raw); CryptographicOperations.ZeroMemory(raw); Check(v.IsOpen, "Device-wrapped key path decrypts same vault");
    Reject(() => v.ChangePassword("wrong-password", "new long master passphrase"), "Password change verifies current password");
    v.ChangePassword(password, "new long master passphrase"); v.Lock(); Reject(() => v.Unlock(password), "Old password rejected after change"); v.Unlock("new long master passphrase");
    Check(v.Entries.Count == 2, "Password change preserves entries");
    v.Put(first with { Name = "Renamed key" }); Check(v.Entries.Count == 2 && v.Search("Renamed", "All items").Count() == 1, "Edit updates item without duplicating");
    v.Delete(first.Id); v.Lock(); v.Unlock("new long master passphrase"); Check(v.Entries.Count == 1, "Deletion persists");
    // A failed atomic replacement must roll back the in-memory edit as well.
    File.Move(path, path + ".saved"); Directory.CreateDirectory(path);
    Reject(() => v.Put(first), "Save failure is reported"); Check(v.Entries.Count == 1, "Failed save rolls back in-memory mutation");
    Directory.Delete(path); File.Move(path + ".saved", path);
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(root, true); }
