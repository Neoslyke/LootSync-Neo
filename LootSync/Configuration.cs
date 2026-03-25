using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace LootSync
{
    public class Configuration
    {
        [JsonProperty(Order = 0)]
        public bool Enable { get; set; } = true;

        [JsonProperty(Order = 1)]
        public bool ShowLootMessage { get; set; } = true;

        [JsonProperty(Order = 2)]
        public bool ChestProtection { get; set; } = true;

        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "LootSync.json");

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
}