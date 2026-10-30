using System.Text.Json;
using VRCFaceTracking.Core.Contracts.Services;
using IBabbleSettingsService = Baballonia.Contracts.ILocalSettingsService;

namespace Baballonia.NativeVrcft;

/// <summary>
/// Stores VRCFaceTracking's settings in Baballonia's settings file. Keys are prefixed, as VRCFT's
/// OSC target uses the same key names ("OSCOutPort", ...) as the OSC target for the VRCFT module.
/// </summary>
public class VrcftSettingsAdapter(IBabbleSettingsService settings) : ILocalSettingsService
{
    private const string KeyPrefix = "VRCFT_";

    public Task<T> ReadSettingAsync<T>(string key, T? defaultValue = default, bool forceLocal = false) =>
        Task.FromResult(settings.ReadSetting(KeyPrefix + key, defaultValue, forceLocal)!);

    public Task SaveSettingAsync<T>(string key, T value, bool forceLocal = false)
    {
        settings.SaveSetting(KeyPrefix + key, value, forceLocal);
        return Task.CompletedTask;
    }

    public Task Save(object target)
    {
        foreach (var (property, attribute) in GetSavedProperties(target))
        {
            settings.SaveSetting(KeyPrefix + attribute.GetName(), property.GetValue(target), attribute.ForceLocal());
        }

        return Task.CompletedTask;
    }

    public Task Load(object target)
    {
        foreach (var (property, attribute) in GetSavedProperties(target))
        {
            var setting = settings.ReadSetting<JsonElement>(KeyPrefix + attribute.GetName(), default, attribute.ForceLocal());
            var value = setting.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? attribute.Default()
                : setting.Deserialize(property.PropertyType);

            if (value != null)
            {
                property.SetValue(target, value);
            }
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<(System.Reflection.PropertyInfo, SavedSettingAttribute)> GetSavedProperties(object target) =>
        target.GetType().GetProperties()
            .Select(p => (p, attribute: p.GetCustomAttributes(typeof(SavedSettingAttribute), false).FirstOrDefault() as SavedSettingAttribute))
            .Where(x => x.attribute != null)
            .Select(x => (x.p, x.attribute!));
}

public class VrcftDispatcherAdapter(Baballonia.Contracts.IDispatcherService dispatcher) : IDispatcherService
{
    public void Run(Action action) => dispatcher.Run(action);
}
