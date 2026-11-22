using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VanityWardrobe;

internal enum Slot
{
    Helmet,
    Chest,
    Legs,
    Cape,
    Weapon,
    Shield,
}

internal enum Category
{
    Sets,
    Helmets,
    Chests,
    Legs,
    Capes,
    Weapons,
    Shields,
}

// What an item is good for, worked out from its stats. An item can have more than one.
[System.Flags]
internal enum Role
{
    None = 0,
    Mage = 1,
    Tank = 2,
    Damage = 4,
    Archer = 8,
}

// One wearable or wieldable item of the game's item database
internal sealed class Entry
{
    public string Prefab;
    public GameObject PrefabObject;
    public ItemDrop.ItemData.SharedData Shared;
    public Slot Slot;
    public string Name;
    public Sprite Icon;
    public string SearchText;
    public string TypeName;
    public Role Roles;
    public string Source;
    public int Tier = -1;

    public int Hash => Prefab.GetStableHashCode();
}

// Armor pieces that share a set name, and so a set bonus
internal sealed class SetEntry
{
    public string Name;
    public List<Entry> Pieces = new List<Entry>();
    public string SearchText;
    public Role Roles;
    public string Source;
    public int Tier = -1;
    public Sprite Icon => (Pieces.FirstOrDefault(p => p.Slot == Slot.Chest) ?? Pieces[0]).Icon;
}

// The wardrobe's list of everything that can be tried on, built from the item database after every mod has added its items
internal static class Catalog
{
    public static List<Entry> Items { get; private set; } = new List<Entry>();
    public static List<SetEntry> Sets { get; private set; } = new List<SetEntry>();
    public static List<Skills.SkillType> WeaponSkills { get; private set; } = new List<Skills.SkillType>();
    public static List<string> Sources { get; private set; } = new List<string>();

    private static int s_builtCount = -1;
    private static string s_builtLanguage;

    public static void EnsureBuilt()
    {
        if (ObjectDB.instance == null || Localization.instance == null)
        {
            return;
        }
        string language = Localization.instance.GetSelectedLanguage();
        if (s_builtCount == ObjectDB.instance.m_items.Count && s_builtLanguage == language)
        {
            return;
        }
        Build();
        s_builtCount = ObjectDB.instance.m_items.Count;
        s_builtLanguage = language;
    }

    public static Entry Find(string prefab)
    {
        return Items.FirstOrDefault(i => i.Prefab == prefab);
    }

    public static Category CategoryOf(Entry entry)
    {
        switch (entry.Slot)
        {
            case Slot.Helmet: return Category.Helmets;
            case Slot.Chest: return Category.Chests;
            case Slot.Legs: return Category.Legs;
            case Slot.Cape: return Category.Capes;
            case Slot.Weapon: return Category.Weapons;
            default: return Category.Shields;
        }
    }

    // The game has FW_ and SP_ copies of some plain items (FW_HelmetBronze, SP_HelmetBronze next to HelmetBronze) with the same name, icon and stats,
    // so the list would show the item three times. The plain one is kept.
    private static bool IsEventCopy(string prefab, string name)
    {
        foreach (string prefix in new[] { "FW_", "SP_" })
        {
            if (prefab.StartsWith(prefix) && ObjectDB.instance.GetItemPrefab(prefab.Substring(prefix.Length)) is GameObject plain)
            {
                ItemDrop drop = plain.GetComponent<ItemDrop>();
                if (drop != null && Localization.instance.Localize(drop.m_itemData.m_shared.m_name) == name)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void Build()
    {
        Dictionary<string, string> bundleOf = BundleOfPrefabs();
        List<Entry> items = new List<Entry>();
        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                continue;
            }
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            if (!TryGetSlot(shared.m_itemType, out Slot slot) || shared.m_icons == null || shared.m_icons.Length == 0 || string.IsNullOrEmpty(shared.m_name))
            {
                continue;
            }
            if ((slot == Slot.Weapon || slot == Slot.Shield) && !HasAttachPoint(prefab))
            {
                continue;
            }
            string name = Localization.instance.Localize(shared.m_name);
            if (IsEventCopy(prefab.name, name))
            {
                continue;
            }
            items.Add(new Entry
            {
                Prefab = prefab.name,
                PrefabObject = prefab,
                Shared = shared,
                Slot = slot,
                Name = name,
                Icon = shared.m_icons[0],
                SearchText = (name + " " + prefab.name).ToLowerInvariant(),
                TypeName = slot == Slot.Weapon && shared.m_skillType != Skills.SkillType.None ? Localization.instance.Localize("$skill_" + shared.m_skillType.ToString().ToLowerInvariant()) : "",
                Roles = RolesOf(slot, shared),
                Source = SourceOf(prefab.name, bundleOf),
            });
        }
        Items = items.OrderBy(i => i.Name, System.StringComparer.CurrentCultureIgnoreCase).ToList();
        Biomes.Assign(Items);

        // Armor with a set name is grouped by it. Armor without one (the plain vanilla sets, and many from mods) is grouped by the
        // words left in its prefab name once the piece words are taken out: ArmorBronzeChest, ArmorBronzeLegs and HelmetBronze
        // all leave "bronze".
        List<Entry> armor = Items.Where(i => i.Slot == Slot.Helmet || i.Slot == Slot.Chest || i.Slot == Slot.Legs).ToList();
        List<SetEntry> sets = armor
            .Where(i => !string.IsNullOrEmpty(i.Shared.m_setName))
            .GroupBy(i => i.Shared.m_setName)
            .Select(g => MakeSet(SetDisplayName(g.Key), g))
            .ToList();
        sets.AddRange(armor
            .Where(i => string.IsNullOrEmpty(i.Shared.m_setName))
            .GroupBy(i => GuessSetKey(i.Prefab))
            .Where(g => g.Key.Length > 0 && g.Select(p => p.Slot).Distinct().Count() >= 2)
            .Select(g => MakeSet(TitleCase(g.Key), g)));
        Sets = sets.OrderBy(s => s.Name, System.StringComparer.CurrentCultureIgnoreCase).ToList();

        Sources = Items.Select(i => i.Source).Distinct().OrderBy(n => n == OtherSource ? 2 : n == GameSource ? 0 : 1).ThenBy(n => n, System.StringComparer.CurrentCultureIgnoreCase).ToList();
        WeaponSkills = Items.Where(i => i.Slot == Slot.Weapon && i.Shared.m_skillType != Skills.SkillType.None).Select(i => i.Shared.m_skillType).Distinct().OrderBy(s => s.ToString()).ToList();
    }

    // ---- roles ---------------------------------------------------------------------------------------------------------

    private static Role SkillRole(Skills.SkillType skill)
    {
        switch (skill)
        {
            case Skills.SkillType.ElementalMagic:
            case Skills.SkillType.BloodMagic:
                return Role.Mage;
            case Skills.SkillType.Bows:
            case Skills.SkillType.Crossbows:
            case Skills.SkillType.Sneak:
                return Role.Archer;
            case Skills.SkillType.Swords:
            case Skills.SkillType.Axes:
            case Skills.SkillType.Clubs:
            case Skills.SkillType.Knives:
            case Skills.SkillType.Spears:
            case Skills.SkillType.Polearms:
            case Skills.SkillType.Unarmed:
                return Role.Damage;
            default:
                return Role.None;
        }
    }

    // Shields are for tanking. A weapon takes its role from its skill: bows and crossbows are archer, the staves mage, the melee
    // skills damage. Armor is mage with eitr regeneration, tank when it is heavy (slows you, or has a lot of armor), and gets the
    // roles of the skills its status effects raise, plus damage for effects that cut attack stamina or raise stamina regeneration.
    private static Role RolesOf(Slot slot, ItemDrop.ItemData.SharedData shared)
    {
        if (slot == Slot.Shield)
        {
            return Role.Tank;
        }
        if (slot == Slot.Weapon)
        {
            return SkillRole(shared.m_skillType);
        }
        Role roles = Role.None;
        if (shared.m_eitrRegenModifier > 0f)
        {
            roles |= Role.Mage;
        }
        else if (shared.m_movementModifier <= -0.04f || shared.m_armor >= 36f)
        {
            roles |= Role.Tank;
        }
        foreach (StatusEffect effect in new[] { shared.m_equipStatusEffect, shared.m_setStatusEffect })
        {
            if (effect is SE_Stats stats)
            {
                roles |= SkillRole(stats.m_skillLevel) | SkillRole(stats.m_skillLevel2) | SkillRole(stats.m_modifyAttackSkill);
                if (stats.m_attackStaminaUseModifier < 0f || stats.m_staminaRegenMultiplier > 1f)
                {
                    roles |= Role.Damage;
                }
            }
        }
        return roles;
    }

    // A set has a role when at least half of its pieces do
    private static Role SetRoles(List<Entry> pieces)
    {
        Role result = Role.None;
        int needed = (pieces.Count + 1) / 2;
        foreach (Role role in new[] { Role.Mage, Role.Tank, Role.Damage, Role.Archer })
        {
            if (pieces.Count(p => (p.Roles & role) != 0) >= needed)
            {
                result |= role;
            }
        }
        return result;
    }

    // ---- source --------------------------------------------------------------------------------------------------------

    public const string GameSource = "Valheim";
    public static string OtherSource => Translations.T("vanitywardrobe_source_other");

    // The game's own asset bundles have hash names (like "c4210710"); a mod's bundle is named by the mod. This maps each prefab
    // name to the bundle that holds it, preferring a mod's bundle.
    private static Dictionary<string, string> BundleOfPrefabs()
    {
        Dictionary<string, string> map = new Dictionary<string, string>();
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            bool game = IsGameBundle(bundle.name);
            string[] names;
            try
            {
                names = bundle.GetAllAssetNames();
            }
            catch (System.Exception)
            {
                continue;
            }
            foreach (string path in names)
            {
                if (!path.EndsWith(".prefab"))
                {
                    continue;
                }
                string stem = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (!game || !map.ContainsKey(stem))
                {
                    map[stem] = bundle.name;
                }
            }
        }
        return map;
    }

    private static bool IsGameBundle(string name)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-f]{6,8}$");
    }

    private static string SourceOf(string prefab, Dictionary<string, string> bundleOf)
    {
        if (bundleOf.TryGetValue(prefab.ToLowerInvariant(), out string bundle))
        {
            return IsGameBundle(bundle) ? GameSource : ModNameOfBundle(bundle);
        }
        try
        {
            BepInEx.BepInPlugin plugin = Jotunn.Managers.ItemManager.Instance.GetItem(prefab)?.SourceMod;
            if (plugin != null)
            {
                return plugin.Name;
            }
        }
        catch (System.Exception)
        {
        }
        return OtherSource;
    }

    private static string Squash(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]", "");
    }

    // The plugin a bundle belongs to, found by its name: "corewood_fortified" is the plugin "CorewoodFortified". If none matches, the
    // bundle's own name, tidied.
    private static string ModNameOfBundle(string bundle)
    {
        string key = Squash(bundle).Replace("assetbundle", "").Replace("bundle", "");
        if (key.Length >= 4)
        {
            foreach (BepInEx.PluginInfo plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                string name = Squash(plugin.Metadata.Name);
                string guid = Squash(plugin.Metadata.GUID);
                if ((name.Length >= 4 && (name.Contains(key) || key.Contains(name))) || guid.Contains(key))
                {
                    return plugin.Metadata.Name;
                }
            }
        }
        return Pretty(bundle);
    }

    private static int TierOfPieces(List<Entry> pieces)
    {
        return pieces.Select(p => p.Tier).Where(t => t >= 0).DefaultIfEmpty(-1).Max();
    }

    // Works the biomes out again from what has been read since the list was built (the locations are read in the background)
    public static void Retier()
    {
        Biomes.Assign(Items);
        foreach (SetEntry set in Sets)
        {
            set.Tier = TierOfPieces(set.Pieces);
        }
    }

    private static string SetDisplayName(string setName)
    {
        return Pretty(setName.StartsWith("$") ? Localization.instance.Localize(setName) : setName);
    }

    private static SetEntry MakeSet(string name, IEnumerable<Entry> pieces)
    {
        List<Entry> list = pieces.OrderBy(p => p.Slot).ToList();
        return new SetEntry
        {
            Name = name,
            Pieces = list,
            SearchText = (name + " " + string.Join(" ", list.Select(p => p.SearchText))).ToLowerInvariant(),
            Roles = SetRoles(list),
            Tier = TierOfPieces(list),
            Source = list.GroupBy(p => p.Source).OrderByDescending(g => g.Count()).First().Key,
        };
    }

    private static readonly HashSet<string> PieceWords = new HashSet<string>
    {
        "armor", "armour", "chest", "legs", "helmet", "helm", "cape", "tunic", "trousers", "pants", "cuirass", "hood", "leggings", "boots", "body", "torso", "greaves", "pauldrons",
    };

    // Piece words that are sometimes glued onto the end of another word in a prefab name: "ArmorDeepNorthMagelegs"
    private static readonly string[] GluedPieceWords = { "legs", "chest", "helmet", "hood", "greaves", "leggings", "cuirass", "trousers", "armor", "armour", "tunic" };

    // Abbreviations in prefab names that stand for the words of the set's name: "HelmetDNMage" belongs with "ArmorDeepNorthMageChest"
    private static readonly Dictionary<string, string[]> Abbreviations = new Dictionary<string, string[]>
    {
        { "dn", new[] { "deep", "north" } },
    };

    // A prefab name as lower case words: "HelmetDNMage" is helmet, deep, north, mage and "ArmorDeepNorthMagelegs" is armor, deep, north, mage, legs
    private static List<string> Words(string name)
    {
        string spaced = System.Text.RegularExpressions.Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
        spaced = System.Text.RegularExpressions.Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        List<string> words = new List<string>();
        foreach (string raw in spaced.Split(new[] { ' ', '_', '.', '-' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw.ToLowerInvariant();
            string glued = GluedPieceWords.FirstOrDefault(p => word.Length >= p.Length + 3 && word.EndsWith(p));
            if (glued != null)
            {
                word = word.Substring(0, word.Length - glued.Length);
            }
            words.AddRange(Abbreviations.TryGetValue(word, out string[] full) ? full : new[] { word });
            if (glued != null)
            {
                words.Add(glued);
            }
        }
        return words;
    }

    private static string GuessSetKey(string prefab)
    {
        return string.Join(" ", Words(prefab).Where(w => !PieceWords.Contains(w)));
    }

    // "deep north mage" as "Deep North Mage", and short words like "fw" as "FW"
    private static string TitleCase(string words)
    {
        return string.Join(" ", words.Split(' ').Where(w => w.Length > 0).Select(w => w.Length <= 2 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    // "AshlandsMediumArmor" and "berserker_armor" as "Ashlands Medium Armor" and "Berserker Armor"
    private static string Pretty(string text)
    {
        if (text.Contains(" "))
        {
            return text;
        }
        return string.Join(" ", Words(text).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    // A weapon or shield is drawn by the game from a child of its prefab named "attach...". Some have none, such as the Dvergr's own crossbows
    // (DvergerArbalest_shoot) and the fists, which are for monsters: there is nothing to show, so they are left out. (Armor is not checked:
    // the leather legs have no such child and are drawn from the skin.)
    private static bool HasAttachPoint(GameObject prefab)
    {
        Transform root = prefab.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name.StartsWith("attach", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetSlot(ItemDrop.ItemData.ItemType type, out Slot slot)
    {
        switch (type)
        {
            case ItemDrop.ItemData.ItemType.Helmet: slot = Slot.Helmet; return true;
            case ItemDrop.ItemData.ItemType.Chest: slot = Slot.Chest; return true;
            case ItemDrop.ItemData.ItemType.Legs: slot = Slot.Legs; return true;
            case ItemDrop.ItemData.ItemType.Shoulder: slot = Slot.Cape; return true;
            case ItemDrop.ItemData.ItemType.OneHandedWeapon:
            case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
            case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
            case ItemDrop.ItemData.ItemType.Bow:
                slot = Slot.Weapon;
                return true;
            case ItemDrop.ItemData.ItemType.Shield: slot = Slot.Shield; return true;
            default:
                slot = Slot.Weapon;
                return false;
        }
    }
}
