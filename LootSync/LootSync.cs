using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace LootSync;

[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public override string Name => "LootSync";
    public override string Author => "Neoslyke, Codian, matheus-fsc";
    public override Version Version => new Version(2, 1, 0);
    public override string Description => "Player loot synchronization.";

    public static Configuration Config { get; private set; } = new();
    public static Database Database { get; private set; } = new();
    private static bool _enabled = true;

    public Plugin(Main game) : base(game) { }

    public override void Initialize()
    {
        Config = Configuration.Load();
        Database = new Database();
        Database.Initialize();

        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        GetDataHandlers.PlaceChest += OnChestPlace;
        GetDataHandlers.ChestOpen += OnChestOpen;
        GetDataHandlers.ChestItemChange += OnChestItemChange;
        GeneralHooks.ReloadEvent += OnReload;

        Commands.ChatCommands.Add(new Command("lootsync.admin", ToggleCommand, "lstoggle")
        {
            HelpText = "Toggles per-player loot functionality."
        });
        Commands.ChatCommands.Add(new Command("lootsync.admin", AddChestCommand, "lsaddchest")
        {
            HelpText = "Marks a chest at coordinates as player-placed (excluded from per-player loot)."
        });
        Commands.ChatCommands.Add(new Command("lootsync.admin", RemoveChestCommand, "lsremchest")
        {
            HelpText = "Removes a chest from player-placed list."
        });
        Commands.ChatCommands.Add(new Command("lootsync.admin", ReloadCommand, "lsreload")
        {
            HelpText = "Reloads the configuration."
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
            GetDataHandlers.PlaceChest -= OnChestPlace;
            GetDataHandlers.ChestOpen -= OnChestOpen;
            GetDataHandlers.ChestItemChange -= OnChestItemChange;
            GeneralHooks.ReloadEvent -= OnReload;

            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == ToggleCommand);
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == AddChestCommand);
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == RemoveChestCommand);
            Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == ReloadCommand);

            Database.SaveAllChests();
        }
        base.Dispose(disposing);
    }

    private void OnWorldSave(WorldSaveEventArgs args)
    {
        Database.SaveAllChests();
    }

    private void OnReload(ReloadEventArgs args)
    {
        Config = Configuration.Load();
        args.Player?.SendSuccessMessage("[LootSync] Configuration reloaded.");
    }

    private void ToggleCommand(CommandArgs args)
    {
        _enabled = !_enabled;
        args.Player.SendSuccessMessage($"[LootSync] Per-player loot is now {(_enabled ? "enabled" : "disabled")}.");
    }

    private void AddChestCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 2 ||
            !int.TryParse(args.Parameters[0], out int x) ||
            !int.TryParse(args.Parameters[1], out int y))
        {
            args.Player.SendErrorMessage("Usage: /lsaddchest <x> <y>");
            return;
        }

        Database.SetChestPlayerPlaced(x, y);
        args.Player.SendSuccessMessage($"[LootSync] Chest at ({x}, {y}) marked as player-placed.");
    }

    private void RemoveChestCommand(CommandArgs args)
    {
        if (args.Parameters.Count < 2 ||
            !int.TryParse(args.Parameters[0], out int x) ||
            !int.TryParse(args.Parameters[1], out int y))
        {
            args.Player.SendErrorMessage("Usage: /lsremchest <x> <y>");
            return;
        }

        if (Database.RemovePlayerPlacedChest(x, y))
            args.Player.SendSuccessMessage($"[LootSync] Chest at ({x}, {y}) removed from player-placed list.");
        else
            args.Player.SendErrorMessage($"[LootSync] Chest at ({x}, {y}) was not in the player-placed list.");
    }

    private void ReloadCommand(CommandArgs args)
    {
        Config = Configuration.Load();
        args.Player.SendSuccessMessage("[LootSync] Configuration reloaded.");
    }

    private void OnChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs e)
    {
        if (!_enabled || !Config.Enable) return;

        var realChest = Main.chest[e.ID];
        if (realChest == null || realChest.bankChest) return;

        if (Database.IsChestPlayerPlaced(realChest.x, realChest.y)) return;

        var item = new Item();
        item.netDefaults(e.Type);
        item.stack = e.Stacks;
        item.prefix = e.Prefix;

        var fakeChest = Database.GetOrCreateFakeChest(e.ID, e.Player.UUID);
        fakeChest.item[e.Slot] = item;

        e.Handled = true;
    }

    private void OnChestOpen(object? sender, GetDataHandlers.ChestOpenEventArgs e)
    {
        if (e.Handled || !_enabled || !Config.Enable) return;

        int chestId = Chest.FindChest(e.X, e.Y);
        if (chestId == -1) return;

        var realChest = Main.chest[chestId];
        if (realChest == null || realChest.bankChest) return;

        if (Database.IsChestPlayerPlaced(realChest.x, realChest.y)) return;

        var fakeChest = Database.GetOrCreateFakeChest(chestId, e.Player.UUID);

        if (Config.ShowLootMessage)
            e.Player.SendInfoMessage("[LootSync] Loot in this chest is saved per-player!");

        for (int slot = 0; slot < fakeChest.item.Length; slot++)
        {
            var item = fakeChest.item[slot];
            byte[] payload = ConstructChestItemPacket(chestId, slot, item);
            e.Player.SendRawData(payload);
        }

        e.Player.SendData(PacketTypes.ChestOpen, "", chestId);
        e.Player.ActiveChest = chestId;
        Main.player[e.Player.Index].chest = chestId;
        e.Player.SendData(PacketTypes.SyncPlayerChestIndex, null, e.Player.Index, chestId);

        e.Handled = true;
    }

    private void OnChestPlace(object? sender, GetDataHandlers.PlaceChestEventArgs e)
    {
        if (!_enabled || !Config.Enable) return;

        int tileX = e.TileX;
        int tileY = e.TileY - 1;

        if (e.Flag == 0)
        {
            if (!Database.IsChestPlayerPlaced(tileX, tileY))
            {
                int chestId = Chest.FindChest(tileX, tileY);
                if (chestId != -1)
                    Main.chest[chestId].item = new Item[Main.chest[chestId].item.Length];
            }
            Database.SetChestPlayerPlaced(tileX, tileY);
        }
        else if (e.Flag == 1)
        {
            bool isPlayerPlaced = Database.IsChestPlayerPlaced(tileX, tileY);

            if (!isPlayerPlaced && Config.ChestProtection)
            {
                e.Player.SendErrorMessage("[LootSync] Loot chests cannot be destroyed! You are that greedy huh?");
                e.Handled = true;
                return;
            }

            if (isPlayerPlaced)
            {
                Database.RemovePlayerPlacedChest(tileX, tileY);
            }
        }
    }

    private static byte[] ConstructChestItemPacket(int chestId, int slot, Item item)
    {
        var memoryStream = new MemoryStream();
        var packetWriter = new OTAPI.PacketWriter(memoryStream);

        packetWriter.BaseStream.Position = 0L;
        long position = packetWriter.BaseStream.Position;

        packetWriter.BaseStream.Position += 2L;
        packetWriter.Write((byte)PacketTypes.ChestItem);

        packetWriter.Write((short)chestId);
        packetWriter.Write((byte)slot);

        short netId = (short)item.type;
        if (item.Name == null)
        {
            netId = 0;
        }

        packetWriter.Write((short)item.stack);
        packetWriter.Write(item.prefix);
        packetWriter.Write(netId);

        int positionAfter = (int)packetWriter.BaseStream.Position;

        packetWriter.BaseStream.Position = position;
        packetWriter.Write((ushort)positionAfter);
        packetWriter.BaseStream.Position = positionAfter;

        return memoryStream.ToArray();
    }
}