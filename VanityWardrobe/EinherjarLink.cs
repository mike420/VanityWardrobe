using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace VanityWardrobe;

// Einherjar Bodies' look API, found by name when that mod is installed: its eye and underwear colours. Without the mod, Available is
// false and the appearance panel leaves those parts out. (VillagePlebs finds the same API the same way.) A look is the eyes as text; its
// fields (EyeHue, EyeSaturation, EyeValue, Glow, IrisSize, HideIris) are each from 0 to 1. An underwear colour is its hue (0),
// saturation (1) or brightness (2), each from 0 to 1.
internal static class EinherjarLink
{
    private const string Guid = "MagicMike.EinherjarBodies";
    private const int ApiVersion = 1;

    // Where the mod keeps each character's eyes and underwear colour, with the character (the names carry its own version)
    private const string EyesKey = "einherjar_eyes_v1";
    private const string UnderwearKey = "einherjar_underwear_v1";

    private static bool s_looked;
    private static Func<string> s_defaultLook;
    private static Func<string, string, float> s_getField;
    private static Func<string, string, float, string> s_setField;
    private static Action<GameObject, string> s_applyLook;
    private static Func<string> s_underwearDefault;
    private static Func<string, int, float> s_underwearPart;
    private static Func<string, int, float, string> s_setUnderwearPart;
    private static Action<GameObject, string> s_applyUnderwear;
    private static Action<VisEquipment, VisEquipment> s_useLookOf;

    public static bool Available
    {
        get
        {
            Find();
            return s_applyLook != null;
        }
    }

    private static void Find()
    {
        if (s_looked)
        {
            return;
        }
        s_looked = true;
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out BepInEx.PluginInfo plugin) || plugin.Instance == null)
            {
                return;
            }
            Type api = plugin.Instance.GetType().Assembly.GetType("EinherjarBodies.EinherjarLookApi");
            FieldInfo version = api?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (version == null || (int)version.GetValue(null) != ApiVersion)
            {
                Debug.Log("[VanityWardrobe] Einherjar Bodies is installed without a look API this version understands: the eye and underwear settings are left out.");
                return;
            }
            s_defaultLook = Bind<Func<string>>(api, "DefaultLook");
            s_getField = Bind<Func<string, string, float>>(api, "GetField");
            s_setField = Bind<Func<string, string, float, string>>(api, "SetField");
            s_underwearDefault = Bind<Func<string>>(api, "UnderwearDefault");
            s_underwearPart = Bind<Func<string, int, float>>(api, "UnderwearPart");
            s_setUnderwearPart = Bind<Func<string, int, float, string>>(api, "SetUnderwearPart");
            s_applyUnderwear = Bind<Action<GameObject, string>>(api, "ApplyUnderwear");
            s_useLookOf = Bind<Action<VisEquipment, VisEquipment>>(api, "UseLookOf");
            s_applyLook = Bind<Action<GameObject, string>>(api, "ApplyLook");   // (last: it is what Available checks)
            Debug.Log("[VanityWardrobe] Einherjar Bodies found: the appearance panel has its eye and underwear settings.");
        }
        catch (Exception e)
        {
            s_applyLook = null;
            Debug.LogWarning("[VanityWardrobe] Could not use Einherjar Bodies' look API: " + e.Message);
        }
    }

    private static T Bind<T>(Type api, string name) where T : Delegate
    {
        MethodInfo method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(api.FullName, name);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    // ---- eyes

    public static string Eyes(Player player)
    {
        return Available && player.m_customData.TryGetValue(EyesKey, out string look) && !string.IsNullOrEmpty(look) ? look : DefaultLook();
    }

    public static string DefaultLook() => Available ? s_defaultLook() : "";

    public static float Get(string look, string field) => Available ? s_getField(look, field) : 0f;

    public static void SetEye(Player player, string field, float value)
    {
        if (Available)
        {
            s_applyLook(player.gameObject, s_setField(Eyes(player), field, value));
        }
    }

    // ---- underwear

    public static string Underwear(Player player)
    {
        return Available && player.m_customData.TryGetValue(UnderwearKey, out string colour) && !string.IsNullOrEmpty(colour) ? colour : UnderwearDefault();
    }

    public static string UnderwearDefault() => Available ? s_underwearDefault() : "";

    public static float UnderwearPart(string colour, int part) => Available ? s_underwearPart(colour, part) : 0f;

    public static void SetUnderwear(Player player, int part, float value)
    {
        if (Available)
        {
            s_applyUnderwear(player.gameObject, s_setUnderwearPart(Underwear(player), part, value));
        }
    }

    // ---- other characters

    // Makes a model that has no look of its own (the fitting-room model) show another character's eyes and underwear. null stops it.
    public static void UseLookOf(VisEquipment shown, VisEquipment source)
    {
        if (Available)
        {
            s_useLookOf(shown, source);
        }
    }
}
