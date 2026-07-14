using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

public sealed class DdoTrackerApiClient : IDisposable
{
    public const string DefaultBaseUrl = "https://ddotracker.zepsu.com/api/plugin";
    public const string PluginTokenLabel = "dungeonhelper";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public DdoTrackerApiClient(string? baseUrl = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri((baseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void SetBearerToken(string? token)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    public async Task<DdoTrackerLoginResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var payload = new DdoTrackerLoginRequest
        {
            Email = email.Trim(),
            Password = password,
            Label = PluginTokenLabel
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            "auth/login",
            payload,
            authenticated: false,
            ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Login failed");

        var parsed = JsonSerializer.Deserialize<DdoTrackerLoginResponse>(body, JsonOptions)
            ?? throw new DdoTrackerApiException((int)response.StatusCode, "Login response was empty", body);

        if (string.IsNullOrWhiteSpace(parsed.Token))
            throw new DdoTrackerApiException((int)response.StatusCode, "Login response did not include a token", body);

        return parsed;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "auth/logout", null, authenticated: true, ct)
            .ConfigureAwait(false);
        _ = response;
    }

    public async Task<DdoTrackerUser> GetMeAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "auth/me", null, authenticated: true, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not load account");

        var user = ExtractUser(body);
        if (user == null)
            throw new DdoTrackerApiException((int)response.StatusCode, "Account payload was empty", body);

        return user;
    }

    public async Task<IReadOnlyList<DdoTrackerCharacter>> GetCharactersAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "characters", null, authenticated: true, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not load characters");

        return ExtractCharacters(body);
    }

    public async Task<IReadOnlyList<DdoTrackerCharacter>> FindCharactersAsync(
        string? givenName = null,
        string? surname = null,
        string? name = null,
        string? server = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(givenName))
            query.Add($"givenName={Uri.EscapeDataString(givenName.Trim())}");
        if (!string.IsNullOrWhiteSpace(surname))
            query.Add($"surname={Uri.EscapeDataString(surname.Trim())}");
        if (!string.IsNullOrWhiteSpace(name))
            query.Add($"name={Uri.EscapeDataString(name.Trim())}");
        if (!string.IsNullOrWhiteSpace(server))
            query.Add($"server={Uri.EscapeDataString(server.Trim())}");

        var path = query.Count == 0 ? "characters/find" : $"characters/find?{string.Join("&", query)}";
        using var response = await SendAsync(HttpMethod.Get, path, null, authenticated: true, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not find characters");

        return ExtractCharacters(body);
    }

    public async Task<DdoTrackerCharacter> CreateCharacterAsync(
        DdoTrackerCharacterUpsertRequest request,
        CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "characters", request, authenticated: true, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not create character");

        return ExtractCharacter(body)
            ?? throw new DdoTrackerApiException((int)response.StatusCode, "Create character response was empty", body);
    }

    public async Task<DdoTrackerCharacter> UpdateCharacterAsync(
        string characterId,
        DdoTrackerCharacterUpsertRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("Character id is required.", nameof(characterId));

        using var response = await SendAsync(
            HttpMethod.Put,
            $"characters/{Uri.EscapeDataString(characterId.Trim())}",
            request,
            authenticated: true,
            ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not update character");

        return ExtractCharacter(body)
            ?? throw new DdoTrackerApiException((int)response.StatusCode, "Update character response was empty", body);
    }

    public async Task NoteCharacterLoginAsync(string characterId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("Character id is required.", nameof(characterId));

        using var response = await SendAsync(
            HttpMethod.Post,
            $"characters/{Uri.EscapeDataString(characterId.Trim())}/login",
            new { lastLoginAt = "now" },
            authenticated: true,
            ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not note character login");
    }

    public async Task PostCompletionAsync(
        string characterId,
        DdoTrackerCompletionRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            throw new ArgumentException("Character id is required.", nameof(characterId));

        using var response = await SendAsync(
            HttpMethod.Post,
            $"characters/{Uri.EscapeDataString(characterId.Trim())}/completions",
            request,
            authenticated: true,
            ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not sync quest completion");
    }

    /// <summary>
    /// Full quest catalog from GET /quests (<c>{ count, quests: [...] }</c>).
    /// </summary>
    public async Task<string> GetQuestsJsonAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "quests", null, authenticated: true, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response, body, "Could not load quest catalog");

        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        bool authenticated,
        CancellationToken ct)
    {
        if (authenticated && _http.DefaultRequestHeaders.Authorization == null)
            throw new DdoTrackerApiException(401, "Not signed in to DDO Tracker");

        using var request = new HttpRequestMessage(method, relativePath);
        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static DdoTrackerApiException CreateApiException(HttpResponseMessage response, string body, string fallback)
    {
        var message = ExtractErrorMessage(body) ?? fallback;
        return new DdoTrackerApiException((int)response.StatusCode, message, body);
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                    return error.GetString();
                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }
        }
        catch
        {
            // Fall through.
        }

        return body.Length <= 200 ? body : body[..200];
    }

    private static DdoTrackerUser? ExtractUser(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("user", out var userElement))
                return userElement.Deserialize<DdoTrackerUser>(JsonOptions);

            return root.Deserialize<DdoTrackerUser>(JsonOptions);
        }
        catch
        {
            return JsonSerializer.Deserialize<DdoTrackerUser>(body, JsonOptions);
        }
    }

    private static DdoTrackerCharacter? ExtractCharacter(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("character", out var character))
                    return character.Deserialize<DdoTrackerCharacter>(JsonOptions);

                return root.Deserialize<DdoTrackerCharacter>(JsonOptions);
            }
        }
        catch
        {
            // Fall through.
        }

        return JsonSerializer.Deserialize<DdoTrackerCharacter>(body, JsonOptions);
    }

    private static IReadOnlyList<DdoTrackerCharacter> ExtractCharacters(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            JsonElement arrayElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("characters", out var characters))
                    arrayElement = characters;
                else if (root.TryGetProperty("data", out var data))
                    arrayElement = data;
                else if (root.TryGetProperty("items", out var items))
                    arrayElement = items;
                else
                    return Array.Empty<DdoTrackerCharacter>();
            }
            else
            {
                return Array.Empty<DdoTrackerCharacter>();
            }

            return arrayElement.Deserialize<List<DdoTrackerCharacter>>(JsonOptions)
                ?? new List<DdoTrackerCharacter>();
        }
        catch
        {
            return JsonSerializer.Deserialize<List<DdoTrackerCharacter>>(body, JsonOptions)
                ?? new List<DdoTrackerCharacter>();
        }
    }

    public void Dispose() => _http.Dispose();
}
