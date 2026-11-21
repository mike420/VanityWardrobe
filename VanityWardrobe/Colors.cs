using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace VanityWardrobe;

// The colour chosen for each item. The choice belongs to the player: it is kept in a file, shown on the player's own character and on
// the fitting-room model, and the colours of the items being worn are put in the player's ZDO so that other players who have the mod
// see them too. Without the mod, others see the original colours.
internal static class Colors
{
    internal enum Kind
    {
        Hue,
        Black,
        White,
        Grey,
    }

    internal sealed class Preset
    {
        public string Key;
        public Color Swatch;
        public float Hue;
        public Kind Kind;
    }

    // Index 0 is the original colour; the presets are 1 to 11
    public static readonly Preset[] Presets =
    {
        new Preset { Key = "red", Swatch = new Color(0.82f, 0.14f, 0.14f), Hue = 0.00f },
        new Preset { Key = "orange", Swatch = new Color(0.93f, 0.5f, 0.1f), Hue = 0.07f },
        new Preset { Key = "yellow", Swatch = new Color(0.93f, 0.84f, 0.2f), Hue = 0.15f },
        new Preset { Key = "green", Swatch = new Color(0.25f, 0.62f, 0.25f), Hue = 0.33f },
        new Preset { Key = "teal", Swatch = new Color(0.15f, 0.62f, 0.62f), Hue = 0.50f },
        new Preset { Key = "blue", Swatch = new Color(0.2f, 0.4f, 0.85f), Hue = 0.60f },
        new Preset { Key = "purple", Swatch = new Color(0.55f, 0.3f, 0.75f), Hue = 0.77f },
        new Preset { Key = "pink", Swatch = new Color(0.93f, 0.5f, 0.7f), Hue = 0.91f },
        new Preset { Key = "black", Swatch = new Color(0.08f, 0.08f, 0.08f), Kind = Kind.Black },
        new Preset { Key = "white", Swatch = new Color(0.94f, 0.94f, 0.94f), Kind = Kind.White },
        new Preset { Key = "grey", Swatch = new Color(0.5f, 0.5f, 0.5f), Kind = Kind.Grey },
    };

    public static string Name(int preset)
    {
        return Translations.T(preset <= 0 ? "vanitywardrobe_color_original" : "vanitywardrobe_color_" + Presets[preset - 1].Key);
    }

    // Counts up whenever a choice changes, so the models know to be coloured again
    public static int Revision { get; private set; }

    private static readonly Dictionary<string, int> s_map = new Dictionary<string, int>();
    private static bool s_loaded;
    private static string s_published = "";
    private static float s_nextPublish;
    private static int s_publishedRevision = -1;
    private static readonly int s_zdoKey = "VanityWardrobe.colors".GetStableHashCode();
    private static readonly Dictionary<string, Dictionary<string, int>> s_parsed = new Dictionary<string, Dictionary<string, int>>();

    private static string FilePath => Path.Combine(Paths.ConfigPath, "VanityWardrobe", "colors.txt");

    private static void EnsureLoaded()
    {
        if (s_loaded)
        {
            return;
        }
        s_loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int preset) && preset > 0 && preset <= Presets.Length)
                    {
                        s_map[parts[0]] = preset;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VanityWardrobe] Could not read the colours: " + e.Message);
        }
    }

    // The preset chosen for an item, 0 for its own colour
    public static int Get(string prefab)
    {
        EnsureLoaded();
        return s_map.TryGetValue(prefab, out int preset) ? preset : 0;
    }

    public static void Set(string prefab, int preset)
    {
        EnsureLoaded();
        if (Get(prefab) == preset)
        {
            return;
        }
        if (preset <= 0)
        {
            s_map.Remove(prefab);
        }
        else
        {
            s_map[prefab] = preset;
        }
        Revision++;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, s_map.OrderBy(p => p.Key).Select(p => p.Key + "|" + p.Value));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VanityWardrobe] Could not save the colours: " + e.Message);
        }
    }

    // ---- what a model shows --------------------------------------------------------------------------------------------------

    // The colours to show on a model, as item name to preset, and a stamp that changes when they do. The fitting-room model and the
    // local player use the player's own choices; other players' colours come from their ZDO.
    public static Dictionary<string, int> For(VisEquipment vis, out string stamp)
    {
        EnsureLoaded();
        stamp = null;
        if (vis == null)
        {
            return null;
        }
        Player local = Player.m_localPlayer;
        if (vis == FittingRoom.PreviewVis || (local != null && vis == local.m_visEquipment))
        {
            stamp = "own" + Revision;
            return s_map;
        }
        if (!vis.m_isPlayer)
        {
            return null;
        }
        ZNetView view = vis.m_nViewOverride != null ? vis.m_nViewOverride : vis.GetComponent<ZNetView>();
        ZDO zdo = view != null ? view.GetZDO() : null;
        string text = zdo != null ? zdo.GetString(s_zdoKey, "") : "";
        stamp = "zdo" + text;
        return Parse(text);
    }

    private static Dictionary<string, int> Parse(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        if (s_parsed.TryGetValue(text, out Dictionary<string, int> known))
        {
            return known;
        }
        Dictionary<string, int> map = new Dictionary<string, int>();
        foreach (string pair in text.Split(';'))
        {
            string[] parts = pair.Split('=');
            if (parts.Length == 2 && int.TryParse(parts[1], out int preset) && preset > 0 && preset <= Presets.Length)
            {
                map[parts[0]] = preset;
            }
        }
        if (s_parsed.Count > 200)
        {
            s_parsed.Clear();
        }
        s_parsed[text] = map;
        return map;
    }

    // ---- telling other players ---------------------------------------------------------------------------------------------

    // Called every frame. Puts the colours of what the local player is wearing in the player's ZDO, a few times a second at most.
    public static void Publish()
    {
        Player player = Player.m_localPlayer;
        if (player == null || Time.unscaledTime < s_nextPublish)
        {
            return;
        }
        s_nextPublish = Time.unscaledTime + 0.5f;
        ZDO zdo = player.m_nview != null && player.m_nview.IsValid() && player.m_nview.IsOwner() ? player.m_nview.GetZDO() : null;
        if (zdo == null)
        {
            return;
        }
        EnsureLoaded();
        string text = string.Join(";", player.GetInventory().GetEquippedItems()
            .Select(i => i.m_dropPrefab != null ? i.m_dropPrefab.name : null)
            .Where(n => n != null && s_map.ContainsKey(n))
            .Distinct()
            .OrderBy(n => n)
            .Select(n => n + "=" + s_map[n]));
        if (text != s_published || s_publishedRevision != Revision)
        {
            if (text != s_published)
            {
                Diag.Log("Publishing your worn colours to other players: '" + text + "'");
            }
            s_published = text;
            s_publishedRevision = Revision;
            if (zdo.GetString(s_zdoKey, "") != text)
            {
                zdo.Set(s_zdoKey, text);
            }
        }
    }
}
