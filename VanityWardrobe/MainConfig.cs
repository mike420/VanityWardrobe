using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Jotunn.Extensions;

namespace VanityWardrobe;

internal static class MainConfig
{
    public static ConfigEntry<bool> modEnabled;
    public static ConfigEntry<KeyboardShortcut> openKey;
    public static ConfigEntry<float> chestRadius;
    public static ConfigEntry<bool> startWithMyGear;

    public static void Init(ConfigFile config)
    {
        modEnabled = config.BindConfigInOrder("General Settings", "Enable Plugin", defaultValue: true, "Enable the VanityWardrobe plugin.\n(Restart required. It is read before the game starts, so it is a local setting).\n", synced: false, sectionOrder: true, settingOrder: true);

        openKey = config.BindConfigInOrder("Wardrobe", "Open Key", defaultValue: new KeyboardShortcut(UnityEngine.KeyCode.F9), "The key that opens and closes the wardrobe. It does nothing while you are typing in chat or the console.", synced: false, sectionOrder: true, settingOrder: true);
        chestRadius = config.BindConfigInOrder("Wardrobe", "Chest Radius", defaultValue: 30f, "Chests within this many metres of you count towards the have / need totals for crafting costs. 0 counts only your own inventory.", synced: false, sectionOrder: true, settingOrder: true, acceptableValues: new AcceptableValueRange<float>(0f, 100f));
        startWithMyGear = config.BindConfigInOrder("Wardrobe", "Start With My Gear", defaultValue: true, "If On, the fitting-room model wears what you are wearing each time the wardrobe opens. If Off, it keeps the last outfit you tried.", synced: false, sectionOrder: true, settingOrder: true);

        CarryOverOldSections(config);
    }

    private static readonly Dictionary<string, string[]> OldSections = new Dictionary<string, string[]>
    {
    };

    private static void CarryOverOldSections(ConfigFile config)
    {
        Dictionary<ConfigDefinition, string> orphans;
        try
        {
            PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            orphans = property?.GetValue(config) as Dictionary<ConfigDefinition, string>;
        }
        catch (System.Exception e)
        {
            Jotunn.Logger.LogWarning("[VanityWardrobe] Could not look for settings under old section names: " + e.Message);
            return;
        }
        if (orphans == null || orphans.Count == 0)
        {
            return;
        }

        int carried = 0, dropped = 0;
        HashSet<ConfigEntryBase> handled = new HashSet<ConfigEntryBase>();
        bool saveOnSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
        {
            // An earlier renumbering can leave the same setting under several old numbers; the highest is the latest
            foreach (ConfigDefinition old in orphans.Keys.OrderByDescending(d => SectionNumber(d.Section)).ToList())
            {
                string oldName = SectionName(old.Section);
                ConfigEntryBase entry = ((IDictionary<ConfigDefinition, ConfigEntryBase>)config).Values.FirstOrDefault(e => e.Definition.Key == old.Key && e.Definition.Section != old.Section
                    && (SectionName(e.Definition.Section) == oldName || OldSections.TryGetValue(SectionName(e.Definition.Section), out string[] before) && before.Contains(oldName)));
                if (entry == null)
                {
                    continue;   // (not one of ours any more: left as it is)
                }
                if (handled.Add(entry) && Equals(entry.BoxedValue, entry.DefaultValue))
                {
                    try
                    {
                        entry.SetSerializedValue(orphans[old]);
                        carried++;
                    }
                    catch (System.Exception e)
                    {
                        Jotunn.Logger.LogWarning("[VanityWardrobe] Could not carry over " + old.Section + " / " + old.Key + ": " + e.Message);
                    }
                }
                orphans.Remove(old);
                dropped++;
            }
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
        }
        if (dropped > 0)
        {
            config.Save();
            Jotunn.Logger.LogInfo("[VanityWardrobe] Carried " + carried + " setting(s) over from old section names in the config file.");
        }
    }

    // A section's name without the number in front ("4 - Fortified Ballista": Fortified Ballista)
    private static string SectionName(string section) => Regex.Replace(section ?? "", @"^\d+\s*-\s*", "");

    private static int SectionNumber(string section)
    {
        Match match = Regex.Match(section ?? "", @"^(\d+)\s*-");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }
}
