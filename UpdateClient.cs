using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace BatteryMonitor;

internal static class UpdateClient
{
    internal static Version CurrentVersion { get; } = new Version(typeof(UpdateClient).Assembly.GetName().Version!.ToString(3));
    private static readonly Lazy<UpdateConfig?> Config = new(ReadConfig);
    internal static bool IsConfigured => Config.Value != null;
    internal static Uri? ReleasesPage => Config.Value is { } config ? new Uri($"https://github.com/{config.Repository}/releases") : null;
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan });
    private static UpdateConfig? ReadConfig()
    {
        try
        {
            using var stream = typeof(UpdateClient).Assembly.GetManifestResourceStream("BatteryMonitor.UpdateConfig.json");
            if (stream == null) return null;
            var config = JsonSerializer.Deserialize<UpdateConfig>(stream);
            if (config == null || !UpdateProtocol.ValidRepository(config.Repository) || string.IsNullOrWhiteSpace(config.PublicKey)) return null;
            using var rsa = RSA.Create(); rsa.ImportFromPem(config.PublicKey);
            return rsa.KeySize >= 2048 ? config : null;
        }
        catch { return null; }
    }
    // Only follow HTTPS redirects to GitHub's release download infrastructure.
    private static bool Allowed(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
    private static async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int i = 0; i < 6; i++)
        {
            if (!Allowed(uri)) throw new InvalidDataException("Unexpected update download host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("BatteryMonitor/" + CurrentVersion);
            var response = await Http.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) throw new InvalidDataException("Missing redirect.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException("Update server returned HTTP " + (int)status, null, status);
            }
            return response;
        }
        throw new InvalidDataException("Too many redirects.");
    }
    internal static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        var config = Config.Value ?? throw new InvalidOperationException("Update source is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetAsync(new Uri($"https://github.com/{config.Repository}/releases/latest/download/update.json"), timeout.Token).ConfigureAwait(false);
        using var data = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int count;
        while ((count = await data.ReadAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > UpdateProtocol.MaxManifestBytes) throw new InvalidDataException("Manifest too large.");
            buffer.Write(chunk, 0, count);
        }
        var release = UpdateProtocol.Verify(buffer.ToArray(), config.PublicKey);
        UpdatePreferences.RecordSuccessfulCheck();
        return UpdateProtocol.ParseVersion(release.Version) > CurrentVersion ? release : null;
    }
    internal static async Task<string> DownloadAsync(UpdateRelease release, IProgress<int> progress, CancellationToken cancellationToken)
    {
        UpdateProtocol.Validate(release);
        if (UpdateProtocol.ParseVersion(release.Version) <= CurrentVersion) throw new InvalidDataException("Update is not newer.");
        var config = Config.Value ?? throw new InvalidOperationException("Update source is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryMonitor", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, release.Asset);
        try
        {
            using var response = await GetAsync(new Uri($"https://github.com/{config.Repository}/releases/download/v{release.Version}/{release.Asset}"), token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException("Installer size mismatch.");
            using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            int lastProgress = -1;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] chunk = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("Installer size mismatch.");
                    hash.AppendData(chunk, 0, count);
                    await file.WriteAsync(chunk.AsMemory(0, count), token).ConfigureAwait(false);
                    int percent = (int)(total * 100 / release.Size);
                    if (percent != lastProgress) { progress.Report(percent); lastProgress = percent; }
                }
            }
            if (total != release.Size || !UpdateProtocol.MatchesHash(hash.GetHashAndReset(), release.Sha256)) throw new CryptographicException("Installer SHA-256 verification failed.");
            // Mark as an Internet download so Windows can apply its usual protections.
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            return path;
        }
        catch { DeleteDownload(path); throw; }
    }
    internal static void DeleteDownload(string path)
    {
        try { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); } catch { }
    }
    internal static void LaunchVerified(string path, UpdateRelease release)
    {
        // Recheck the on-disk installer before opening the ordinary interactive setup wizard.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != release.Size || !UpdateProtocol.MatchesHash(SHA256.HashData(file), release.Sha256)) throw new CryptographicException("Installer changed after download.");
        var info = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! };
        info.Arguments = StartupSettings.IsConfigured() ? "/MERGETASKS=autostart" : "/MERGETASKS=!autostart";
        // Inno Setup checks the existing mutex at launch, so the caller must exit the app
        // before invoking this method (see Program.Main). No elevation or silent flags.
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not open the installer.");
    }
}
