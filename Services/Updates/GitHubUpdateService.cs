using CryptoJournal.Wpf.Storage.Common;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace CryptoJournal.Wpf.Services.Updates;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/ButterDevelop/CryptoJournal.Wpf/releases/latest";

    private readonly HttpClient _http;

    public GitHubUpdateService(HttpClient http) => _http = http;

    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var installedVersion))
            throw new InvalidOperationException($"The installed version '{currentVersion}' is not valid.");

        var release = await _http.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken)
                      ?? throw new InvalidDataException("GitHub returned an empty release response.");

        if (!ReleaseVersion.TryParse(release.TagName, out var availableVersion))
            throw new InvalidDataException($"The release tag '{release.TagName}' is not a supported version.");

        if (availableVersion.CompareTo(installedVersion) <= 0)
            return null;

        var installerName = $"Setup_CryptoJournal_{availableVersion}.exe";
        var installer = FindAsset(release.Assets, installerName);
        var checksum  = FindAsset(release.Assets, installerName + ".sha256");

        return new UpdateInfo(
            availableVersion.ToString(),
            RequireHttpsUrl(release.HtmlUrl),
            installerName,
            RequireHttpsUrl(installer.BrowserDownloadUrl),
            RequireHttpsUrl(checksum.BrowserDownloadUrl));
    }

    public async Task<string> DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var updateDirectory = AppDataPaths.EnsureDir(
            Path.Combine(AppDataPaths.RootDir, "updates", update.Version));
        var installerPath = Path.Combine(updateDirectory, update.InstallerName);
        var temporaryPath = installerPath + ".download";

        try
        {
            await DownloadFileAsync(update.InstallerUrl, temporaryPath, cancellationToken);

            var checksumText = await _http.GetStringAsync(update.ChecksumUrl, cancellationToken);
            var expectedHash = ReadChecksum(checksumText);
            var actualHash   = await ComputeSha256Async(temporaryPath, cancellationToken);

            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded installer checksum does not match the release checksum.");

            File.Move(temporaryPath, installerPath, true);
            return installerPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
    }

    private static GitHubAsset FindAsset(IEnumerable<GitHubAsset> assets, string name)
    {
        return assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException($"Release asset '{name}' was not found.");
    }

    private static string RequireHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("GitHub returned an invalid download URL.");

        return uri.AbsoluteUri;
    }

    internal static string ReadChecksum(string value)
    {
        var hash = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The release checksum is invalid.");

        return hash;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
