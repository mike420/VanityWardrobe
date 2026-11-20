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
    }
}
