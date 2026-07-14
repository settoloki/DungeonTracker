using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonTracker.Models;

public sealed class DdoTrackerSettings
{
    public string? Token { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public bool AutoSync { get; set; } = true;
    public string? SelectedCharacterId { get; set; }
    public string? SelectedCharacterName { get; set; }
    public List<DdoTrackerPendingCompletion> Pending { get; set; } = new();
}

public sealed class GameAccountCharacter
{
    public string Name { get; init; } = string.Empty;
    public string GivenName { get; init; } = string.Empty;
    public string? Surname { get; init; }
    public string? Server { get; init; }
    public string? Race { get; init; }
    public string? Gender { get; init; }
    public string? Alignment { get; init; }
    public string? Guild { get; init; }
    public int? Level { get; init; }
    public int? EpicLevels { get; init; }
    public ulong? GameCharacterId { get; init; }
    public IReadOnlyList<DdoTrackerClassLevel> Classes { get; init; } = Array.Empty<DdoTrackerClassLevel>();
}

public sealed class DdoTrackerClassLevel
{
    public string Name { get; set; } = string.Empty;
    public int Levels { get; set; }
}

public sealed class DdoTrackerPendingCompletion
{
    public string CharacterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Heroic";
    public string Setting { get; set; } = "elite";
    public int? DurationSeconds { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
}

public sealed class DdoTrackerLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public sealed class DdoTrackerLoginResponse
{
    public string? Token { get; set; }

    // API returns a number; accept string or number.
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? TokenId { get; set; }

    public string? Label { get; set; }
    public DdoTrackerUser? User { get; set; }
}

public sealed class DdoTrackerUser
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class DdoTrackerCharacter
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? GivenName { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Race { get; set; }
    public string? Gender { get; set; }
    public string? Alignment { get; set; }
    public string? Server { get; set; }
    public string? ServerName { get; set; }
    public string? Guild { get; set; }
    public List<DdoTrackerClassLevel>? Classes { get; set; }
    public string? ClassSummary { get; set; }
    public int? EpicLevels { get; set; }
    public int? HeroicLevel { get; set; }
    public int? TotalLevel { get; set; }
    public int? CharacterLevel { get; set; }
    public int? MinLevel { get; set; }

    public override string ToString()
    {
        var server = ServerName ?? Server;
        var level = TotalLevel ?? CharacterLevel;
        var label = string.IsNullOrWhiteSpace(server) ? Name : $"{Name} ({server})";
        return level is > 0 ? $"{label} · L{level}" : label;
    }
}

public sealed class DdoTrackerCharacterUpsertRequest
{
    public string? Name { get; set; }
    public string? GivenName { get; set; }
    public string? Surname { get; set; }
    public string? Race { get; set; }
    public string? Gender { get; set; }
    public string? Alignment { get; set; }
    public string? Server { get; set; }
    public string? Guild { get; set; }
    public List<DdoTrackerClassLevel>? Classes { get; set; }
    public int? EpicLevels { get; set; }
    public string? LastLoginAt { get; set; }
    public bool? TouchLastLogin { get; set; }
}

public sealed class DdoTrackerCompletionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Heroic";
    public string? Setting { get; set; }
    public int? DurationSeconds { get; set; }
    public string? CompletedAt { get; set; }
}

public sealed class DdoTrackerApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public DdoTrackerApiException(int statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

internal sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number when reader.TryGetInt64(out var longValue) => longValue.ToString(),
            JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => string.Empty
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
