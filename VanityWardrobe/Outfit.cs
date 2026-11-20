using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace VanityWardrobe;

// What the fitting-room model wears: at most one item per slot
internal sealed class Outfit
{
    public static readonly Slot[] Slots = (Slot[])Enum.GetValues(typeof(Slot));

    public readonly Entry[] Worn = new Entry[Slots.Length];

    public Entry this[Slot slot]
    {
        get => Worn[(int)slot];
        set => Worn[(int)slot] = value;
    }

    // Wears an item. A two-handed weapon or a bow takes the shield's hand, and a shield takes the place of a two-handed weapon.
    public void Wear(Entry entry)
    {
        this[entry.Slot] = entry;
        if (entry.Slot == Slot.Weapon && IsTwoHanded(entry))
        {
            this[Slot.Shield] = null;
        }
        else if (entry.Slot == Slot.Shield && this[Slot.Weapon] != null && IsTwoHanded(this[Slot.Weapon]))
        {
            this[Slot.Weapon] = null;
        }
    }

    public void Clear()
    {
        Array.Clear(Worn, 0, Worn.Length);
    }

    public Outfit Copy()
    {
        Outfit copy = new Outfit();
        Array.Copy(Worn, copy.Worn, Worn.Length);
        return copy;
    }

    public IEnumerable<Entry> Pieces => Worn.Where(e => e != null);

    public static bool IsTwoHanded(Entry entry)
    {
        ItemDrop.ItemData.ItemType type = entry.Shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || type == ItemDrop.ItemData.ItemType.Bow;
    }

    public static bool IsLeftHand(Entry entry)
    {
        ItemDrop.ItemData.ItemType type = entry.Shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.Bow || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
    }
}

// Outfits the player has named, kept in a plain text file next to the mod's translations, one outfit a line
internal static class SavedOutfits
{
    private static string FilePath => Path.Combine(Paths.ConfigPath, VanityWardrobeMod.PluginName, "outfits.txt");

    public static List<string> Names()
    {
        return Read().Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Save(string name, Outfit outfit)
    {
        name = Clean(name);
        if (name.Length == 0)
        {
            return;
        }
        Dictionary<string, string[]> all = Read();
        all[name] = Outfit.Slots.Select(s => outfit[s]?.Prefab ?? "").ToArray();
        Write(all);
    }

    public static void Delete(string name)
    {
        Dictionary<string, string[]> all = Read();
        if (all.Remove(name))
        {
            Write(all);
        }
    }

    // Puts a saved outfit back on. Items a mod no longer provides are left out.
    public static bool Load(string name, Outfit outfit)
    {
        if (!Read().TryGetValue(name, out string[] prefabs))
        {
            return false;
        }
        outfit.Clear();
        for (int i = 0; i < Outfit.Slots.Length && i < prefabs.Length; i++)
        {
            Entry entry = prefabs[i].Length > 0 ? Catalog.Find(prefabs[i]) : null;
            outfit.Worn[i] = entry;
        }
        return true;
    }

    private static string Clean(string name)
    {
        return (name ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static Dictionary<string, string[]> Read()
    {
        Dictionary<string, string[]> all = new Dictionary<string, string[]>();
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2 && parts[0].Length > 0)
                    {
                        all[parts[0]] = parts.Skip(1).ToArray();
                    }
                }
            }
        }
        catch (Exception e)
        {
            Jotunn.Logger.LogWarning("[VanityWardrobe] Could not read the saved outfits: " + e.Message);
        }
        return all;
    }

    private static void Write(Dictionary<string, string[]> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, all.Select(p => p.Key + "|" + string.Join("|", p.Value)));
        }
        catch (Exception e)
        {
            Jotunn.Logger.LogWarning("[VanityWardrobe] Could not save the outfits: " + e.Message);
        }
    }
}
