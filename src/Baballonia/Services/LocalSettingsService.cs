using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baballonia.Services;

public class LocalSettingsService : ILocalSettingsService
{
    public const string DefaultApplicationDataFolder = "ApplicationData";
    public const string DefaultLocalSettingsFile = "LocalSettings.json";

    private readonly string _localApplicationData = Utils.PersistentDataDirectory;
    private readonly string _localSettingsFile;

    private Dictionary<string, JsonElement> _settings;
    private readonly DebounceFunction _debouncedSave;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly Task _isInitializedTask;
    private readonly ILogger<LocalSettingsService> _logger;

    public LocalSettingsService(IOptions<LocalSettingsOptions> options, ILogger<LocalSettingsService> logger)
    {
        var opt = options.Value;

        _logger = logger;
        var applicationDataFolder =
            Path.Combine(_localApplicationData, opt.ApplicationDataFolder ?? DefaultApplicationDataFolder);
        _localSettingsFile = opt.LocalSettingsFile ?? Path.Combine(applicationDataFolder, DefaultLocalSettingsFile);

        _debouncedSave = new DebounceFunction(async () =>
        {
            var json = JsonSerializer.Serialize(_settings, _jsonSerializerOptions);
            // Despite what the docs say, the below does not in fact create a new file if it does not exist
            // However, we can safely assume this file will always exist (created by App.axaml.cs)
            await File.WriteAllTextAsync(_localSettingsFile, json);
            logger.LogInformation("Saving settings");
        }, 2000);

        _settings = new Dictionary<string, JsonElement>();

        _isInitializedTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (!File.Exists(_localSettingsFile))
        {
            _settings = new Dictionary<string, JsonElement>();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_localSettingsFile);
            _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                        ?? new Dictionary<string, JsonElement>();
        }
        catch (Exception)
        {
            _settings = new Dictionary<string, JsonElement>();
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false)
    {
        await _isInitializedTask;

        try
        {
            if (_settings.TryGetValue(key, out var obj))
            {
                return obj.Deserialize<T>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Cannot load {} setting key: {}", key, ex.Message);
        }

        return defaultValue;
    }

    public void ForceSave()
    {
        _debouncedSave.Force();
    }

    public async Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
    {
        if (key == null)
            return;
        await _isInitializedTask;

        try
        {
            _settings[key] = JsonSerializer.SerializeToElement<T>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError("Cannot save {} setting key: {}", key, ex.Message);
            return;
        }

        _debouncedSave.Call();
    }

    public async Task Load(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();
            var defaultValue = savedSettingAttribute.Default();

            try
            {
                var setting = await ReadSettingAsync<JsonElement>(settingName, default, savedSettingAttribute.ForceLocal());
                if (setting.ValueKind != JsonValueKind.Undefined && setting.ValueKind != JsonValueKind.Null)
                {
                    var value = setting.Deserialize(property.PropertyType);
                    property.SetValue(instance, value);
                }
                else if (defaultValue != null)
                {
                    property.SetValue(instance, defaultValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading setting {SettingName}", settingName);
                if (defaultValue != null)
                {
                    property.SetValue(instance, defaultValue);
                }
            }
        }
    }

    public async Task Save(object instance)
    {
        var type = instance.GetType();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes(typeof(SavedSettingAttribute), false);

            if (attributes.Length <= 0)
            {
                continue;
            }

            var savedSettingAttribute = (SavedSettingAttribute)attributes[0];
            var settingName = savedSettingAttribute.GetName();

            await SaveSettingAsync(settingName, property.GetValue(instance), savedSettingAttribute.ForceLocal());
        }
    }
}
