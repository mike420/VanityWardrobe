using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VanityWardrobe;

// One pinned recipe: an item, or a whole set, at a quality. Its materials are shown in the panel under the minimap.
internal sealed class Pin
{
    public string Title;
    public int Quality = 1;
    public bool Collapsed;
    public List<string> Prefabs = new List<string>();
    public HashSet<string> Done = new HashSet<string>();   // the pieces of a set that have been crafted already

    public List<string> Remaining => Prefabs.Where(p => !Done.Contains(p)).ToList();

    public string Key => Pins.KeyOf(Prefabs);
}

// The pinned recipes of the character being played. They are kept in the character's own data (like Einherjar Bodies keeps its eyes), so
// each character has its own and they are saved with it. A pin whose items are not in the game (a mod taken out) is kept but not shown.
internal static class Pins
{
    private const string DataKey = "vanitywardrobe_pins";

    public static readonly List<Pin> All = new List<Pin>();

    // Counts up whenever the pins change, so what shows them knows to draw again
    public static int Revision { get; private set; }

    private static Player s_loadedFor;

    public static string KeyOf(IEnumerable<string> prefabs) => string.Join("+", prefabs);

    // Reads the pins from the character the first time it is seen
    public static void EnsureLoaded(Player player)
    {
        if (s_loadedFor == player)
        {
            return;
        }
        s_loadedFor = player;
        All.Clear();
        if (player.m_customData.TryGetValue(DataKey, out string text) && !string.IsNullOrEmpty(text))
        {
            foreach (string part in text.Split('|'))
            {
                string[] fields = part.Split('~');
                if ((fields.Length == 4 || fields.Length == 5) && int.TryParse(fields[1], out int quality))
                {
                    All.Add(new Pin
                    {
                        Title = fields[0],
                        Quality = Mathf.Max(1, quality),
                        Collapsed = fields[2] == "1",
                        Prefabs = fields[3].Split('+').Where(p => p.Length > 0).ToList(),
                        Done = new HashSet<string>(fields.Length == 5 ? fields[4].Split('+').Where(p => p.Length > 0) : new string[0]),
                    });
                }
            }
        }
        Revision++;
    }

    private static string Clean(string text) => text.Replace("~", " ").Replace("|", " ").Replace("+", " ");

    private static void Save()
    {
        Player player = Player.m_localPlayer;
        if (player != null)
        {
            if (All.Count == 0)
            {
                player.m_customData.Remove(DataKey);
            }
            else
            {
                player.m_customData[DataKey] = string.Join("|", All.Select(p => Clean(p.Title) + "~" + p.Quality + "~" + (p.Collapsed ? "1" : "0") + "~" + p.Key + "~" + string.Join("+", p.Done)));
            }
        }
        Revision++;
    }

    public static bool IsPinned(IEnumerable<string> prefabs)
    {
        string key = KeyOf(prefabs);
        return All.Any(p => p.Key == key);
    }

    // Pins what is not pinned, and unpins what is
    public static void Toggle(string title, IEnumerable<string> prefabs, int quality)
    {
        if (Player.m_localPlayer == null)
        {
            return;
        }
        EnsureLoaded(Player.m_localPlayer);
        List<string> list = prefabs.ToList();
        string key = KeyOf(list);
        Pin existing = All.FirstOrDefault(p => p.Key == key);
        if (existing != null)
        {
            All.Remove(existing);
        }
        else
        {
            All.Add(new Pin { Title = title, Quality = Mathf.Max(1, quality), Prefabs = list });
        }
        Save();
    }

    public static void Remove(Pin pin)
    {
        if (All.Remove(pin))
        {
            Save();
        }
    }

    // The player has crafted (or upgraded to) this item: it is ticked off every pin that wants it at that quality or lower, and a pin with
    // nothing left to craft is removed
    public static void Crafted(string prefab, int quality)
    {
        if (Player.m_localPlayer == null)
        {
            return;
        }
        EnsureLoaded(Player.m_localPlayer);
        bool changed = false;
        foreach (Pin pin in All.ToList())
        {
            if (pin.Quality <= quality && pin.Prefabs.Contains(prefab) && pin.Done.Add(prefab))
            {
                changed = true;
                if (pin.Remaining.Count == 0)
                {
                    All.Remove(pin);
                }
            }
        }
        if (changed)
        {
            Save();
        }
    }

    public static void SetCollapsed(Pin pin, bool collapsed)
    {
        pin.Collapsed = collapsed;
        Save();
    }

    public static void Clear()
    {
        All.Clear();
        Save();
    }
}
