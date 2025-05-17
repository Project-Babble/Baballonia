using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Baballonia.Models;

namespace Baballonia.Services;

public class GithubService
{
    static GithubService()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ProjectBabble", "1.0"));
    }

    private static readonly HttpClient Client = new();

    public async Task<List<GithubContributor>> GetContributors(string owner, string repo)
    {
        var response = await Client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/contributors");
        if (!response.IsSuccessStatusCode)
        {
            return Enumerable.Empty<GithubContributor>().ToList();
        }
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<GithubContributor>>(content)!;
    }

    public async Task<GithubRelease> GetReleases(string owner, string repo)
    {
        var response = await Client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        if (!response.IsSuccessStatusCode)
        {
            return new GithubRelease();
        }
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GithubRelease>(content)!;
    }

    /// <summary>
    /// Downloads an OpenIris .zip artifact from github, extracts it and returns the paths of the firmware and config
    /// </summary>
    /// <param name="path"></param>
    /// <param name="asset"></param>
    /// <returns></returns>
    public async Task<(FirmwareConfig config, string firmwarePath)> DownloadAndExtractOpenIrisRelease(string tempDir, string asset, string name)
    {
        var zipFile = Path.Combine(tempDir, name);
        var bytes = await Client.GetByteArrayAsync(asset);

        await File.WriteAllBytesAsync(zipFile, bytes);
        ZipFile.ExtractToDirectory(zipFile, tempDir);

        var manifest = Path.Combine(tempDir, "manifest.json");
        var manifestText = await File.ReadAllTextAsync(manifest);
        var config = JsonSerializer.Deserialize<FirmwareConfig>(manifestText);

        var firmware = Path.Combine(tempDir, "merged-firmware.bin");
        return (config, firmwarePath: firmware)!;
    }
}
