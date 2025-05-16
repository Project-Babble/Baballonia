using System.Collections.Generic;
using System.IO;
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

    public async Task<string> DownloadRelease(string localPath, string asset)
    {
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = await Client.GetByteArrayAsync(asset);
        await File.WriteAllBytesAsync(localPath, bytes);
        return localPath;
    }
}
