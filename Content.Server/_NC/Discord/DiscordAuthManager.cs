using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._NC.CCCvars;
using Content.Server._NC.Sponsors;
using Content.Shared._NC.DiscordAuth;
using Content.Shared._NC.Sponsors;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._NC.Discord;

public sealed partial class DiscordAuthManager : IPostInjectInit
{
    [Dependency] private readonly IServerNetManager _netMgr = default!;
    [Dependency] private readonly IPlayerManager _playerMgr = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SponsorManager _sponsors = default!;

    private ISawmill _sawmill = default!;

    private readonly HttpClient _httpClient = new();

    private bool _enabled = false;
    private string _apiUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _discordGuild = String.Empty;
    public event EventHandler<ICommonSession>? PlayerVerified;

    public void PostInject()
    {
        IoCManager.InjectDependencies(this);
    }

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("discordAuth");

        _cfg.OnValueChanged(CCCVars.DiscordAuthEnabled, v => _enabled = v, true);
        _cfg.OnValueChanged(CCCVars.DiscordApiUrl, v => _apiUrl = v, true);
        _cfg.OnValueChanged(CCCVars.ApiKey, v => _apiKey = v, true);
        _cfg.OnValueChanged(CCCVars.DiscordGuildID, v => _discordGuild = v, true);

        _netMgr.RegisterNetMessage<MsgDiscordAuthRequired>();
        _netMgr.RegisterNetMessage<MsgSyncSponsorData>();
        _netMgr.RegisterNetMessage<MsgDiscordAuthCheck>(OnAuthCheck);
        _netMgr.Disconnect += OnDisconnect;

        _playerMgr.PlayerStatusChanged += OnPlayerStatusChanged;

        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs e)
    {
        _sponsors.Sponsors.Remove(e.Channel.UserId);
    }

    private async void OnAuthCheck(MsgDiscordAuthCheck msg)
    {
        var data = await IsVerified(msg.MsgChannel.UserId);
        if (!data.Status)
            return;

        var session = _playerMgr.GetSessionById(msg.MsgChannel.UserId);
        PlayerVerified?.Invoke(this, session);
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected)
            return;

        if (!_enabled)
        {
            PlayerVerified?.Invoke(this, args.Session);
            return;
        }


        var data = await IsVerified(args.Session.UserId);
        if (data.Status && data.UserData is not null)
        {
            PlayerVerified?.Invoke(this, args.Session);
            return;
        }

        var link = await GenerateLink(args.Session.UserId);
        var message = new MsgDiscordAuthRequired
        {
            Link = link ?? "",
            ErrorMessage = data.ErrorMessage ?? "",
        };
        args.Session.Channel.SendMessage(message);
    }

    public async Task<DiscordData> IsVerified(NetUserId userId, CancellationToken cancel = default)
    {
        _sawmill.Debug($"Player {userId} check Discord verification");

        var requestUrl = $"{_apiUrl}/uuid?method=uid&id={userId}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        // try catch block to catch HttpRequestExceptions due to remote service unavailability
        try
        {
            var response = await _httpClient.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
                return UnauthorizedErrorData();

            var discordUuid = await response.Content.ReadFromJsonAsync<DiscordUuidResponse>(cancel);

            var roles = await GetRoles(userId);
            if (roles == null)
                return EmptyResponseErrorRoleData();

            var isInGuild = await CheckGuild(userId, cancel);
            if (!isInGuild)
                return NotInGuildErrorData();

            if (discordUuid is null)
                return EmptyResponseErrorData();

            var level = SponsorData.ParseRoles(roles);
            if (level != SponsorLevel.None)
            {
                _sponsors.Sponsors.Add(userId, level);
                var session = _playerMgr.GetSessionById(userId);
                var message = new MsgSyncSponsorData
                {
                    UserId = userId,
                    Level = level
                };
                _netMgr.ServerSendMessage(message, session.Channel);
                _sawmill.Info($"{userId} is sponsor now.\nUserId: {userId}. Level: {Enum.GetName(level)}:{(int) level}");
            }

            return new DiscordData(true, new DiscordUserData(userId, discordUuid.DiscordId));
        }
        catch (HttpRequestException)
        {
            _sawmill.Error("Remote auth service is unreachable. Check if its online!");
            return ServiceUnreachableErrorData();
        }
        catch (Exception e)
        {
            _sawmill.Error($"Unexpected error verifying user via auth service. Error: {e.Message}. Stack: \n{e.StackTrace}");
            return UnexpectedErrorData();
        }
    }

    private async Task<bool> CheckGuild(NetUserId userId, CancellationToken cancel = default)
    {
        if (RolesCache.ContainsKey(userId))
            return true;

        var requestUrl = $"{_apiUrl}/guilds?method=uid&id={userId}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        var response = await _httpClient.SendAsync(request, cancel);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30;
            await Task.Delay(TimeSpan.FromSeconds(retryAfter));
            return await CheckGuild(userId);
        }

        if (!response.IsSuccessStatusCode)
            return false;

        var guilds = await response.Content.ReadFromJsonAsync<DiscordGuildsResponse>(cancel);
        if (guilds is null)
            return false;

        return guilds.Guilds.Any(guild => guild.Id == _discordGuild);
    }

    private async Task<List<string>?> GetRoles(NetUserId userId)
    {
        if (RolesCache.TryGetValue(userId, out var cached))
        {
            if (cached.Expiry > DateTimeOffset.Now)
                return cached.Roles;

            RolesCache.Remove(userId);
        }

        var requestUrl = $"{_apiUrl}/roles?method=uid&id={userId}&guildId={_discordGuild}";
        var response = await _httpClient.GetAsync(requestUrl);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30;
            await Task.Delay(TimeSpan.FromSeconds(retryAfter));
            return await GetRoles(userId);
        }

        if (!response.IsSuccessStatusCode)
            return null;

        var responseContent = await response.Content.ReadFromJsonAsync<RolesResponse>();
        if (responseContent == null)
            return null;

        RolesCache[userId] = (DateTimeOffset.Now.AddHours(1), responseContent.Roles.ToList());
        return responseContent.Roles.ToList();
    }

    public async Task<string?> GenerateLink(NetUserId userId, CancellationToken cancel = default)
    {
        _sawmill.Debug($"Generating link for {userId}");
        var requestUrl = $"{_apiUrl}/link?uid={userId}";

        // try catch block to catch HttpRequestExceptions due to remote service unavailability
        try
        {
            var response = await _httpClient.GetAsync(requestUrl, cancel);
            if (!response.IsSuccessStatusCode)
                return null;

            var link = await response.Content.ReadFromJsonAsync<DiscordLinkResponse>(cancel);
            return link!.Link;
        }
        catch (HttpRequestException)
        {
            _sawmill.Error("Remote auth service is unreachable. Check if its online!");
            return null;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Unexpected error verifying user via auth service. Error: {e.Message}. Stack: \n{e.StackTrace}");
            return null;
        }
    }
}
