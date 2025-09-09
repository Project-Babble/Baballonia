using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Baballonia.Services;

public class UpdateService
{
    private string _url = "https://api.github.com/repos/Project-Babble/Baballonia/releases/latest";
    private Version? _fetchedLatest = null;

    /// <summary>
    /// Gets lates release tag from our github
    /// </summary>
    /// <returns>github release tag</returns>
    public async Task<string> FetchLatestTagAsync()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Baballonia");

        var rawJson = await httpClient.GetStringAsync(_url);

        var json = JsonDocument.Parse(rawJson);

        // assume github api will not change and tag_name is always present
        return json.RootElement.GetProperty("tag_name").GetString()!;
    }

    public async Task<Version?> TryGetLatestVersion()
    {
        if (_fetchedLatest != null)
            return _fetchedLatest;

        string res;
        try
        {
            res = await FetchLatestTagAsync();
        }
        catch
        {
            return null;
        }

        var latestVersion = Utils.FindVersionInString(res);

        _fetchedLatest = latestVersion;

        return _fetchedLatest;
    }

    /// <summary>
    /// Checks if current assembly is of the latest release from by the github releases
    /// In absence of internet or github api assumes that current version is latest
    /// </summary>
    /// <returns>true if yes or error, false if no</returns>
    public async Task<bool> IsLatest()
    {
        var latestVersion = await TryGetLatestVersion();
        if (latestVersion == null)
            return true;

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (currentVersion == null)
            throw new InvalidOperationException("Challenge Complete!: How Did We Get Here? No Assembly version found");

        var versionDifference = currentVersion.CompareTo(latestVersion);

        // false only if newer version exists
        return versionDifference >= 0;
    }

}
