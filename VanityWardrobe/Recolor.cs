using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VanityWardrobe;

// Changes an item's main colour. The main colour is the hue most of the item's colourful pixels have (the cloth or leather, not the grey
// metal or the dark trim), found from its albedo texture. Pixels near that hue are moved to the chosen colour and everything else is
// left as it was. The changed copy of the texture is made once and kept. An item with no colourful pixels at all (grey iron, say) is
// coloured over all of it.
internal static class Recolor
{
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private const int MaxSize = 1024;
    private const int MaxCached = 120;

    private sealed class Analysis
    {
        public float Hue;
        public bool Neutral;
    }

    private static readonly Dictionary<int, Analysis> s_analysis = new Dictionary<int, Analysis>();
    private static readonly Dictionary<long, Texture2D> s_made = new Dictionary<long, Texture2D>();
    private static readonly Queue<long> s_order = new Queue<long>();

    // What each material and renderer looked like before, so the original colour can be put back
    private static readonly ConditionalWeakTable<Material, Texture> s_original = new ConditionalWeakTable<Material, Texture>();
    private static readonly ConditionalWeakTable<Renderer, string> s_touched = new ConditionalWeakTable<Renderer, string>();

    // The last colours shown on each model, so it is coloured again when they change
    private static readonly ConditionalWeakTable<VisEquipment, string> s_shown = new ConditionalWeakTable<VisEquipment, string>();

    // ---- models ---------------------------------------------------------------------------------------------------------------

    // Called after the game has put an item on a model
    public static void OnAttached(VisEquipment vis, int itemHash, IEnumerable<GameObject> instances)
    {
        if (instances == null)
        {
            return;
        }
        Dictionary<string, int> colors = Colors.For(vis, out _);
        int preset = PresetOf(colors, itemHash);
        foreach (GameObject instance in instances)
        {
            Tint(instance, preset);
        }
    }

    // Called every frame for each model: colours it again if the colours it should show have changed
    public static void Watch(VisEquipment vis)
    {
        if (!vis.m_isPlayer && vis != FittingRoom.PreviewVis)
        {
            return;
        }
        Dictionary<string, int> colors = Colors.For(vis, out string stamp);
        if (stamp == null)
        {
            return;
        }
        if (s_shown.TryGetValue(vis, out string shown) && shown == stamp)
        {
            return;
        }
        s_shown.Remove(vis);
        s_shown.Add(vis, stamp);
        TintWorn(vis, colors);
    }

    private static void TintWorn(VisEquipment vis, Dictionary<string, int> colors)
    {
        Tint(vis.m_helmetItemInstance, PresetOf(colors, vis.m_helmetItem));
        TintAll(vis.m_chestItemInstances, PresetOf(colors, vis.m_chestItem));
        TintAll(vis.m_legItemInstances, PresetOf(colors, vis.m_legItem));
        TintAll(vis.m_shoulderItemInstances, PresetOf(colors, vis.m_shoulderItem));
        Tint(vis.m_rightItemInstance, PresetOf(colors, vis.m_rightItem));
        Tint(vis.m_leftItemInstance, PresetOf(colors, vis.m_leftItem));
    }

    private static void TintAll(List<GameObject> instances, int preset)
    {
        if (instances == null)
        {
            return;
        }
        foreach (GameObject instance in instances)
        {
            Tint(instance, preset);
        }
    }

    private static int PresetOf(Dictionary<string, int> colors, int itemHash)
    {
        if (colors == null || colors.Count == 0 || itemHash == 0 || ObjectDB.instance == null)
        {
            return 0;
        }
        GameObject prefab = ObjectDB.instance.GetItemPrefab(itemHash);
        return prefab != null && colors.TryGetValue(prefab.name, out int preset) ? preset : 0;
    }

    private static void Tint(GameObject root, int preset)
    {
        if (root == null)
        {
            return;
        }
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            {
                Tint(renderer, preset);
            }
        }
    }

    // Sets the albedo of each of the renderer's materials to the recoloured one, or back to the original for preset 0
    private static void Tint(Renderer renderer, int preset)
    {
        bool touched = s_touched.TryGetValue(renderer, out _);
        if (preset <= 0 && !touched)
        {
            return;
        }
        Material[] materials = renderer.materials;
        foreach (Material material in materials)
        {
            if (material == null || !material.HasProperty(MainTex))
            {
                continue;
            }
            if (!s_original.TryGetValue(material, out Texture original))
            {
                original = material.GetTexture(MainTex);
                s_original.Add(material, original);
            }
            if (original == null)
            {
                continue;
            }
            Texture target = preset <= 0 ? original : Recoloured(original, preset);
            if (target != null)
            {
                material.SetTexture(MainTex, target);
            }
        }
        if (!touched)
        {
            s_touched.Add(renderer, "1");
        }
    }

    public static void Clear()
    {
        foreach (Texture2D texture in s_made.Values)
        {
            if (texture != null)
            {
                Object.Destroy(texture);
            }
        }
        s_made.Clear();
        s_order.Clear();
        s_analysis.Clear();
    }

    // ---- textures ------------------------------------------------------------------------------------------------------------

    private static Texture Recoloured(Texture source, int preset)
    {
        if (!(source is Texture2D) || source.width < 4 || source.height < 4)
        {
            return null;
        }
        long key = ((long)source.GetInstanceID() << 8) | (uint)preset;
        if (s_made.TryGetValue(key, out Texture2D made) && made != null)
        {
            return made;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Texture2D copy = Make(source, preset);
        if (copy == null)
        {
            return null;
        }
        s_made[key] = copy;
        s_order.Enqueue(key);
        while (s_order.Count > MaxCached)
        {
            long oldest = s_order.Dequeue();
            if (s_made.TryGetValue(oldest, out Texture2D old) && old != null)
            {
                Object.Destroy(old);
            }
            s_made.Remove(oldest);
        }
        if (watch.ElapsedMilliseconds > 150)
        {
            Debug.Log("[VanityWardrobe] Recolouring " + source.name + " took " + watch.ElapsedMilliseconds + " ms.");
        }
        return copy;
    }

    // Reads a texture even if the game made it unreadable, by drawing it into a render texture first
    private static Color32[] Read(Texture source, out int width, out int height)
    {
        float scale = Mathf.Min(1f, (float)MaxSize / Mathf.Max(source.width, source.height));
        width = Mathf.Max(4, Mathf.RoundToInt(source.width * scale));
        height = Mathf.Max(4, Mathf.RoundToInt(source.height * scale));
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            Color32[] pixels = readable.GetPixels32();
            Object.Destroy(readable);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static Texture2D Make(Texture source, int preset)
    {
        Color32[] pixels = Read(source, out int width, out int height);
        int id = source.GetInstanceID();
        if (!s_analysis.TryGetValue(id, out Analysis analysis))
        {
            analysis = Analyse(pixels);
            s_analysis[id] = analysis;
        }
        Apply(pixels, Colors.Presets[preset - 1], analysis);
        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = source.name + " (recoloured)",
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            anisoLevel = source.anisoLevel,
        };
        result.SetPixels32(pixels);
        result.Apply(true);
        result.Compress(false);
        result.Apply(false, true);
        return result;
    }

    // The main colour: the hue with most weight among the pixels that have any colour (saturated and not too dark)
    private static Analysis Analyse(Color32[] pixels)
    {
        const int bins = 72;
        float[] weight = new float[bins];
        float total = 0f;
        int sampled = 0;
        for (int i = 0; i < pixels.Length; i += 3)
        {
            sampled++;
            Color.RGBToHSV(new Color(pixels[i].r / 255f, pixels[i].g / 255f, pixels[i].b / 255f), out float h, out float s, out float v);
            if (s > 0.25f && v > 0.15f)
            {
                float w = s * v;
                weight[Mathf.Min(bins - 1, (int)(h * bins))] += w;
                total += w;
            }
        }
        if (sampled == 0 || total < sampled * 0.02f)
        {
            return new Analysis { Neutral = true };
        }
        int peak = 0;
        float best = -1f;
        for (int b = 0; b < bins; b++)
        {
            float sum = weight[(b + bins - 1) % bins] + weight[b] + weight[(b + 1) % bins];
            if (sum > best)
            {
                best = sum;
                peak = b;
            }
        }
        // The peak's hue as a circle average, so a hue near red (0 and 1) is not pulled to the middle
        float x = 0f;
        float y = 0f;
        for (int d = -3; d <= 3; d++)
        {
            int b = (peak + d + bins) % bins;
            float angle = (b + 0.5f) / bins * Mathf.PI * 2f;
            x += weight[b] * Mathf.Cos(angle);
            y += weight[b] * Mathf.Sin(angle);
        }
        float hue = Mathf.Atan2(y, x) / (Mathf.PI * 2f);
        return new Analysis { Hue = hue < 0f ? hue + 1f : hue };
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static void Apply(Color32[] pixels, Colors.Preset preset, Analysis analysis)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            Color.RGBToHSV(new Color(p.r / 255f, p.g / 255f, p.b / 255f), out float h, out float s, out float v);
            float mask;
            if (analysis.Neutral)
            {
                // Nothing coloured to pick out, so colour the whole item except the very dark and very bright
                mask = Smooth(0.08f, 0.22f, v) * (1f - Smooth(0.93f, 1f, v));
            }
            else
            {
                float distance = Mathf.Abs(h - analysis.Hue);
                distance = Mathf.Min(distance, 1f - distance);
                mask = (1f - Smooth(0.07f, 0.16f, distance)) * Smooth(0.12f, 0.3f, s) * Smooth(0.08f, 0.2f, v);
            }
            if (mask <= 0.003f)
            {
                continue;
            }
            float newH = h;
            float newS = s;
            float newV = v;
            switch (preset.Kind)
            {
                case Colors.Kind.Hue:
                    newH = analysis.Neutral ? preset.Hue : Mathf.Repeat(preset.Hue + (h - analysis.Hue) * 0.5f, 1f);
                    newS = analysis.Neutral ? 0.55f : Mathf.Clamp01(s + 0.08f);
                    break;
                case Colors.Kind.Black:
                    newS = s * 0.25f;
                    newV = v * 0.25f;
                    break;
                case Colors.Kind.White:
                    newS = s * 0.08f;
                    newV = Mathf.Lerp(v, 1f, 0.6f);
                    break;
                default:
                    newS = s * 0.05f;
                    newV = 0.25f + v * 0.45f;
                    break;
            }
            Color changed = Color.HSVToRGB(newH, newS, newV);
            pixels[i] = new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(p.r, changed.r * 255f, mask)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(p.g, changed.g * 255f, mask)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(p.b, changed.b * 255f, mask)),
                p.a);
        }
    }
}
