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
                if (fields.Length == 4 && int.TryParse(fields[1], out int quality))
                {
                    All.Add(new Pin
                    {
                        Title = fields[0],
                        Quality = Mathf.Max(1, quality),
                        Collapsed = fields[2] == "1",
                        Prefabs = fields[3].Split('+').Where(p => p.Length > 0).ToList(),
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
                player.m_customData[DataKey] = string.Join("|", All.Select(p => Clean(p.Title) + "~" + p.Quality + "~" + (p.Collapsed ? "1" : "0") + "~" + p.Key));
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
