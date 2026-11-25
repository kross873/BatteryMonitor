using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BatteryMonitor;

try
{
    string root = Directory.GetCurrentDirectory();
    string configPath = Path.Combine(root, "update-config.json");
    var config = JsonSerializer.Deserialize<UpdateConfig>(File.ReadAllText(configPath)) ?? throw new InvalidDataException("Invalid configuration.");
    string operation = args.Length > 0 ? args[0] : "";
    if (operation == "configure")
    {
        Console.Write("Public GitHub repository (owner/repo): ");
        string repo = (Console.ReadLine() ?? "").Trim();
        if (!UpdateProtocol.ValidRepository(repo)) throw new ArgumentException("Enter owner/repo, not a URL.");
        if (UpdateProtocol.ValidRepository(config.Repository) && !string.Equals(config.Repository, repo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Changing a configured repository requires a new manual installation for all users.");
        string keyPath = KeyPath(repo);
        using var rsa = RSA.Create(3072);
        if (File.Exists(keyPath)) rsa.ImportFromPem(File.ReadAllText(keyPath));
        else
        {
            if (!string.IsNullOrEmpty(config.PublicKey)) throw new InvalidOperationException("Existing private key is missing. Restore your backup; do not create a replacement.");
            Console.WriteLine("If you have already published this app, stop and restore the original signing key.");
            Console.Write("Generate a NEW signing key for first-time setup? Type YES: ");
            if (Console.ReadLine() != "YES") return 1;
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            using var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file); writer.Write(rsa.ExportPkcs8PrivateKeyPem());
        }
        string publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        if (!string.IsNullOrEmpty(config.PublicKey) && config.PublicKey != publicKey) throw new CryptographicException("Signing key does not match the existing config.");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new UpdateConfig(repo, publicKey)));
        Console.WriteLine("Configured. Rebuild the setup before sharing it.");
        Console.WriteLine("PRIVATE KEY (back up securely, never upload or share): " + keyPath);
        return 0;
    }
    if (operation == "sign")
    {
        if (!UpdateProtocol.ValidRepository(config.Repository) || string.IsNullOrEmpty(config.PublicKey)) throw new InvalidOperationException("Run configure-updates.cmd first, then rebuild the installer.");
        string version = XDocument.Load(Path.Combine(root, "BatteryMonitor.csproj")).Descendants("Version").Single().Value;
        string asset = UpdateProtocol.AssetName(version);
        string setup = Path.Combine(root, "release", asset);
        using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(KeyPath(config.Repository)));
        if (rsa.ExportSubjectPublicKeyInfoPem() != config.PublicKey) throw new CryptographicException("Wrong signing key.");
        using var file = File.OpenRead(setup);
        var release = new UpdateRelease(version, asset, file.Length, Convert.ToHexString(SHA256.HashData(file)));
        string notesPath = Path.Combine(root, "RELEASE-NOTES.txt");
        if (File.Exists(notesPath))
        {
            string notes = File.ReadAllText(notesPath).Trim();
            if (notes.Length > UpdateProtocol.MaxNotesChars) throw new InvalidDataException("Release notes exceed the allowed length.");
            if (notes.Length > 0) release = release with { Notes = notes };
        }
        byte[] manifest = UpdateProtocol.Sign(release, rsa);
        _ = UpdateProtocol.Verify(manifest, config.PublicKey);
        File.WriteAllBytes(Path.Combine(root, "release", "update.json"), manifest);
        Console.WriteLine("Upload ONLY these two files to a public GitHub Release tagged v" + version + ":");
        Console.WriteLine(asset + " and update.json");
        return 0;
    }
    if (operation == "self-test")
    {
        using var rsa = RSA.Create(2048); using var wrong = RSA.Create(2048);
        var r = new UpdateRelease("0.11.1", UpdateProtocol.AssetName("0.11.1"), 3, Convert.ToHexString(SHA256.HashData(new byte[]{1,2,3})));
        byte[] manifest = UpdateProtocol.Sign(r, rsa);
        if (UpdateProtocol.Verify(manifest, rsa.ExportSubjectPublicKeyInfoPem()) != r) throw new Exception("Round trip failed.");
        var envelope = JsonSerializer.Deserialize<UpdateEnvelope>(manifest)!;
        var bytes = Convert.FromBase64String(envelope.Payload); bytes[0] ^= 1;
        ExpectFailure(() => UpdateProtocol.Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(bytes) }), rsa.ExportSubjectPublicKeyInfoPem()));
        ExpectFailure(() => UpdateProtocol.Verify(manifest, wrong.ExportSubjectPublicKeyInfoPem()));
        ExpectFailure(() => UpdateProtocol.Validate(r with { Asset = "other.exe" }));
        if (UpdateProtocol.MatchesHash(SHA256.HashData(new byte[]{1,2,4}), r.Sha256)) throw new Exception("Bad hash accepted.");
        if (!(UpdateProtocol.ParseVersion("0.11.10") > UpdateProtocol.ParseVersion("0.11.2"))) throw new Exception("Version comparison failed.");
        var withNotes = r with { Notes = "Release notes\nSecond line" };
        if (UpdateProtocol.Verify(UpdateProtocol.Sign(withNotes, rsa), rsa.ExportSubjectPublicKeyInfoPem()) != withNotes) throw new Exception("Signed notes round trip failed.");
        ExpectFailure(() => UpdateProtocol.Validate(r with { Notes = new string('x', UpdateProtocol.MaxNotesChars + 1) }));
        Console.WriteLine("Eight protocol tests passed."); return 0;
    }
    throw new ArgumentException("Use configure, sign, or self-test.");
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static string KeyPath(string repo) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryMonitorPublisher", repo.Replace('/', '_').ToLowerInvariant() + ".pem");
static void ExpectFailure(Action action)
{
    try { action(); } catch { return; }
    throw new Exception("Invalid update was accepted.");
}
