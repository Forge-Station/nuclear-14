using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Content.Server._NC.Discord;
using Content.Server._NC.CCCvars;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Content.Shared._NC.Sponsors;
using System.Text.Json.Serialization;
using System.Linq;

namespace Content.Server._NC.Sponsors;

public sealed class SponsorsManager : ISponsorsManager
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly DiscordAuthManager _discordAuthManager = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    private ISawmill _sawmill = default!;

    private readonly HttpClient _httpClient = new();
    private string _guildId = default!;
    private string _apiUrl = default!;
    private string _apiKey = default!;
    private bool _enabled;

    private Dictionary<NetUserId, SponsorData> _cachedSponsors = new();

    public void Initialize()
    {
        _configuration.OnValueChanged(CCCVars.DiscordAuthEnabled, val => { _enabled = val; }, true);
        _configuration.OnValueChanged(CCCVars.DiscordGuildID, s => _guildId = s, true);
        _configuration.OnValueChanged(CCCVars.DiscordApiUrl, s => _apiUrl = s, true);
        _configuration.OnValueChanged(CCCVars.ApiKey, value => _apiKey = value, true);

        _discordAuthManager.PlayerVerified += OnPlayerVerified;
        _netManager.Disconnect += OnDisconnect;

        _sawmill = Logger.GetSawmill("sponsors");
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs e)
    {
        _cachedSponsors.Remove(e.Channel.UserId);
    }

    private async void OnPlayerVerified(object? sender, ICommonSession e)
    {
        if (!_enabled)
            return;

        var roles = await GetRoles(e.UserId);
        if (roles is null)
            return;

        var level = SponsorData.ParseRoles(roles);

        var data = new SponsorData(level, e.UserId);

        _cachedSponsors.Add(e.UserId, data);

        _sawmill.Info($"{e.UserId} is sponsor now.\nUserId: {e.UserId}. Level: {Enum.GetName(data.Level)}:{(int) data.Level}");

    }

    private async Task<List<string>?> GetRoles(NetUserId userId)
    {
        var requestUrl = $"{_apiUrl}/roles?method=uid&id={userId}&guildId={_guildId}";
        var response = await _httpClient.GetAsync(requestUrl);

        if (!response.IsSuccessStatusCode)
        {
            _sawmill.Error($"Failed to retrieve roles for user {userId}: {response.StatusCode}");
            return null;
        }

        var responseContent = await response.Content.ReadFromJsonAsync<RolesResponse>();

        if (responseContent is not null)
            return responseContent.Roles.ToList();


        _sawmill.Error($"Roles not found in response for user {userId}");
        return null;
    }

    public bool TryGetSponsorData(NetUserId userId, [NotNullWhen(true)] out SponsorData? sponsorData)
    {
        return _cachedSponsors.TryGetValue(userId, out sponsorData);
    }

    public bool TryGetSponsorColor(SponsorLevel level, [NotNullWhen(true)] out string? color)
    {
        return SponsorData.SponsorColor.TryGetValue(level, out color);
    }

    public bool TryGetSponsorGhost(SponsorLevel level, [NotNullWhen(true)] out string? ghost)
    {
        return SponsorData.SponsorGhost.TryGetValue(level, out ghost);
    }

    private sealed class RolesResponse
    {
        [JsonPropertyName("roles")]
        public string[] Roles { get; set; } = [];
    }
}
