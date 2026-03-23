using Newtonsoft.Json;
using TShockAPI;

namespace LootSync;

public class Configuration
{
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "LootSync.json");

    [JsonProperty("Enable")]
    public bool Enable { get; set; } = true;

    [JsonProperty("ShowLootMessage")]
    public bool ShowLootMessage { get; set; } = true;

    [JsonProperty("ChestProtection")]
    public bool ChestProtection { get; set; } = true;

    public static Configuration Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var config = new Configuration();
                config.Save();
                return config;
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonConvert.DeserializeObject<Configuration>(json) ?? new Configuration();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[LootSync] Error loading config: {ex.Message}");
            return new Configuration();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[LootSync] Error saving config: {ex.Message}");
        }
    }
}