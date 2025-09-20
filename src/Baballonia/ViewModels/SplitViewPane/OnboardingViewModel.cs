using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Baballonia.Services;
using Baballonia.Contracts;

namespace Baballonia.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly ILocalSettingsService _localSettingsService;

    [ObservableProperty]
    private int _currentSlideIndex;

    [ObservableProperty]
    private string _currentSlideName;

    [ObservableProperty]
    private string _nextButtonText = "Next";

    [ObservableProperty] private string _babbleFirmwareDocs =
        "https://docs.babble.diy/docs/hardware/Firmware";

    [ObservableProperty] private string _youtubeLink =
        "https://www.youtube.com/watch?v=iPRabTew0KU";

    [ObservableProperty]
    private bool _canGoBack;

    public ObservableCollection<SlideIndicator> SlideIndicators { get; } = new();

    public ICommand NextCommand { get; private set; }
    public ICommand FinishCommand { get; private set; }

    public OnboardingViewModel()
    {
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();

        // Initialize commands
        NextCommand = new RelayCommand(GoToNext);
        FinishCommand = new RelayCommand(FinishOnboarding);

        // Initialize slide indicators
        for (int i = 0; i < 5; i++) // 5 slides total
        {
            SlideIndicators.Add(new SlideIndicator { IsActive = false });
        }

        UpdateCurrentSlide();
    }

    public void Initialize()
    {
        // Load the user preference
        var showOnStartup = _localSettingsService.ReadSetting<bool>("ShowOnboardingOnStartup");
    }

    private void UpdateCurrentSlide()
    {
        CurrentSlideName = CurrentSlideIndex switch
        {
            0 => "Welcome!",
            1 => "Firmware",
            2 => "Assembly",
            3 => "UI Overview",
            4 => "Finished!",
            _ => "Welcome"
        };

        // Update indicators
        for (int i = 0; i < SlideIndicators.Count; i++)
        {
            SlideIndicators[i].IsActive = (i == CurrentSlideIndex);
        }

        // Update button states
        CanGoBack = CurrentSlideIndex > 0;
        NextButtonText = CurrentSlideIndex == SlideIndicators.Count - 1 ? "Finish" : "Next";
    }

    public void GoToPrevious()
    {
        if (CurrentSlideIndex > 0)
        {
            CurrentSlideIndex--;
            UpdateCurrentSlide();
        }
    }

    private void GoToNext()
    {
        if (CurrentSlideIndex < SlideIndicators.Count - 1)
        {
            CurrentSlideIndex++;
            UpdateCurrentSlide();
        }
        else
        {
            FinishOnboarding();
        }
    }

    public void OpenBabbleModuleUrl()
    {
        Utils.OpenUrl(BabbleFirmwareDocs);
    }

    public void OpenYoutubeUrl()
    {
        Utils.OpenUrl(YoutubeLink);
    }

    private async void FinishOnboarding()
    {
        // Save the user preference
        _localSettingsService.SaveSetting("ShowOnboardingOnStartup", false);

        // Raise completed event to close the overlay
        OnboardingCompleted?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler OnboardingCompleted;
}

public class SlideIndicator : ObservableObject
{
    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }
}
