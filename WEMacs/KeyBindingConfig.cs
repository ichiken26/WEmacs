using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace WEMacs;

public sealed class KeyBindingConfig
{
    [JsonPropertyName("bindings")]
    public List<KeyBindingDefinition> Bindings { get; init; } = [];

    public static IReadOnlyDictionary<Keys, KeyBinding> Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Key binding config was not found.", path);

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<KeyBindingConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (config is null)
            throw new InvalidOperationException("Failed to parse key binding config.");

        return config.Bindings
            .Select(ParseBinding)
            .ToDictionary(binding => binding.Key);
    }

    private static KeyBinding ParseBinding(KeyBindingDefinition binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Key))
            throw new InvalidOperationException("Binding key must not be empty.");

        if (!Enum.TryParse<Keys>(binding.Key, ignoreCase: true, out var key))
            throw new InvalidOperationException($"Unknown key in config: {binding.Key}");

        if (string.IsNullOrWhiteSpace(binding.Log))
            throw new InvalidOperationException($"Binding log must not be empty for key: {binding.Key}");

        if (string.IsNullOrWhiteSpace(binding.SendKeys))
            throw new InvalidOperationException($"Binding sendKeys must not be empty for key: {binding.Key}");

        return new KeyBinding(key, binding.Log, binding.SendKeys);
    }
}

public sealed class KeyBindingDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("log")]
    public string Log { get; init; } = string.Empty;

    [JsonPropertyName("sendKeys")]
    public string SendKeys { get; init; } = string.Empty;
}

public sealed record KeyBinding(Keys Key, string Log, string SendKeys);
