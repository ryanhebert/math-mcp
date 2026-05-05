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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Config Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to parse {path}");
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(path, json);
    }
}
