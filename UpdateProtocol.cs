using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BatteryMonitor;

internal sealed record UpdateConfig(string Repository, string PublicKey);
internal sealed record UpdateEnvelope(string Payload, string Signature);
internal sealed record UpdateRelease(string Version, string Asset, long Size, string Sha256)
{
    // Optional signed metadata; older clients ignore unknown JSON properties.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; init; }
}

internal static class UpdateProtocol
{
    public const long MaxInstallerBytes = 512L * 1024 * 1024;
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxNotesChars = 4000;
    public static bool ValidRepository(string? repository) => repository != null &&
        Regex.IsMatch(repository, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}\z") &&
        repository.Split('/')[1] is not "." and not "..";
    public static Version ParseVersion(string value)
    {
        if (value == null || !Regex.IsMatch(value, @"\A[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}\z") ||
            !Version.TryParse(value, out var version) || version.ToString(3) != value)
            throw new InvalidDataException("Invalid release version.");
        return version;
    }
    public static string AssetName(string version) => $"BatteryMonitor-{ParseVersion(version)}-Setup.exe";
    public static void Validate(UpdateRelease r)
    {
        if (r.Asset != AssetName(r.Version) || r.Size <= 0 || r.Size > MaxInstallerBytes ||
            r.Notes is { Length: > MaxNotesChars } || r.Sha256 == null || !Regex.IsMatch(r.Sha256, @"\A[0-9A-Fa-f]{64}\z"))
            throw new InvalidDataException("Invalid release metadata.");
    }
    public static byte[] Sign(UpdateRelease release, RSA privateKey)
    {
        Validate(release);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(release);
        byte[] signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return JsonSerializer.SerializeToUtf8Bytes(new UpdateEnvelope(Convert.ToBase64String(payload), Convert.ToBase64String(signature)));
    }
    public static UpdateRelease Verify(byte[] manifest, string publicKey)
    {
        if (manifest.Length > MaxManifestBytes) throw new InvalidDataException("Manifest too large.");
        var envelope = JsonSerializer.Deserialize<UpdateEnvelope>(manifest) ?? throw new InvalidDataException("Missing manifest.");
        byte[] payload = Convert.FromBase64String(envelope.Payload);
        byte[] signature = Convert.FromBase64String(envelope.Signature);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 2048 || !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Update signature verification failed.");
        var release = JsonSerializer.Deserialize<UpdateRelease>(payload) ?? throw new InvalidDataException("Missing release.");
        Validate(release);
        return release;
    }
    public static bool MatchesHash(byte[] actual, string expected) => CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
}
