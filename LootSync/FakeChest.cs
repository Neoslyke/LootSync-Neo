using Terraria;

namespace LootSync;

public class FakeChest
{
    public int x;
    public int y;
    public Item[] item;

    public FakeChest(int x, int y)
    {
        this.x = x;
        this.y = y;
        this.item = new Item[40];
        
        for (int i = 0; i < this.item.Length; i++)
        {
            this.item[i] = new Item();
        }
    }
}