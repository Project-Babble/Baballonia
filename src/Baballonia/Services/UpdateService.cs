using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Baballonia.Models;

namespace Baballonia.Services;

public class UpdateService
{
    private GithubRelease? _fetchedLatest = null;
    private readonly GithubService _githubService;

    public UpdateService(GithubService githubService)
    {
        _githubService = githubService;
    }

    public async Task<Version?> TryGetLatestVersion()
    {
        if (_fetchedLatest != null)
            return Utils.FindVersionInString(_fetchedLatest.tag_name);

        GithubRelease res;
        try
        {
            res = await _githubService.FetchLatestReleaseInfo("Project-Babble", "Baballonia");
            _fetchedLatest = res;
        }
        catch
        {
            return null;
        }

        var latestVersion = Utils.FindVersionInString(res.tag_name);

        return latestVersion;
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

    public void NavigateToLatestWebPage()
    {
        string url = "https://github.com/Project-Babble/Baballonia/releases/latest";
        Utils.OpenUrl(url);
    }

}
