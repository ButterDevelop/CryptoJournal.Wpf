using CryptoJournal.Wpf.Services.Updates;
using System.Net;
using System.Net.Http;
using System.Text;

namespace CryptoJournal.Wpf.Tests.Updates;

[TestFixture]
internal sealed class GitHubUpdateServiceTests
{
    [Test]
    public async Task CheckAsync_NewerRelease_ReturnsInstallerAndChecksum()
    {
        using var http = CreateHttpClient(CreateReleaseJson("v1.0.4"));
        var service = new GitHubUpdateService(http);

        var update = await service.CheckAsync("1.0.3+local");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(update, Is.Not.Null);
            Assert.That(update!.Version, Is.EqualTo("1.0.4"));
            Assert.That(update.InstallerName, Is.EqualTo("Setup_CryptoJournal_1.0.4.exe"));
            Assert.That(update.ChecksumUrl, Does.EndWith(".sha256"));
        }
    }

    [Test]
    public async Task CheckAsync_CurrentRelease_ReturnsNull()
    {
        using var http = CreateHttpClient(CreateReleaseJson("v1.0.4"));
        var service = new GitHubUpdateService(http);

        var update = await service.CheckAsync("1.0.4.0");

        Assert.That(update, Is.Null);
    }

    [Test]
    public void ReadChecksum_StandardChecksumFile_ReturnsHash()
    {
        var hash = new string('a', 64);

        var parsed = GitHubUpdateService.ReadChecksum($"{hash}  Setup_CryptoJournal_1.0.4.exe\n");

        Assert.That(parsed, Is.EqualTo(hash));
    }

    private static HttpClient CreateHttpClient(string response)
    {
        return new HttpClient(new StubHandler(response));
    }

    private static string CreateReleaseJson(string tag)
    {
        var version = tag.TrimStart('v');
        return $$"""
                 {
                   "tag_name": "{{tag}}",
                   "html_url": "https://github.com/ButterDevelop/CryptoJournal.Wpf/releases/tag/{{tag}}",
                   "assets": [
                     {
                       "name": "Setup_CryptoJournal_{{version}}.exe",
                       "browser_download_url": "https://github.com/installer.exe"
                     },
                     {
                       "name": "Setup_CryptoJournal_{{version}}.exe.sha256",
                       "browser_download_url": "https://github.com/installer.exe.sha256"
                     }
                   ]
                 }
                 """;
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(message);
        }
    }
}
