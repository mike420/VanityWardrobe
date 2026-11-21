using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VanityWardrobe;

// The fitting-room model: a copy of the player prefab, made the way the game's own character-select screen makes its preview
// (with no network object), far above the world where nothing else can see it, and drawn by its own camera into a texture.
// It is taken out of the game's lists of characters and players, so nothing in the world treats it as a real one.
internal sealed class FittingRoom
{
    private static readonly Vector3 Origin = new Vector3(0f, 9000f, 0f);
    private const int TextureHeight = 880;
    // The camera, as in BetterEquipSlots' preview: a zoom from 0 (the whole body, FarDistance away looking at FarHeight) to 1 (head and shoulders,
    // NearDistance away looking at NearHeight), eased towards where the wheel put it; and a pan up and down the body from dragging, kept so the height
    // looked at stays between LowestLook and HighestLook.
    private const float FarDistance = 4.7f;
    private const float NearDistance = 1.2f;
    private const float FarHeight = 0.92f;
    private const float NearHeight = 1.55f;
    private const float ZoomPerNotch = 0.2f;
    private const float ZoomSeconds = 0.05f;
    private const float PanPerPixel = 0.004f;
    private const float LowestLook = 0.15f;
    private const float HighestLook = 1.85f;

    private GameObject _root;
    private GameObject _dummy;
    private Camera _camera;
    private VisEquipment _vis;
    private Animator[] _animators = new Animator[0];
    private Entry _weapon;
    private Entry _shield;
    private float _blockUntil;

    // The preview model's equipment, so a patch can tell it from the real player's
    public static VisEquipment PreviewVis { get; private set; }
    private float _yaw = 330f;
    private float _zoom;
    private float _zoomTarget;
    private float _zoomSpeed;
    private float _pan;

    public RenderTexture Texture { get; private set; }

    public bool IsOpen => _dummy != null;

    // The model itself, for showing it eyes and underwear
    public GameObject Model => _dummy;

    // aspect is the width over the height of the frame the picture is shown in, so the model is not stretched
    public void Open(float aspect)
    {
        if (IsOpen)
        {
            return;
        }
        Player local = Player.m_localPlayer;
        GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
        if (local == null || prefab == null)
        {
            return;
        }

        _zoom = _zoomTarget = _zoomSpeed = _pan = 0f;
        _root = new GameObject("VanityWardrobeRoom");
        _root.transform.position = Origin;

        ZNetView.m_forceDisableInit = true;
        try
        {
            _dummy = Object.Instantiate(prefab, Origin, Quaternion.Euler(0f, _yaw, 0f), _root.transform);
        }
        finally
        {
            ZNetView.m_forceDisableInit = false;
        }
        Object.Destroy(_dummy.GetComponent<Rigidbody>());
        _animators = _dummy.GetComponentsInChildren<Animator>();
        foreach (Animator animator in _dummy.GetComponentsInChildren<Animator>())
        {
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
        }
        Player player = _dummy.GetComponent<Player>();
        Character.s_characters.Remove(player);
        Player.s_players.Remove(player);
        _vis = _dummy.GetComponent<VisEquipment>();
        PreviewVis = _vis;
        CopyLook(local);
        if (EinherjarLink.Available)
        {
            EinherjarLink.CopyLook(local, player);   // (the model keeps its own copy: nothing done here reaches the real character)
        }

        Texture = new RenderTexture(Mathf.Clamp(Mathf.RoundToInt(TextureHeight * aspect), 320, 1760), TextureHeight, 24, RenderTextureFormat.ARGB32) { name = "VanityWardrobe preview", antiAliasing = 1 };
        Texture.Create();
        _camera = new GameObject("VanityWardrobeCamera").AddComponent<Camera>();
        _camera.transform.SetParent(_root.transform, false);
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.1f, 0.08f, 0.07f, 1f);
        _camera.fieldOfView = 26f;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 40f;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.targetTexture = Texture;
        AddLight("Key", new Vector3(1.8f, 2.4f, 3.2f), new Color(1f, 0.93f, 0.8f), 2.4f, 14f);
        AddLight("Fill", new Vector3(-2.2f, 1.3f, 2.6f), new Color(0.7f, 0.8f, 1f), 1.1f, 14f);
        AddLight("Rim", new Vector3(0.5f, 2.2f, -2.5f), new Color(1f, 0.8f, 0.6f), 1.4f, 12f);
        PlaceCamera();
    }

    public void Close()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
        if (Texture != null)
        {
            Texture.Release();
            Object.Destroy(Texture);
        }
        _root = null;
        _dummy = null;
        _camera = null;
        _vis = null;
        PreviewVis = null;
        Texture = null;
    }

    public void Update()
    {
        if (!IsOpen)
        {
            return;
        }
        _dummy.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        if (_blockUntil > 0f && Time.time >= _blockUntil)
        {
            _blockUntil = 0f;
            SetBlocking(false);
        }
        if (_zoom != _zoomTarget)
        {
            _zoom = Mathf.SmoothDamp(_zoom, _zoomTarget, ref _zoomSpeed, ZoomSeconds, float.PositiveInfinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(_zoom - _zoomTarget) < 0.001f)
            {
                _zoom = _zoomTarget;
                _zoomSpeed = 0f;
            }
        }
        PlaceCamera();
    }

    private Animator Main => _animators.FirstOrDefault(a => a != null && a.runtimeAnimatorController != null);

    private void SetBlocking(bool on)
    {
        Animator animator = Main;
        if (animator != null)
        {
            animator.SetBool("blocking", on);
        }
    }

    // Plays the attack of what the model holds (or its fists), as the game does: the weapon names its animation, and one with a chain
    // of attacks or several to pick from has the first one's name end in 0
    public void Attack()
    {
        Animator animator = Main;
        Player local = Player.m_localPlayer;
        if (!IsOpen || animator == null || local == null)
        {
            return;
        }
        ItemDrop.ItemData.SharedData shared = _weapon != null ? _weapon.Shared : local.m_unarmedWeapon != null ? local.m_unarmedWeapon.m_itemData.m_shared : null;
        Attack attack = shared != null ? shared.m_attack : null;
        if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation))
        {
            return;
        }
        _blockUntil = 0f;
        animator.SetBool("blocking", false);
        string trigger = attack.m_attackAnimation;
        if (attack.m_attackChainLevels > 1 || attack.m_attackRandomAnimations >= 2)
        {
            trigger += "0";
        }
        animator.SetTrigger(trigger);
    }

    public void Block()
    {
        if (IsOpen && Main != null)
        {
            SetBlocking(true);
            _blockUntil = Time.time + 0.9f;
        }
    }

    public void Rotate(float degrees)
    {
        _yaw = Mathf.Repeat(_yaw + degrees, 360f);
    }

    // Moves the camera up or down the body as the pointer is dragged (the picture follows the pointer)
    public void Pan(float pixels)
    {
        _pan -= pixels * PanPerPixel;
    }

    // Wheel notches: each brings the camera the same share closer (positive) or further away
    public void Zoom(float notches)
    {
        _zoomTarget = Mathf.Clamp01(_zoomTarget + Mathf.Clamp(notches, -1f, 1f) * ZoomPerNotch);
    }

    // Shows a look on the model: body, skin, hair and beard. Only the model changes, never the player.
    public void ShowLook(int model, Vector3 skin, Vector3 hairColor, string hair, string beard)
    {
        if (!IsOpen || _vis == null)
        {
            return;
        }
        _vis.SetModel(model);
        _vis.SetSkinColor(skin);
        _vis.SetHairColor(hairColor);
        _vis.SetHairItem(hair.GetStableHashCode());
        _vis.SetBeardItem(beard.GetStableHashCode());
    }

    // The same look as the player's: body, skin, hair and beard
    private void CopyLook(Player local)
    {
        ZDO zdo = local.m_nview != null ? local.m_nview.GetZDO() : null;
        if (zdo == null)
        {
            return;
        }
        _vis.SetModel(zdo.GetInt(ZDOVars.s_modelIndex));
        _vis.SetSkinColor(zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one));
        _vis.SetHairColor(zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one));
        _vis.SetHairItem(zdo.GetInt(ZDOVars.s_hairItem));
        _vis.SetBeardItem(zdo.GetInt(ZDOVars.s_beardItem));
    }

    // What the player is wearing and holding now, as an outfit
    public static Outfit MyGear()
    {
        Outfit outfit = new Outfit();
        Player local = Player.m_localPlayer;
        ZDO zdo = local != null && local.m_nview != null ? local.m_nview.GetZDO() : null;
        if (zdo == null)
        {
            return outfit;
        }
        Catalog.EnsureBuilt();
        Dictionary<int, Entry> byHash = new Dictionary<int, Entry>();
        foreach (Entry entry in Catalog.Items)
        {
            byHash[entry.Hash] = entry;
        }
        void Put(int hash)
        {
            if (hash != 0 && byHash.TryGetValue(hash, out Entry entry))
            {
                outfit.Wear(entry);
            }
        }
        Put(zdo.GetInt(ZDOVars.s_helmetItem));
        Put(zdo.GetInt(ZDOVars.s_chestItem));
        Put(zdo.GetInt(ZDOVars.s_legItem));
        Put(zdo.GetInt(ZDOVars.s_shoulderItem));
        // A weapon that is put away hangs on the back, and still belongs to the look
        Put(zdo.GetInt(ZDOVars.s_rightItem) != 0 ? zdo.GetInt(ZDOVars.s_rightItem) : zdo.GetInt(ZDOVars.s_rightBackItem));
        Put(zdo.GetInt(ZDOVars.s_leftItem) != 0 ? zdo.GetInt(ZDOVars.s_leftItem) : zdo.GetInt(ZDOVars.s_leftBackItem));
        return outfit;
    }

    public void Apply(Outfit outfit)
    {
        if (!IsOpen || _vis == null)
        {
            return;
        }
        _vis.SetHelmetItem(Hash(outfit[Slot.Helmet]));
        _vis.SetChestItem(Hash(outfit[Slot.Chest]));
        _vis.SetLegItem(Hash(outfit[Slot.Legs]));
        _vis.SetShoulderItem(Hash(outfit[Slot.Cape]), 0, 1);

        Entry weapon = outfit[Slot.Weapon];
        Entry shield = outfit[Slot.Shield];
        bool weaponInLeft = weapon != null && Outfit.IsLeftHand(weapon);
        _vis.SetRightItem(weapon != null && !weaponInLeft ? Hash(weapon) : 0, 1);
        int left = weaponInLeft ? Hash(weapon) : Hash(shield);
        _vis.SetLeftItem(left, 0, 1);

        // The stance the game gives a character holding these: what is in the left hand decides first (a shield), then the right
        _weapon = weapon;
        _shield = shield;
        Entry leftEntry = weaponInLeft ? weapon : shield;
        Entry rightEntry = weaponInLeft ? null : weapon;
        ItemDrop.ItemData.AnimationState pose = leftEntry != null ? leftEntry.Shared.m_animationState
            : rightEntry != null ? rightEntry.Shared.m_animationState
            : ItemDrop.ItemData.AnimationState.Unarmed;
        foreach (Animator animator in _animators)
        {
            if (animator != null)
            {
                animator.SetFloat("statef", (float)pose);
                animator.SetInteger("statei", (int)pose);
            }
        }
    }

    private static int Hash(Entry entry)
    {
        return entry != null ? entry.Hash : 0;
    }

    private void AddLight(string name, Vector3 offset, Color color, float intensity, float range)
    {
        Light light = new GameObject("VanityWardrobeLight " + name).AddComponent<Light>();
        light.transform.SetParent(_root.transform, false);
        light.transform.position = Origin + offset;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    private void PlaceCamera()
    {
        float distance = FarDistance * Mathf.Pow(NearDistance / FarDistance, _zoom);
        float height = Mathf.Lerp(FarHeight, NearHeight, Mathf.InverseLerp(FarDistance, NearDistance, distance));
        _pan = Mathf.Clamp(_pan, LowestLook - height, HighestLook - height);
        height += _pan;
        _camera.transform.position = Origin + new Vector3(0f, height, distance);
        _camera.transform.LookAt(Origin + new Vector3(0f, height, 0f));
    }
}
