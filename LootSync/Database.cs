using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;

namespace LootSync;

public class Database
{
    private static readonly string DbPath = Path.Combine(TShock.SavePath, "LootSync.sqlite");
    private readonly string _connString = $"Data Source={DbPath}";

    private readonly Dictionary<string, Dictionary<int, Chest>> _fakeChests = new();
    private readonly HashSet<(int, int)> _playerPlacedChests = new();

    public void Initialize()
    {
        CreateTables();
        LoadData();
    }

    private void CreateTables()
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Chests (
                Id INTEGER NOT NULL,
                PlayerUuid TEXT NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                Items TEXT NOT NULL,
                PRIMARY KEY (Id, PlayerUuid)
            );

            CREATE TABLE IF NOT EXISTS PlacedChests (
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                PRIMARY KEY (X, Y)
            );
        ";
        cmd.ExecuteNonQuery();
    }

    private void LoadData()
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();

        using (var cmd = new SqliteCommand("SELECT Id, PlayerUuid, X, Y, Items FROM Chests;", conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string playerUuid = reader.GetString(1);
                int chestId = reader.GetInt32(0);
                int x = reader.GetInt32(2);
                int y = reader.GetInt32(3);
                string itemsJson = reader.GetString(4);

                var items = JsonConvert.DeserializeObject<List<ItemData>>(itemsJson) ?? new List<ItemData>();

                var chest = new Chest { x = x, y = y };
                for (int i = 0; i < items.Count && i < Chest.maxItems; i++)
                {
                    var data = items[i];
                    var item = new Item();
                    item.netDefaults(data.Id);
                    item.stack = data.Stack;
                    item.prefix = data.Prefix;
                    chest.item[i] = item;
                }

                if (!_fakeChests.ContainsKey(playerUuid))
                    _fakeChests[playerUuid] = new Dictionary<int, Chest>();

                _fakeChests[playerUuid][chestId] = chest;
            }
        }

        using (var cmd = new SqliteCommand("SELECT X, Y FROM PlacedChests;", conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                _playerPlacedChests.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }
    }

    public void SaveAllChests()
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            foreach (var (playerUuid, chests) in _fakeChests)
            {
                foreach (var (chestId, chest) in chests)
                {
                    var items = chest.item.Select(i => new ItemData
                    {
                        Id = i.type,
                        Stack = i.stack,
                        Prefix = i.prefix
                    }).ToList();

                    string itemsJson = JsonConvert.SerializeObject(items);

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Chests (Id, PlayerUuid, X, Y, Items)
                        VALUES (@id, @uuid, @x, @y, @items);
                    ";
                    cmd.Parameters.AddWithValue("@id", chestId);
                    cmd.Parameters.AddWithValue("@uuid", playerUuid);
                    cmd.Parameters.AddWithValue("@x", chest.x);
                    cmd.Parameters.AddWithValue("@y", chest.y);
                    cmd.Parameters.AddWithValue("@items", itemsJson);
                    cmd.ExecuteNonQuery();
                }
            }

            using (var deleteCmd = conn.CreateCommand())
            {
                deleteCmd.CommandText = "DELETE FROM PlacedChests;";
                deleteCmd.ExecuteNonQuery();
            }

            foreach (var (x, y) in _playerPlacedChests)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO PlacedChests (X, Y) VALUES (@x, @y);";
                cmd.Parameters.AddWithValue("@x", x);
                cmd.Parameters.AddWithValue("@y", y);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            TShock.Log.ConsoleError($"[LootSync] Error saving data: {ex.Message}");
        }
    }

    public Chest GetOrCreateFakeChest(int chestId, string playerUuid)
    {
        if (!_fakeChests.ContainsKey(playerUuid))
            _fakeChests[playerUuid] = new Dictionary<int, Chest>();

        if (!_fakeChests[playerUuid].ContainsKey(chestId))
        {
            var realChest = Main.chest[chestId];
            var fakeChest = new Chest { x = realChest.x, y = realChest.y };
            realChest.item.CopyTo(fakeChest.item, 0);
            _fakeChests[playerUuid][chestId] = fakeChest;
        }

        return _fakeChests[playerUuid][chestId];
    }

    public void SetChestPlayerPlaced(int x, int y)
    {
        _playerPlacedChests.Add((x, y));
    }

    public bool RemovePlayerPlacedChest(int x, int y)
    {
        return _playerPlacedChests.Remove((x, y));
    }

    public bool IsChestPlayerPlaced(int x, int y)
    {
        return _playerPlacedChests.Contains((x, y));
    }

    private class ItemData
    {
        public int Id { get; set; }
        public int Stack { get; set; }
        public byte Prefix { get; set; }
    }
}