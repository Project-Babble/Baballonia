using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Baballonia.Services;

public class LocalSettingsService : ILocalSettingsService
{
    public static readonly string DefaultApplicationDataFolder = Utils.IsSupportedDesktopOS ? "ApplicationData" : "";
    public const string DefaultLocalSettingsFile = "LocalSettings.json";

    private readonly string _localApplicationData = Utils.PersistentDataDirectory;
    private readonly string _localSettingsFile;

    private ConcurrentDictionary<string, JsonElement> _settings;
    private readonly DebounceFunction _debouncedSave;
    private readonly object _saveLock = new();

    // Set when the file existed but couldn't be read (e.g. transiently locked): we run on an in-memory
    // config this session and refuse to write, rather than overwrite a possibly-good file with empty.
    private bool _saveBlocked;

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

        _debouncedSave = new DebounceFunction(() =>
        {
            try
            {
                PersistSettings();
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
        var outcome = TryLoad(_localSettingsFile, out var loaded);
        if (outcome is LoadOutcome.Loaded or LoadOutcome.Missing)
        {
            _settings = loaded ?? new ConcurrentDictionary<string, JsonElement>();
            return;
        }

        // The file is present but unreadable/corrupt. Recover the last-known-good backup if we have one.
        if (TryLoad(_localSettingsFile + ".bak", out var fromBackup) == LoadOutcome.Loaded)
        {
            _settings = fromBackup!;
            _logger.LogWarning("Settings file was unreadable; recovered from backup");
            try { File.Copy(_localSettingsFile + ".bak", _localSettingsFile, overwrite: true); }
            catch (Exception e) { _logger.LogError("Could not restore settings from backup: {}", e.Message); }
            return;
        }

        _settings = new ConcurrentDictionary<string, JsonElement>();

        if (outcome == LoadOutcome.Corrupt)
        {
            // Confirmed-bad content with no usable backup: set it aside for recovery, then start fresh.
            // Saving is allowed now — there's nothing salvageable left to overwrite.
            try { File.Move(_localSettingsFile, _localSettingsFile + ".bad", overwrite: true); }
            catch (Exception e) { _logger.LogError("Could not set aside corrupt settings file: {}", e.Message); }
            _logger.LogError("Settings file was corrupt; kept a copy as '.bad' and started fresh");
        }
        else
        {
            // Couldn't read it at all (likely a transient lock). Don't overwrite a possibly-good file.
            _logger.LogError("Settings file could not be read; not overwriting it this session");
            _saveBlocked = true;
        }
    }

    private enum LoadOutcome { Loaded, Missing, Corrupt, Unreadable }

    private LoadOutcome TryLoad(string path, out ConcurrentDictionary<string, JsonElement>? settings)
    {
        settings = null;
        if (!File.Exists(path))
            return LoadOutcome.Missing;

        string json;
        try
        {
            // Synchronous read: ReadAllTextAsync has been observed to hang here indefinitely.
            json = File.ReadAllText(path);
        }
        catch (Exception)
        {
            return LoadOutcome.Unreadable;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            settings = new ConcurrentDictionary<string, JsonElement>();
            return LoadOutcome.Loaded;
        }

        try
        {
            settings = JsonSerializer.Deserialize<ConcurrentDictionary<string, JsonElement>>(json)
                       ?? new ConcurrentDictionary<string, JsonElement>();
            return LoadOutcome.Loaded;
        }
        catch (Exception)
        {
            return LoadOutcome.Corrupt;
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
        // Synchronous by design: this runs during shutdown, just before the process may be force-
        // terminated (the camera teardown can wedge a native thread, so we don't get a clean exit to
        // flush on). The write is atomic, so a kill mid-write can't truncate the live settings file.
        _debouncedSave.Cancel();
        try
        {
            PersistSettings();
        }
        catch (Exception e)
        {
            _logger.LogError("Could not save settings file: {}", e);
        }
    }

    private void PersistSettings()
    {
        if (_saveBlocked)
            return;

        lock (_saveLock)
        {
            var json = JsonSerializer.Serialize(_settings, _jsonSerializerOptions);
            WriteAllTextAtomic(_localSettingsFile, json);
        }

        _logger.LogInformation("Saving settings");
    }

    // Write via a sibling temp file then atomically rename it over the target, keeping the previous
    // contents as a ".bak". A truncation/abrupt exit can only ever hit the temp, never the live file.
    private void WriteAllTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }

        try
        {
            File.Replace(tmp, path, path + ".bak");
        }
        catch (IOException)
        {
            // Some filesystems reject File.Replace; an overwrite-rename on the same volume is still
            // atomic, just without the backup generation.
            File.Move(tmp, path, overwrite: true);
        }
    }

    public void SaveSetting<T>(string key, T value, bool forceLocal = false)
    {
        if (key == null)
            return;

        JsonElement element;
        try
        {
            element = JsonSerializer.SerializeToElement<T>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError("Cannot save {} setting key: {}", key, ex.Message);
            return;
        }

        // Skip unchanged values: many callers re-save the same setting (VMs persist on init, the
        // camera-connect flow re-saves state per retry), so there's no point churning the writer.
        if (_settings.TryGetValue(key, out var existing) &&
            JsonSerializer.Serialize(existing) == JsonSerializer.Serialize(element))
            return;

        _settings[key] = element;
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
