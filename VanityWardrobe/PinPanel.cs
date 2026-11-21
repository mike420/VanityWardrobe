using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using MagicMike.ModSettings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VanityWardrobe;

// Left click on a pin collapses or expands its materials, right click unpins it (they work whenever the mouse pointer is free, for example
// with the inventory open)
internal sealed class PinClick : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Pin Pin;

    public void OnBeginDrag(PointerEventData eventData)
    {
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        PinPanel.EndDrag();
    }

    // Dragging with the left button moves the whole panel
    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            PinPanel.Drag(eventData);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging)
        {
            return;
        }
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Pins.Remove(Pin);
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            Pins.SetCollapsed(Pin, !Pin.Collapsed);
        }
    }
}

// A middle click on a list row
internal sealed class MiddleClick : MonoBehaviour, IPointerClickHandler
{
    public System.Action Action;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Middle)
        {
            Action?.Invoke();
        }
    }
}

// The pinned recipes, drawn on the game's HUD under the minimap. Each shows its materials with how many you have out of how many it takes,
// green once there is enough and red while short.
internal static class PinPanel
{
    private sealed class CountLine
    {
        public TMP_Text Text;
        public ItemDrop Item;
        public int Need;
        public string Name;
    }

    private static readonly Color Enough = new Color(0.55f, 0.85f, 0.45f, 1f);
    private static readonly Color Short = new Color(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Color Back = new Color(0f, 0f, 0f, 0.55f);
    private const float Width = 250f;

    private static RectTransform s_root;
    private static int s_builtRevision = -1;
    private static float s_nextCounts;
    private static readonly List<CountLine> s_lines = new List<CountLine>();

    public static void Tick()
    {
        Player player = Player.m_localPlayer;
        if (!MainConfig.showPins.Value || player == null || Hud.instance == null || Minimap.instance == null)
        {
            Hide();
            return;
        }
        Pins.EnsureLoaded(player);
        bool wanted = Pins.All.Count > 0 && !Minimap.IsOpen();
        if (!wanted)
        {
            Hide();
            return;
        }
        if (s_root == null)
        {
            Make();
            s_builtRevision = -1;
        }
        if (s_builtRevision != Pins.Revision)
        {
            s_builtRevision = Pins.Revision;
            Stock.Refresh();
            Rebuild();
            s_nextCounts = 0f;
        }
        s_root.gameObject.SetActive(true);
        Place();
        if (Time.unscaledTime >= s_nextCounts)
        {
            s_nextCounts = Time.unscaledTime + 0.5f;
            Stock.RefreshIfDue(3f);
            UpdateCounts();
        }
    }

    public static void Shutdown()
    {
        if (s_root != null)
        {
            Object.Destroy(s_root.gameObject);
        }
        s_root = null;
    }

    // Moves the panel by how far the pointer moved, and keeps that in the two offset settings
    public static void Drag(PointerEventData eventData)
    {
        RectTransform hud = (RectTransform)Hud.instance.m_rootObject.transform;
        Camera camera = eventData.pressEventCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, eventData.position - eventData.delta, camera, out Vector2 before)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, eventData.position, camera, out Vector2 now))
        {
            Vector2 move = now - before;
            // The settings file is written once, when the drag ends, not on every movement
            ConfigFile file = MainConfig.pinOffset.ConfigFile;
            bool saving = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            MainConfig.pinOffsetX.Value = Mathf.Clamp(MainConfig.pinOffsetX.Value + move.x, -2000f, 2000f);
            MainConfig.pinOffset.Value = Mathf.Clamp(MainConfig.pinOffset.Value - move.y, -2000f, 2000f);
            file.SaveOnConfigSet = saving;
        }
    }

    public static void EndDrag()
    {
        MainConfig.pinOffset.ConfigFile.Save();
    }

    private static void Hide()
    {
        if (s_root != null && s_root.gameObject.activeSelf)
        {
            s_root.gameObject.SetActive(false);
        }
    }

    private static void Make()
    {
        GameObject go = Widgets.Child(Hud.instance.m_rootObject.transform, "VanityWardrobePins");
        s_root = (RectTransform)go.transform;
        s_root.anchorMin = s_root.anchorMax = new Vector2(1f, 1f);
        s_root.pivot = new Vector2(1f, 1f);
        s_root.sizeDelta = new Vector2(Width, 10f);
        VerticalLayoutGroup stack = go.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 4f;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    // Just under the minimap, lined up with its right edge; lower again while the sailing dials are showing
    private static void Place()
    {
        RectTransform hud = (RectTransform)Hud.instance.m_rootObject.transform;
        s_root.localScale = Vector3.one * MainConfig.pinScale.Value;
        float right = hud.rect.xMax - 8f;
        float bottom = hud.rect.yMax - 8f;
        RectTransform small = Minimap.instance.m_smallRoot != null ? Minimap.instance.m_smallRoot.transform as RectTransform : null;
        if (small != null && small.gameObject.activeInHierarchy)
        {
            Vector3[] corners = new Vector3[4];
            small.GetWorldCorners(corners);
            Vector3 low = hud.InverseTransformPoint(corners[3]);
            right = low.x;
            bottom = low.y - 10f;
        }
        if (Hud.instance.m_shipHudRoot != null && Hud.instance.m_shipHudRoot.activeSelf)
        {
            bottom -= 150f;
        }
        s_root.anchoredPosition = new Vector2(right + MainConfig.pinOffsetX.Value - hud.rect.xMax, bottom - hud.rect.yMax - MainConfig.pinOffset.Value);
    }

    private static void Rebuild()
    {
        for (int i = s_root.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(s_root.GetChild(i).gameObject);
        }
        s_lines.Clear();
        int shown = 0;
        int hidden = 0;
        foreach (Pin pin in Pins.All)
        {
            List<KeyValuePair<ItemDrop, int>> need = Stock.Needed(pin.Prefabs, pin.Quality);
            if (need.Count == 0 && !pin.Prefabs.Any(p => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(p) != null))
            {
                continue;   // an item that is not in the game now (a mod taken out): kept, not shown
            }
            if (shown >= MainConfig.pinMax.Value)
            {
                hidden++;
                continue;
            }
            shown++;
            AddPin(pin, need);
        }
        if (hidden > 0)
        {
            TMP_Text more = Widgets.Label(s_root, Translations.T("vanitywardrobe_pin_more", hidden.ToString()), 14f, Theme.Muted, TextAlignmentOptions.MidlineRight);
            more.raycastTarget = false;
        }
    }

    private static Sprite s_rounded;

    // A small white rounded square that stretches without distorting its corners (9-sliced)
    public static Sprite RoundedSprite()
    {
        if (s_rounded != null)
        {
            return s_rounded;
        }
        const int size = 24;
        const float radius = 6f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        s_rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        s_rounded.hideFlags = HideFlags.HideAndDontSave;
        return s_rounded;
    }

    private static void AddPin(Pin pin, List<KeyValuePair<ItemDrop, int>> need)
    {
        Image card = Widgets.Box(s_root, "Pin", Back);
        card.sprite = RoundedSprite();
        card.type = Image.Type.Sliced;
        card.gameObject.AddComponent<PinClick>().Pin = pin;
        VerticalLayoutGroup inside = card.gameObject.AddComponent<VerticalLayoutGroup>();
        inside.padding = new RectOffset(8, 8, 6, 6);
        inside.spacing = 2f;
        inside.childControlWidth = true;
        inside.childControlHeight = true;
        inside.childForceExpandWidth = true;
        inside.childForceExpandHeight = false;

        string title = pin.Title + (pin.Quality > 1 ? "  " + Translations.T("vanitywardrobe_pin_quality", pin.Quality.ToString()) : "");
        TMP_Text head = Widgets.Label(card.transform, (pin.Collapsed ? "> " : "v ") + title, 16f, Theme.TextBright, TextAlignmentOptions.MidlineLeft, bold: true);
        head.raycastTarget = false;
        head.textWrappingMode = TextWrappingModes.NoWrap;
        head.overflowMode = TextOverflowModes.Ellipsis;
        if (pin.Collapsed)
        {
            return;
        }
        foreach (KeyValuePair<ItemDrop, int> pair in need)
        {
            TMP_Text line = Widgets.Label(card.transform, "", 14f, Theme.Text, TextAlignmentOptions.MidlineLeft);
            line.raycastTarget = false;
            line.richText = true;
            line.textWrappingMode = TextWrappingModes.NoWrap;
            line.overflowMode = TextOverflowModes.Ellipsis;
            s_lines.Add(new CountLine
            {
                Text = line,
                Item = pair.Key,
                Need = pair.Value,
                Name = Localization.instance.Localize(pair.Key.m_itemData.m_shared.m_name),
            });
        }
    }

    private static void UpdateCounts()
    {
        foreach (CountLine line in s_lines)
        {
            if (line.Text == null)
            {
                continue;
            }
            int have = Stock.Have(line.Item);
            string colour = Theme.Tag(have >= line.Need ? Enough : Short);
            line.Text.text = line.Name + "  <color=" + colour + ">" + have + "/" + line.Need + "</color>";
        }
    }
}
