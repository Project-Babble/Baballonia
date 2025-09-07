using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baballonia.Services;

public class UpdateService
{
    private string _url = "https://api.github.com/repos/Project-Babble/Baballonia/releases/latest";

    public async Task<string?> GetLatestVersion()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Baballonia");

        var rawJson = await httpClient.GetStringAsync(_url);

        var json = JsonDocument.Parse(rawJson);

        var tagName = json.RootElement.GetProperty("tag_name").GetString();

        return tagName?.TrimStart('v');
    }

}
