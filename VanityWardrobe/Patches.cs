using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VanityWardrobe;

// While the wardrobe is open, Escape closes it rather than opening the pause menu behind it, and the mouse wheel zooms the
// model rather than the game's camera, build menu or hotbar.
[HarmonyPatch]
internal static class Patches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Menu), "Update")]
    private static bool MenuUpdate()
    {
        return !WardrobePanel.IsOpen;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    private static void MouseScroll(ref float __result)
    {
        if (WardrobePanel.IsOpen)
        {
            __result = 0f;
        }
    }

    // A few armor prefabs (Deep North Heavy) name an attach point the character does not have, and the game logs "Missing joint ..."
    // each time it dresses a model in them. It skips that part and carries on, so on the fitting room's model the warning is only
    // noise. It is dropped while the game dresses that model, and kept for everything else.
    private static bool s_dressingPreview;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VisEquipment), "AttachArmor")]
    private static void DressingStarts(VisEquipment __instance)
    {
        s_dressingPreview = __instance != null && __instance == FittingRoom.PreviewVis;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(VisEquipment), "AttachArmor")]
    private static void DressingEnds()
    {
        s_dressingPreview = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZLog), nameof(ZLog.LogWarning))]
    private static bool Warning(object o)
    {
        return !(s_dressingPreview && o is string text && text.StartsWith("Missing joint "));
    }

    // Colours: items are coloured as the game puts them on a model, and a model is coloured again when its colours change
    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisEquipment), "AttachArmor")]
    private static void ArmorAttached(VisEquipment __instance, int itemHash, List<GameObject> __result)
    {
        Recolor.OnAttached(__instance, itemHash, __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    private static void ItemAttached(VisEquipment __instance, int itemHash, GameObject __result)
    {
        if (__result != null)
        {
            Recolor.OnAttached(__instance, itemHash, new[] { __result });
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
    private static void VisualsUpdated(VisEquipment __instance)
    {
        Recolor.Watch(__instance);
    }
}
