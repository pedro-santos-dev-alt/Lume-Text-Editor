using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace EditorDeTextoSimples;

internal sealed record LumeUpdate(Version Version, Uri DownloadUri, string? Sha256);

internal static class AtualizadorLume
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/pedro-santos-dev-alt/Lume-Text-Editor/releases/latest");
    private static readonly HttpClient Client = CreateClient();

    public static async Task<LumeUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = release.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;
        var tag = tagElement.GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latestVersion)) return null;

        var currentVersion = typeof(AtualizadorLume).Assembly.GetName().Version ?? new Version(1, 0);
        if (latestVersion <= currentVersion) return null;

        var expectedAssetName = $"Lume-Setup-{latestVersion}-x64.exe";
        if (!root.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) || nameElement.GetString() != expectedAssetName)
                continue;
            if (!asset.TryGetProperty("browser_download_url", out var urlElement) ||
                !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var downloadUri))
                return null;

            var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
            return new LumeUpdate(latestVersion, downloadUri, digest);
        }

        return null;
    }

    public static async Task<string> DownloadAndVerifyAsync(LumeUpdate update, CancellationToken cancellationToken = default)
    {
        var updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "Updates");
        Directory.CreateDirectory(updatesDirectory);
        var installerPath = Path.Combine(updatesDirectory, $"Lume-Setup-{update.Version}-x64.exe");

        using (var response = await Client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            var expectedHash = update.Sha256.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? update.Sha256[7..]
                : update.Sha256;
            await using var installer = File.OpenRead(installerPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
            {
                File.Delete(installerPath);
                throw new InvalidDataException("A verificação de integridade do instalador falhou.");
            }
        }

        return installerPath;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lume-Text-Editor-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
