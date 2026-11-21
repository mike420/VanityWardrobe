using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VanityWardrobe;

// What the player has to craft with and what crafting something takes. The window's costs and the pinned recipes both use this. "Have"
// counts the player's inventory plus the containers within the Chest Radius setting.
internal static class Stock
{
    private static List<Inventory> s_inventories = new List<Inventory>();
    private static float s_nextRefresh;

    // Looks again for the containers near the player
    public static void Refresh()
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            s_inventories = new List<Inventory>();
            return;
        }
        List<Inventory> found = new List<Inventory> { player.GetInventory() };
        float radius = MainConfig.chestRadius.Value;
        if (radius > 0f)
        {
            foreach (Container container in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                Inventory inventory = container.GetInventory();
                if (inventory != null && container.m_nview != null && container.m_nview.IsValid() && Vector3.Distance(container.transform.position, player.transform.position) <= radius)
                {
                    found.Add(inventory);
                }
            }
        }
        s_inventories = found;
    }

    // Refresh, but not more often than this many seconds
    public static void RefreshIfDue(float seconds)
    {
        if (Time.unscaledTime >= s_nextRefresh)
        {
            s_nextRefresh = Time.unscaledTime + seconds;
            Refresh();
        }
    }

    public static int Have(ItemDrop item)
    {
        string name = item.m_itemData.m_shared.m_name;
        int count = 0;
        foreach (Inventory inventory in s_inventories)
        {
            count += inventory.CountItems(name);
        }
        return count;
    }

    // The materials for crafting these items and upgrading each to the quality (as far as it goes), in name order. An item with no recipe
    // adds nothing.
    public static List<KeyValuePair<ItemDrop, int>> Needed(IEnumerable<string> prefabs, int quality)
    {
        Dictionary<ItemDrop, int> need = new Dictionary<ItemDrop, int>();
        if (ObjectDB.instance == null)
        {
            return new List<KeyValuePair<ItemDrop, int>>();
        }
        foreach (string prefab in prefabs)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            Recipe recipe = drop != null ? ObjectDB.instance.GetRecipe(drop.m_itemData) : null;
            if (recipe == null)
            {
                continue;
            }
            int levels = Mathf.Min(quality, drop.m_itemData.m_shared.m_maxQuality);
            for (int level = 1; level <= levels; level++)
            {
                foreach (Piece.Requirement requirement in recipe.m_resources)
                {
                    if (requirement.m_resItem == null || (requirement.m_upgraderResource && level == 1))
                    {
                        continue;
                    }
                    need.TryGetValue(requirement.m_resItem, out int sofar);
                    need[requirement.m_resItem] = sofar + requirement.GetAmount(level);
                }
            }
        }
        return need.OrderBy(p => p.Key.name).ToList();
    }
}
