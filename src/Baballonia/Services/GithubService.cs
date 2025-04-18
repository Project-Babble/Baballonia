using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using AvaloniaMiaDev.Models;

namespace AvaloniaMiaDev.Services;

public class GithubService
{
    static GithubService()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VRCFaceTracking", "1.0"));
    }

    private static readonly HttpClient Client = new();

    public async Task<List<GithubContributor>> GetContributors(string repo)
    {
        var response = await Client.GetAsync($"https://api.github.com/repos/{repo}/contributors");
        if (!response.IsSuccessStatusCode)
        {
            return Enumerable.Empty<GithubContributor>().ToList();
        }
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<GithubContributor>>(content)!;
    }
}
