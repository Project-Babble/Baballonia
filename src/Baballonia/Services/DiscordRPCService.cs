using Baballonia.Contracts;
using DiscordRPC;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class DiscordRPCService
{
    private readonly DiscordRpcClient? _client;
    private readonly bool _isInitialized;
    private readonly ILogger<DiscordRPCService>? _logger;

    public DiscordRPCService(ILogger<DiscordRPCService>? logger, ILocalSettingsService localSettingsService)
    {
        _logger = logger;

        if (!localSettingsService.ReadSetting<bool>("AppSettings_UseDiscordRPC"))
            return;

        if (_isInitialized)
        {
            _logger?.LogWarning("Discord RPC already initialized.");
            return;
        }

        const string applicationId = "1457501538523287562";
        _client = new DiscordRpcClient(applicationId);

        _client.OnReady += (_, _) =>
        {
            _logger?.LogInformation("Discord RPC connected.");

            // For the time being just set RPC to our logo
            UpdatePresence();
        };

        _client.OnError += (_, e) =>
        {
            _logger?.LogError("Discord RPC error: {ArgsMessage}", e.Message);
        };

        _client.Initialize();
        _isInitialized = true;
    }

    private void UpdatePresence(string? largeImageKey = "babblelogo")
    {
        if (!_isInitialized || _client == null)
        {
            _logger?.LogError("Discord RPC not initialized. Call Initialize() first.");
            return;
        }

        var presence = new RichPresence();

        if (!string.IsNullOrEmpty(largeImageKey))
        {
            presence.Assets = new DiscordRPC.Assets
            {
                LargeImageKey = largeImageKey,
            };
        }

        _client.SetPresence(presence);
    }

    ~DiscordRPCService()
    {
        if (_client != null)
        {
            _client.ClearPresence();
            _client.Dispose();
        }
        _logger?.LogInformation("Discord RPC client disposed");
    }
}
