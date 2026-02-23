using Baballonia.Models;
using Baballonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class AboutPageViewModel(GithubService githubService) : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpandedByDefault = true;

    [ObservableProperty]
    private bool _isLoadingContributors;

    [ObservableProperty]
    private ObservableCollection<GithubContributor> _contributors = [];

    public async Task LoadContributorsAsync()
    {
        if (IsLoadingContributors || Contributors.Any())
            return;

        try
        {
            IsLoadingContributors = true;
            var contributors = await githubService.GetContributors("Project-Babble", "Baballonia");
            var sortedContributors = contributors.OrderByDescending(c => c.Contributions).ToList();

            foreach (var contributor in sortedContributors)
            {
                Contributors.Add(contributor);
            }
        }
        finally
        {
            IsLoadingContributors = false;
        }
    }
}
