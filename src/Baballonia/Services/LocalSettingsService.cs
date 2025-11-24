using System;
using System.Collections.Concurrent;
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

    private ConcurrentDictionary<string, JsonElement> _settings;
    private readonly DebounceFunction _debouncedSave;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger<LocalSettingsService> _logger;

    public LocalSettingsService(IOptions<LocalSettingsOptions> options, ILogger<LocalSettingsService> logger)
    {
        _logger = logger;
        var opt = options.Value;

        var applicationDataFolder =
            Path.Combine(_localApplicationData, opt.ApplicationDataFolder ?? DefaultApplicationDataFolder);
        _localSettingsFile = opt.LocalSettingsFile ?? Path.Combine(applicationDataFolder, DefaultLocalSettingsFile);

        _debouncedSave = new DebounceFunction(async void () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, _jsonSerializerOptions);
                // Despite what the docs say, the below does not in fact create a new file if it does not exist
                // However, we can safely assume this file will always exist (created by App.axaml.cs)
                await File.WriteAllTextAsync(_localSettingsFile, json);
                logger.LogInformation("Saving settings");
            }
            catch (Exception e)
            {
                logger.LogError("Could not save settings file: {}", e);
            }
        }, 2000);

        _settings = new ConcurrentDictionary<string, JsonElement>();

        Initialize();
    }

    private void Initialize()
    {
        if (!File.Exists(_localSettingsFile))
        {
            _settings = new ConcurrentDictionary<string, JsonElement>();
            return;
        }

        try
        {
            // I've observed this await File.ReadAllTextAsync can just hang forever here
            // So, it's a File.ReadAllText for now
            var json = File.ReadAllText(_localSettingsFile);
            _settings = JsonSerializer.Deserialize<ConcurrentDictionary<string, JsonElement>>(json)
                        ?? new ConcurrentDictionary<string, JsonElement>();
        }
        catch (Exception)
        {
            _settings = new ConcurrentDictionary<string, JsonElement>();
        }
    }

    public T? ReadSetting<T>(string key, T? defaultValue = default, bool forceLocal = false)
    {
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

    public void SaveSetting<T>(string key, T value, bool forceLocal = false)
    {
        if (key == null)
            return;

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

    public void Load(object instance)
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
                var setting =
                    ReadSetting<JsonElement>(settingName, default, savedSettingAttribute.ForceLocal());
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

    public void Save(object instance)
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

            SaveSetting(settingName, property.GetValue(instance), savedSettingAttribute.ForceLocal());
        }
    }
}
