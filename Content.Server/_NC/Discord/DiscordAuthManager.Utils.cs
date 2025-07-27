using System.Text.Json.Serialization;
using Content.Shared._NC.DiscordAuth;

namespace Content.Server._NC.Discord;

public sealed partial class DiscordAuthManager
{
    private DiscordData CreateError(string localizationKey)
    {
        return new DiscordData(false, null, Loc.GetString(localizationKey));
    }

    private DiscordData UnauthorizedErrorData()
    {
        return CreateError("st-not-authorized-error-text");
    }

    private DiscordData NotInGuildErrorData()
    {
        return CreateError("st-not-in-guild");
    }

    private DiscordData EmptyResponseErrorData()
    {
        return CreateError("st-service-response-empty");
    }

    private DiscordData ServiceUnreachableErrorData()
    {
        return CreateError("st-service-unreachable");
    }

    private DiscordData UnexpectedErrorData()
    {
        return CreateError("st-unexpected-error");
    }

    private sealed class DiscordUuidResponse
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = null!;

        [JsonPropertyName("discord_id")]
        public string DiscordId { get; set; } = null!;
    }

    private sealed class DiscordGuildsResponse
    {
        [JsonPropertyName("guilds")]
        public DiscordGuild[] Guilds { get; set; } = [];
    }

    private sealed class DiscordGuild
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;
    }

    private sealed class DiscordLinkResponse
    {
        [JsonPropertyName("link")]
        public string Link { get; set; } = default!;
    }
}
