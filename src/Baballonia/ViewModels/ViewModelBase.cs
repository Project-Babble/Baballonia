using CommunityToolkit.Mvvm.ComponentModel;

namespace Baballonia.ViewModels;

public partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    protected string _quickstartGuide = "https://docs.babble.diy/docs/babbleofficaltracker";

    [ObservableProperty]
    protected string _babbleDiscord = "https://discord.gg/XV4aVN8n";

    [ObservableProperty]
    protected string _pishockDiscord = "https://discord.gg/Pishock";

    [ObservableProperty]
    protected string _babbleDocs = "https://docs.babble.diy/docs/";

    [ObservableProperty]
    protected string _babbleSite = "https://babble.diy/";

    [ObservableProperty]
    protected string _babbleBlog = "https://docs.babble.diy/blog";

    [ObservableProperty]
    protected string _babbleDataPrivacyPolicy = "https://docs.babble.diy/docs/dataprivacy";

    [ObservableProperty]
    protected string _youtubeLink = "https://www.youtube.com/watch?v=iPRabTew0KU";

    [ObservableProperty]
    protected string _babbleTwitter = "https://x.com/projectBabbleVR";

    [ObservableProperty]
    protected string _baballoniaGithub = "https://github.com/Project-Babble/Baballonia";

    [ObservableProperty]
    protected string _baballoniaGithubIssues = "https://github.com/Project-Babble/Baballonia/issues";

    [ObservableProperty]
    protected string _baballoniaLicense = "https://github.com/Project-Babble/Baballonia/blob/main/LICENSE";

    [ObservableProperty]
    protected string _baballoniaBuildInstructions = "https://docs.babble.diy/docs/developers/sourceCode/csharp";

}
