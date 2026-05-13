using System.Text.Json;
using System.Text.Json.Serialization;

namespace MathMcp;

public sealed class Config
{
    [JsonPropertyName("httpPort")]
    public int HttpPort { get; set; } = 52080;

    [JsonPropertyName("httpsPort")]
    public int HttpsPort { get; set; } = 52443;

    [JsonPropertyName("logLevel")]
    public string LogLevel { get; set; } = "Information";

    [JsonPropertyName("auth")]
    public AuthConfig? Auth { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Config Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to parse {path}");
    }

    public void Save(string path)
    {
        // Atomic write: a power loss or kill mid-write would otherwise leave
        // config.json half-written and unparseable on the next service start.
        // Write to a sibling .tmp file, then atomically rename over the target.
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}

public sealed class AuthConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("bearerToken")]
    public string? BearerToken { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("tokenTtlSeconds")]
    public int TokenTtlSeconds { get; set; } = 3600;
}
