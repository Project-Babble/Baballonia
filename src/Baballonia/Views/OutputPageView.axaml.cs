using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaMiaDev.Services;

namespace AvaloniaMiaDev.Views;

public partial class OutputPageView : UserControl
{
    private static readonly string[] FileOptions = ["*.txt"];

    private readonly ScrollViewer _scrollViewer;
    // private readonly Button _copyButton;
    // private readonly Button _saveButton;

    public OutputPageView()
    {
        InitializeComponent();

        // _copyButton = this.FindControl<Button>("CopyButton")!;
        // _saveButton = this.FindControl<Button>("SaveButton")!;
        _scrollViewer = this.FindControl<ScrollViewer>("LogScroller")!;

        Loaded += OnLoaded;
    }

    public ObservableCollection<string> FilteredLog => OutputPageLogger.FilteredLogs;
    public ObservableCollection<string> AllLog => OutputPageLogger.AllLogs;

    private void CopyRequested(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var logString = AllLog.Aggregate("", (current, log) => current + log + "\n");
            await clipboard.SetTextAsync(logString);
            // _copyButton.Content = "Copied to clipboard.";
        });
    }

    private void SaveRequested(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var file = await TopLevel.GetTopLevel(this)?.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Log File",
                SuggestedFileName = "log.txt",
                DefaultExtension = "txt",
                FileTypeChoices =
                [
                    new FilePickerFileType("Text File")
                    {
                        Patterns = FileOptions
                    }
                ]
            })!;

            if (file is not null)
            {
                await using var stream = await file.OpenWriteAsync();
                await using var streamWriter = new StreamWriter(stream);
                var logString = AllLog.Aggregate("", (current, log) => current + log + "\n");
                await streamWriter.WriteLineAsync(logString);
                // _saveButton.Content = $"File {file.Name} was saved.";
            }
            // _saveButton.Content = "Operation cancelled.";
        });
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _scrollViewer.ScrollToEnd();

        // We need to subscribe to the observablecollection onchanged event to scroll to the bottom. Note that we need a small delay because windows.
        // If we don't then we'll be scrolling a line too short.
        FilteredLog.CollectionChanged += (_, _) =>
        {
            // Start a timer for 1ms to scroll to the bottom
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _scrollViewer.ScrollToEnd();
            };
            timer.Start();
        };
    }

    ~OutputPageView()
    {
        Loaded -= OnLoaded;
    }
}
