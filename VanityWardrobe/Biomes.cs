using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace VanityWardrobe;

// Works out which biome each item belongs to, from the world itself rather than a fixed list, so items and materials from mods are
// placed too. Where a material can be found comes from what the game places in each biome (trees, rocks, pickables, creature
// spawns and their drops, and the contents of locations and dungeons) and from what is smelted from what. An item is as far on as the
// furthest material in its recipe. A material found in several biomes counts as the earliest of them.
//
// Trees, rocks and spawns are read at once. Locations and dungeon rooms are big, so they are loaded one at a time in the background
// once you are in a world, and the result is kept in a file until the game or the mods change.
internal static class Biomes
{
    // The biomes in the order a player reaches them. Ocean comes after the Swamp, where the first sea creatures are met.
    public static readonly Heightmap.Biome[] Order =
    {
        Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Ocean, Heightmap.Biome.Mountain,
        Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth,
    };

    // True once locations and dungeons have been read (or loaded from the file), so the tiers are complete
    public static bool Complete { get; private set; }

    // Counts up each time the tiers become more complete, so the window knows to redraw
    public static int Revision { get; private set; }

    public static string Name(int tier)
    {
        return tier >= 0 && tier < Order.Length ? Localization.instance.Localize("$biome_" + Order[tier].ToString().ToLowerInvariant()) : "";
    }

    public static int TierOf(Heightmap.Biome biome)
    {
        for (int i = 0; i < Order.Length; i++)
        {
            if ((biome & Order[i]) != 0)
            {
                return i;
            }
        }
        return -1;
    }

    // Earliest biome each material is found in: s_world from trees, rocks and spawns, s_deep from locations and dungeons
    private static readonly Dictionary<string, int> s_world = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> s_deep = new Dictionary<string, int>();
    private static Dictionary<string, int> s_target = s_world;
    private static readonly Dictionary<GameObject, int> s_scanned = new Dictionary<GameObject, int>();
    private static readonly Dictionary<string, List<string>> s_madeFrom = new Dictionary<string, List<string>>();
    private static readonly Dictionary<string, List<Recipe>> s_recipes = new Dictionary<string, List<Recipe>>();
    private static readonly Dictionary<string, int> s_memo = new Dictionary<string, int>();
    private static readonly HashSet<string> s_busy = new HashSet<string>();
    private static readonly Dictionary<Room.Theme, int> s_themeTier = new Dictionary<Room.Theme, int>();
    private static bool s_started;

    private static string CachePath => Path.Combine(Paths.ConfigPath, "VanityWardrobe", "biomes.cache");

    // ---- placing items ------------------------------------------------------------------------------------------------------

    // Sets Tier on every entry: the index in Order of its biome, or -1 when it cannot be told. Quick: it uses what has been read so far.
    public static void Assign(List<Entry> items)
    {
        foreach (Entry item in items)
        {
            item.Tier = -1;
        }
        if (ZoneSystem.instance == null || ObjectDB.instance == null)
        {
            return;
        }
        try
        {
            if (s_world.Count == 0)
            {
                ReadWorld();
            }
            s_madeFrom.Clear();
            s_recipes.Clear();
            s_memo.Clear();
            s_busy.Clear();
            GatherConversions();
            GatherRecipes();
            foreach (Entry item in items)
            {
                item.Tier = EquipmentTier(item.Prefab);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VanityWardrobe] Could not work out the biomes: " + e);
        }
    }

    // Called every frame by the plugin. Once a world is loaded, starts reading the locations in the background (once per game session).
    public static void Tick(MonoBehaviour host)
    {
        if (s_started || ZoneSystem.instance == null || ZNetScene.instance == null || ObjectDB.instance == null || Player.m_localPlayer == null)
        {
            return;
        }
        s_started = true;
        host.StartCoroutine(ReadLocations());
    }

    private static int Found(string item)
    {
        int tier = s_world.TryGetValue(item, out int a) ? a : -1;
        if (s_deep.TryGetValue(item, out int b))
        {
            tier = tier < 0 ? b : Mathf.Min(tier, b);
        }
        return tier;
    }

    // ---- where things are found --------------------------------------------------------------------------------------------

    private static void Note(GameObject item, int tier)
    {
        if (item == null || tier < 0)
        {
            return;
        }
        if (!s_target.TryGetValue(item.name, out int old) || tier < old)
        {
            s_target[item.name] = tier;
        }
    }

    private static void FromTable(DropTable table, int tier)
    {
        if (table == null || table.m_drops == null)
        {
            return;
        }
        foreach (DropTable.DropData drop in table.m_drops)
        {
            Note(drop.m_item, tier);
        }
    }

    // What a prefab placed in the world gives: pickables, what trees, rocks and breakables drop or turn into, loot, what creatures
    // drop, and the creatures that spawners and altars bring (which are scanned in turn)
    private static void Scan(GameObject root, int tier)
    {
        if (root == null || tier < 0 || (s_scanned.TryGetValue(root, out int seen) && seen <= tier))
        {
            return;
        }
        s_scanned[root] = tier;
        if (root.GetComponent<ItemDrop>() != null)
        {
            Note(root, tier);
        }
        foreach (Pickable pickable in root.GetComponentsInChildren<Pickable>(true))
        {
            Note(pickable.m_itemPrefab, tier);
        }
        foreach (Destructible destructible in root.GetComponentsInChildren<Destructible>(true))
        {
            Scan(destructible.m_spawnWhenDestroyed, tier);
        }
        foreach (DropOnDestroyed drops in root.GetComponentsInChildren<DropOnDestroyed>(true))
        {
            FromTable(drops.m_dropWhenDestroyed, tier);
        }
        foreach (TreeBase tree in root.GetComponentsInChildren<TreeBase>(true))
        {
            FromTable(tree.m_dropWhenDestroyed, tier);
            Scan(tree.m_logPrefab, tier);
        }
        foreach (TreeLog log in root.GetComponentsInChildren<TreeLog>(true))
        {
            FromTable(log.m_dropWhenDestroyed, tier);
            Scan(log.m_subLogPrefab, tier);
        }
        foreach (MineRock rock in root.GetComponentsInChildren<MineRock>(true))
        {
            FromTable(rock.m_dropItems, tier);
        }
        foreach (MineRock5 rock in root.GetComponentsInChildren<MineRock5>(true))
        {
            FromTable(rock.m_dropItems, tier);
        }
        foreach (Container container in root.GetComponentsInChildren<Container>(true))
        {
            FromTable(container.m_defaultItems, tier);
        }
        foreach (CharacterDrop creature in root.GetComponentsInChildren<CharacterDrop>(true))
        {
            foreach (CharacterDrop.Drop drop in creature.m_drops)
            {
                Note(drop.m_prefab, tier);
            }
        }
        foreach (CreatureSpawner spawner in root.GetComponentsInChildren<CreatureSpawner>(true))
        {
            Scan(spawner.m_creaturePrefab, tier);
        }
        foreach (SpawnArea area in root.GetComponentsInChildren<SpawnArea>(true))
        {
            foreach (SpawnArea.SpawnData spawn in area.m_prefabs)
            {
                Scan(spawn.m_prefab, tier);
            }
        }
        foreach (OfferingBowl bowl in root.GetComponentsInChildren<OfferingBowl>(true))
        {
            Scan(bowl.m_bossPrefab, tier);
        }
    }

    // Trees, rocks, plants and the creatures that spawn in each biome. Quick.
    private static void ReadWorld()
    {
        s_target = s_world;
        s_scanned.Clear();
        ZoneSystem zones = ZoneSystem.instance;
        foreach (ZoneSystem.ZoneVegetation vegetation in zones.m_vegetation)
        {
            if (vegetation.m_enable)
            {
                Scan(vegetation.m_prefab, TierOf(vegetation.m_biome));
            }
        }
        SpawnSystem spawns = zones.m_zoneCtrlPrefab != null ? zones.m_zoneCtrlPrefab.GetComponent<SpawnSystem>() : null;
        if (spawns != null)
        {
            foreach (SpawnSystemList list in spawns.m_spawnLists)
            {
                foreach (SpawnSystem.SpawnData spawn in list.m_spawners)
                {
                    if (spawn.m_enabled)
                    {
                        Scan(spawn.m_prefab, TierOf(spawn.m_biome));
                    }
                }
            }
        }
    }

    // ---- locations and dungeons (slow, so in the background and kept in a file) ---------------------------------------------

    // Changes whenever the game, the mods or the world's lists of places change, so the file is not trusted after that
    private static string CacheKey()
    {
        string mods = string.Join(",", BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Select(p => p.Metadata.GUID + p.Metadata.Version).OrderBy(x => x));
        return "1|" + Version.GetVersionString() + "|" + ObjectDB.instance.m_items.Count + "|" + ZoneSystem.instance.m_locations.Count + "|" + ZoneSystem.instance.m_vegetation.Count + "|" + mods.GetStableHashCode();
    }

    private static bool LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return false;
            }
            string[] lines = File.ReadAllLines(CachePath);
            if (lines.Length == 0 || lines[0] != CacheKey())
            {
                return false;
            }
            s_deep.Clear();
            foreach (string line in lines.Skip(1))
            {
                string[] parts = line.Split('\t');
                if (parts.Length == 2 && int.TryParse(parts[1], out int tier))
                {
                    s_deep[parts[0]] = tier;
                }
            }
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            File.WriteAllLines(CachePath, new[] { CacheKey() }.Concat(s_deep.Select(p => p.Key + "\t" + p.Value)));
        }
        catch (System.Exception)
        {
        }
    }

    private static void Finish(string how)
    {
        Complete = true;
        Catalog.Retier();
        Revision++;
        Debug.Log("[VanityWardrobe] Biomes: " + how + ", " + s_world.Count + " materials from the world, " + s_deep.Count + " more from locations.");
    }

    private static IEnumerator ReadLocations()
    {
        yield return null;
        if (LoadCache())
        {
            Finish("read from the file");
            yield break;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        s_target = s_deep;
        s_themeTier.Clear();
        int count = 0;
        foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations.ToList())
        {
            if (!location.m_enable || !location.m_prefab.IsValid)
            {
                continue;
            }
            int tier = TierOf(location.m_biome);
            yield return Load(location.m_prefab, prefab =>
            {
                Scan(prefab, tier);
                foreach (DungeonGenerator dungeon in prefab.GetComponentsInChildren<DungeonGenerator>(true))
                {
                    NoteThemes(dungeon.m_themes, tier);
                }
            });
            if (++count % 4 == 0)
            {
                yield return null;
            }
        }

        // Each dungeon room is loaded once, at the earliest biome of its themes
        if (DungeonDB.instance != null)
        {
            foreach (DungeonDB.RoomData room in DungeonDB.instance.m_rooms.ToList())
            {
                int tier = -1;
                foreach (KeyValuePair<Room.Theme, int> theme in s_themeTier)
                {
                    if ((room.m_theme & theme.Key) != 0)
                    {
                        tier = tier < 0 ? theme.Value : Mathf.Min(tier, theme.Value);
                    }
                }
                if (!room.m_enabled || tier < 0 || !room.m_prefab.IsValid)
                {
                    continue;
                }
                yield return Load(room.m_prefab, prefab => Scan(prefab, tier));
                if (++count % 4 == 0)
                {
                    yield return null;
                }
            }
        }
        s_target = s_world;
        SaveCache();
        Finish("read from the world in " + watch.ElapsedMilliseconds / 1000 + " s");
    }

    // Loads a prefab without stopping the game, reads it, and lets it go again if nobody else had it loaded
    private static IEnumerator Load(SoftReferenceableAssets.SoftReference<GameObject> reference, System.Action<GameObject> read)
    {
        bool loadedBefore = reference.IsLoaded;
        if (!loadedBefore)
        {
            reference.LoadAsync();
            float waited = 0f;
            while (!reference.IsLoaded && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        try
        {
            if (reference.Asset != null)
            {
                read(reference.Asset);
            }
        }
        catch (System.Exception)
        {
        }
        if (!loadedBefore)
        {
            reference.Release();
        }
    }

    private static void NoteThemes(Room.Theme themes, int tier)
    {
        foreach (Room.Theme theme in System.Enum.GetValues(typeof(Room.Theme)))
        {
            if (theme != 0 && (themes & theme) != 0 && (!s_themeTier.TryGetValue(theme, out int old) || tier < old))
            {
                s_themeTier[theme] = tier;
            }
        }
    }

    // ---- smelting and recipes ----------------------------------------------------------------------------------------------

    // What is smelted, spun, ground or fermented from what
    private static void GatherConversions()
    {
        if (ZNetScene.instance == null)
        {
            return;
        }
        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }
            Smelter smelter = prefab.GetComponent<Smelter>();
            if (smelter != null)
            {
                foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                {
                    NoteConversion(conversion.m_from, conversion.m_to);
                }
            }
            Fermenter fermenter = prefab.GetComponent<Fermenter>();
            if (fermenter != null)
            {
                foreach (Fermenter.ItemConversion conversion in fermenter.m_conversion)
                {
                    NoteConversion(conversion.m_from, conversion.m_to);
                }
            }
        }
    }

    private static void NoteConversion(ItemDrop from, ItemDrop to)
    {
        if (from == null || to == null)
        {
            return;
        }
        if (!s_madeFrom.TryGetValue(to.name, out List<string> sources))
        {
            s_madeFrom[to.name] = sources = new List<string>();
        }
        if (!sources.Contains(from.name))
        {
            sources.Add(from.name);
        }
    }

    private static void GatherRecipes()
    {
        foreach (Recipe recipe in ObjectDB.instance.m_recipes)
        {
            if (recipe == null || !recipe.m_enabled || recipe.m_item == null)
            {
                continue;
            }
            if (!s_recipes.TryGetValue(recipe.m_item.name, out List<Recipe> list))
            {
                s_recipes[recipe.m_item.name] = list = new List<Recipe>();
            }
            list.Add(recipe);
        }
    }

    // ---- working out an item's biome ---------------------------------------------------------------------------------------

    // Gear is placed by its recipe. If it has none, by where it is found. Variants with no recipe of their own and a prefix
    // ("FW_ArmorBronzeChest", "SP_ArmorBronzeChest") take the biome of the plain item.
    private static int EquipmentTier(string item)
    {
        int tier = RecipeTier(item);
        if (tier < 0)
        {
            tier = Found(item);
        }
        int underscore = item.IndexOf('_');
        if (tier < 0 && underscore > 0 && ObjectDB.instance.GetItemPrefab(item.Substring(underscore + 1)) != null)
        {
            return EquipmentTier(item.Substring(underscore + 1));
        }
        return tier;
    }

    // A material is as early as its earliest route: found in the world, smelted or ground from something, or crafted
    private static int MaterialTier(string item)
    {
        if (s_memo.TryGetValue(item, out int known))
        {
            return known;
        }
        if (!s_busy.Add(item))
        {
            return -1;
        }
        int tier = Found(item);
        if (s_madeFrom.TryGetValue(item, out List<string> sources))
        {
            foreach (int source in sources.Select(MaterialTier).Where(t => t >= 0))
            {
                tier = tier < 0 ? source : Mathf.Min(tier, source);
            }
        }
        int crafted = RecipeTier(item);
        if (crafted >= 0)
        {
            tier = tier < 0 ? crafted : Mathf.Min(tier, crafted);
        }
        s_busy.Remove(item);
        s_memo[item] = tier;
        return tier;
    }

    // The furthest ingredient of the easiest recipe, or the nearest if one ingredient is enough
    private static int RecipeTier(string item)
    {
        if (!s_recipes.TryGetValue(item, out List<Recipe> recipes))
        {
            return -1;
        }
        int best = -1;
        foreach (Recipe recipe in recipes)
        {
            List<int> tiers = recipe.m_resources
                .Where(r => r.m_resItem != null && r.m_resItem.name != item && !r.m_upgraderResource)   // (an upgrade catalyst is not needed to craft it)
                .Select(r => MaterialTier(r.m_resItem.name))
                .Where(t => t >= 0)
                .ToList();
            if (tiers.Count == 0)
            {
                continue;
            }
            int tier = recipe.m_requireOnlyOneIngredient ? tiers.Min() : tiers.Max();
            best = best < 0 ? tier : Mathf.Min(best, tier);
        }
        return best;
    }
}
