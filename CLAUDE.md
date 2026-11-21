# VanityWardrobe

A Valheim BepInEx mod by MagicMike: an in-game wardrobe and fitting room. **F9** opens a window listing every armor set, cape,
weapon and shield in `ObjectDB` (so items from other mods too), a model of the player's own character wears the chosen item, and
the details show stats and crafting costs. Client side only. It is the user's own take on the mod
`G:\Valheim_Stuff\New folder\GoIDA-Valhalla_Wardrobe-1.6.0` (a DLL and README only, no source). **Build from that README's
feature list. Do not decompile or copy its code.**

Set up like the user's other mods, **ValheimSkies** (`E:\Valheim_Mods\ValheimSkies`) is the closest model: same repo layout,
`Shared` submodule, csproj, `Package/` and `Nexus/` files, `UpdateManifest.ps1` and `UpdateNexusReadme.ps1`. When unsure how
something should be done, look at how Skies does it. Other mods are in `E:\Valheim_Mods` (`EinherjarBodies` is the biggest).

## Layout

```
VanityWardrobe.sln, Environment.props (machine paths, not in git), DoPrebuild.props, libraries/, LICENSE
Shared/                      git submodule (github.com/mike420/ValheimShared): the shared ModSettings panel code. Do not edit here.
tools/                       check.sh, restart.sh, gameinput.ps1 (see Building and testing)
VanityWardrobe/              the project
  VanityWardrobeMod.cs       plugin: Awake, Update (calls WardrobePanel.Tick), OnDestroy
  MainConfig.cs              Enable Plugin, Open Key (F9), Chest Radius (30), Start With My Gear
                             and CarryOverOldSections (copied from the other mods): settings found under an old section number move to the current one,
                             so renumbering a section keeps users' values; add renamed sections to `OldSections`. Checked by moving a section to `[5 - ...]`.
  Catalog.cs                 item list and set grouping, built from ObjectDB when the window opens
  Biomes.cs                  works out each item's biome from the world (see Biomes below); reads locations in the background; biomes.cache
  Colors.cs, Recolor.cs      per-item colour presets: the choices (colors.txt), the ZDO sync, and the texture recolouring (see Recolor below)
  AppearancePanel.cs         the panel to the right of the window: sex, skin, hair, beard, hair colour, and with Einherjar Bodies eyes and underwear
  EinherjarLink.cs           finds Einherjar Bodies' look API by GUID at run time (same pattern as VillagePlebs' EinherjarLook.cs); no hard dependency
  Outfit.cs                  what the model wears (one item per Slot), and SavedOutfits (BepInEx/config/VanityWardrobe/outfits.txt)
  FittingRoom.cs             the preview model, its camera and lights; MyGear() reads what the player wears from the ZDO
  WardrobePanel.cs           the whole window; also PreviewInput (drag to turn, wheel to zoom)
  Patches.cs                 Menu.Update prefix (Esc closes the window, not the pause menu); wheel postfix (no game zoom while open)
                             and a patch that drops the game's "Missing joint ..." warning (Deep North Heavy armor) while it dresses the preview model
  Translations.cs            copied from Skies: loads Assets/Translations/<Language>/VanityWardrobe.json
  Properties/                AssemblyInfo.cs, IgnoreAccessModifiers.cs (needed, see Gotchas)
  Assets/Translations/       all nine languages as Skies (keys are all vanitywardrobe_*)
  Package/, Nexus/           README, CHANGELOG, manifest, icon (placeholder) for Thunderstore and Nexus
```

Every `.cs` file is listed by hand in `VanityWardrobe.csproj` (old-style project): **add new files there too.** `tools/check.sh`
finds files itself, so it will not catch a missing csproj entry.

## How it works

- **Catalog:** `ObjectDB.instance.m_items` where the item type is Helmet, Chest, Legs, Shoulder (cape), one/two-handed weapon,
  bow or shield, with an icon. Rebuilt when the item count or language changes. Sets: armor with `m_setName` is grouped by it;
  armor without one is grouped by the prefab-name words left after removing piece words ("Armor", "Chest", "Legs", "Helmet"...),
  needing two or more different slots. Vanilla Bronze, Iron and Leather have no set name, so they rely on the guess.
- **Fitting room:** instantiates the `Player` prefab with `ZNetView.m_forceDisableInit = true` (the game's own character-select
  does this), at y = 9000 so nothing else sees it, removes it from `Character.s_characters` and `Player.s_players`, and renders it
  with its own camera into a `RenderTexture` shown by a `RawImage`. Three point lights light it. Items go on with
  `VisEquipment.Set*Item(hash)`; with no ZDO, `VisEquipment` reads its own fields. The look (model, skin, hair, beard) is read
  from the local player's ZDO.
- **Costs:** `ObjectDB.GetRecipe(itemData)` and `Piece.Requirement.GetAmount(level)` summed over levels 1..quality. "Have" counts
  the player's inventory plus containers within Chest Radius (refreshed every second while open).
- **Window:** built only with `MagicMike.ModSettings.Theme` and `Widgets` from `Shared` (the user wants the Mod Settings
  style), on `GUIManager.CustomGUIFront`. Input is blocked with `GUIManager.BlockInput`.

- **Preview aspect:** the render texture was a fixed portrait 640x880 shown in a nearly square frame, which stretched the model sideways. `FittingRoom.Open(aspect)` now
  makes the texture as wide as the frame is (height 880, width = 880 x frame width / frame height, 320 to 1760), read from the frame each time the window opens.
  The user looked at it and said it looked good; I did not get my own screenshot of it.
- **Role and source filters:** two dropdowns under the search box (the list starts 34 px lower for them). `Catalog` gives every `Entry` and
  `SetEntry` a `Role` (flags Mage, Tank, Damage, Archer; an item can have several) and a `Source` string.
  Roles: shields are Tank; a weapon takes its skill (Bows and Crossbows Archer, ElementalMagic and BloodMagic Mage, the melee skills Damage; tools
  and bombs none); armor is Mage with eitr regeneration, Tank if its movement modifier is -0.04 or less or armor is 36+, plus the skills its
  equip or set status effect raises (Sneak counts as Archer), plus Damage for effects that cut attack stamina or raise stamina regeneration.
  A set has a role when at least half its pieces do. These rules came from a dump of this game's items (364), so they fit vanilla well and
  modded armor only as far as its stats say.
  Source: the game's own asset bundles have 8-hex-digit names, and nearly every item lives in one (`c4210710`), so that is "Valheim". An item in
  a named bundle is matched to a BepInEx plugin by squashing both names (`corewood_fortified` is CorewoodFortified). An item in no loaded
  bundle (a mod's bundle unloaded after use, or code-made) falls back to Jotunn's `ItemManager.GetItem(..).SourceMod`, then to "Other mods".
  In this install that is the Nova Chitinarmor set and the Crimson mantle. The dropdown is refilled each time the window opens.

- **Biomes:** `Biomes.cs`. Biome order (Meadows, Black Forest, Swamp, Ocean, Mountain, Plains, Mistlands, Ashlands, Deep North) is `Biomes.Order`;
  an `Entry` or `SetEntry` has `Tier` as an index into it, -1 for unknown. Nothing is hard-coded per item. Where each material is found
  is read from the world: `ZoneSystem.m_vegetation` (trees, rocks, pickables, anything that spawns another prefab when destroyed, via
  `Destructible.m_spawnWhenDestroyed`), the spawn lists on `ZoneSystem.m_zoneCtrlPrefab`'s `SpawnSystem` and what those creatures drop, and
  locations and dungeon rooms (loot, creature spawners, boss altars). Smelter and Fermenter conversions and recipes come from `ZNetScene` and
  `ObjectDB`. A material's tier is the earliest of: found, made from something, crafted. Gear takes the furthest ingredient of its easiest
  recipe; gear with no recipe takes where it is found, or the plain item it is a variant of (`FW_`, `SP_` prefix). A set takes its furthest piece.
  Vegetation and spawns are read at once (a few ms). Locations (232) and dungeon rooms are slow (about 21 s if loaded synchronously, which
  froze the game), so `Biomes.Tick` starts a coroutine once in a world that loads them one at a time with `SoftReference.LoadAsync`, then
  releases them; it takes about 110 s the first time. The result is saved in `BepInEx/config/VanityWardrobe/biomes.cache` with a key of the game
  version, item, location and vegetation counts and the mod list; with a matching file it is read at once. When the scan finishes
  `Catalog.Retier()` runs and the open window redraws (`Biomes.Revision`). Biome names are the game's own (`$biome_meadows` ...), so they are
  translated for free. The third filter row is a biome dropdown plus an "and earlier" button; "Unknown" lists what could not be placed.
  Checked results: Bronze Black Forest, Iron Swamp, Silver Mountain, Black metal Plains, Mistlands staves, Deep North sets Deep North.
  Only the Harvester set (event items with no recipe) is unknown. Items placed late in a chest but earlier by recipe use the recipe.

- **Recolor:** 11 presets (red, orange, yellow, green, teal, blue, purple, pink, black, white, grey) plus the original, as a row of swatches in the
  details column that acts on the item, the set, or the whole outfit shown. `Colors` keeps item prefab name to preset in
  `BepInEx/config/VanityWardrobe/colors.txt` (`Prefab|index`, 1 to 11) and bumps `Colors.Revision` on a change. `Recolor` finds an item's main colour from
  its `_MainTex`: the hue with most weight among the pixels with saturation over 0.25 and value over 0.15; pixels within about 25 degrees of it
  (fading out by 58) are moved to the preset (black, white and grey lower the saturation and value instead), everything else is left alone, and
  an item with no colourful pixels (grey iron) is coloured over all. The game's textures are unreadable, so each is drawn into a render texture
  and read back, at most 1024 px, and the copy is DXT compressed and cached per (texture, preset) (up to 120). Harmony postfixes on
  `VisEquipment.AttachArmor` and `AttachItem` colour items as the game puts them on a model, and one on `UpdateEquipmentVisuals` colours a model
  again when its colours change (`Colors.For` gives a stamp). The fitting-room model and the local player use the player's own choices. Every
  half second `Colors.Publish` writes the colours of the worn items into the local player's ZDO (key `VanityWardrobe.colors`, text
  `Item=index;Item=index`), and other players' models read that, so players with the mod see each other's colours; players without it see the
  originals. Not recoloured: items on the ground or on armor stands, particles; unusual shaders may change only partly.
  Checked in game: all of red, blue, black, white, green, teal on the Bronze set; the original coming back with the x swatch; the choice saved in
  the file and applied after a restart; the torch in BallBag's hand turning blue and `Torch=6` appearing in the ZDO, then empty again on
  unequip. Not checked: another player seeing it (needs two clients), and the other languages' colour names.
  Note: the gameplay keys (hotbar, Tab) do not reach the game through `gameinput.ps1`; only keys that a mod reads with `Input` (F9, Esc) do. To
  test with worn gear, add a temporary key in the plugin that calls `Player.EquipItem`.

- **Poses and list:** `FittingRoom.Apply` sets the preview's animator parameters `statef` (float) and `statei` (int) to the item's
  `m_animationState`, the way `Humanoid.SetupAnimationState` does: the left item decides first (a shield), then the right, else Unarmed. So a spear,
  knife, two-handed axe and staff are held in their own stances. Checked in game; a bow and a weapon with a shield were not looked at closely.
  The item list is virtual: `RefreshList` only fills `_items`, and `ShowRows` binds a pool of 24 reused rows (`RowView`) to the ones in view, placed by
  hand (the content's layout group and size fitter are turned off), so there is no row cap and a long list costs no more than a short one. Selection
  and hover are drawn by `ColourRow` for the rows currently bound. The game reports a wheel notch as a small unpredictable amount (0.05 here), so
  the lists use `StepScroll` (3 rows, or 90 px for the details, per notch in the wheel's direction) with the ScrollRect's own sensitivity at 0.

- **Appearance panel:** the window is 890 px tall (`MaxHeight`) and an `AppearancePanel` hangs off its top right corner (a child of the window, so it
  moves with it; the pair is centred together and the window narrows to leave room). It edits the player's own character at once, like
  Einherjar Bodies' own window, with no Done or Cancel: the vanilla part uses `Player.SetPlayerModel`, `SetSkinColor`, `SetHairColor`, `SetHair`, `SetBeard`
  (so it works without any other mod); Eye Glow, Iris and Underwear only appear when `EinherjarLink.Available`, and go through
  `EinherjarLookApi` (`GetField`, `SetField`, `ApplyLook`, `UnderwearPart`, `SetUnderwearPart`, `ApplyUnderwear`; the stored look is read from
  `player.m_customData["einherjar_eyes_v1"]` and `["einherjar_underwear_v1"]`). After each edit `FittingRoom.RefreshLook` copies the look to the
  preview, and `EinherjarLookApi.UseLookOf(previewVis, localVis)` makes the preview show the player's eyes and underwear. The game's skin and hair
  colour ranges come from `PlayerCustomizaton.OnEnable` (a Harmony postfix, only fires at the main menu), with Einherjar's usual values until then. The
  chevron arrows are drawn icons, because `<` and `>` in a 24 px text button did not show. The labels reuse EinherjarBodies' translations
  (`vanitywardrobe_ap_*`).
  Checked in game: the layout against the mock, next hair style, Female and Male (the beard row appears and goes), the underwear colour showing on
  the preview, the panel filling the window's height, and no Einherjar Bodies installed (only Character, Skin, Hair, Beard and Hair Color show).
  Not checked: the eye and underwear sliders by dragging, the hair colour picker by dragging, skin tone, and what Done-less live editing does when a
  player closes the window mid-way (it simply keeps the change).

## Building and testing

- The real build is **Visual Studio** (`VanityWardrobe.sln`). `dotnet build` does not work on these old-style projects (it does not
  resolve JotunnLib). MSBuild is at `D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`; from
  `VanityWardrobe/` run `MSBuild.exe VanityWardrobe.csproj -restore -p:Configuration=Release`. It deploys the DLL and translations to the game,
  runs UpdateManifest.ps1 and UpdateNexusReadme.ps1, and writes `VanityWardrobe/ThunderStore/VanityWardrobe_v<version>.zip`.
  Run on 2026-11-20: it works (one harmless warning: `_chestKey` is assigned but never used).
- `tools/check.sh` compiles with Roslyn against the game's DLLs into `tools/out/` and deploys nothing. No error lines means it compiled.
  It compiles against the *publicized* `assembly_valheim`, so it cannot catch private-access problems that only fail at run time.
- `tools/restart.sh` closes the game (it locks the DLL), copies the check build and the translations into the game folder, relaunches
  through Steam and clicks Start on the world list. It takes about 100 s. The game is at `D:\SteamLibrary\steamapps\common\Valheim`.
- `tools/gameinput.ps1` drives the game window: `-Action shot -Out file.png`, `click X Y`, `key VK`, `move`, `scroll`. Coordinates
  are relative to the game window, as in a screenshot. Press F9 with `key 120`, Esc with `key 27`. Read the PNG to see the screen.
  `move` does not register with the game, so **click** where the pointer should be before `scroll`. Do not type text this way (typing into the outfit name field after clicking it is fine). A run entered the "dgdgdgd" world as a female character.
- Check `D:\SteamLibrary\steamapps\common\Valheim\BepInEx\LogOutput.log` for `Exception` and `[Error` after a test.
- Python with UnityPy is in a scratch venv at most; `ilspycmd` (to read the game's code) was installed as a dotnet tool in a scratch folder. Both are
  gone if the scratch folder was cleared; reinstall them if needed (`dotnet tool install ilspycmd --tool-path <dir>`).

## Gotchas learned

- At run time the game's assemblies are not publicized, so reading a private member (`Character.s_characters`,
  `Player.s_players`, `Container.m_nview`) throws `FieldAccessException` unless `Properties/IgnoreAccessModifiers.cs` (a
  `SkipVerification` attribute, copied from EinherjarBodies) is in the project. Keep it.
- **The game's TMP font has no `+` or `-` glyph at least at some sizes**: buttons using them showed blank. Use `<` `>`, or draw an icon.
- Valheim renders deferred, so light culling masks are unreliable: the fitting room is far from the world and uses point lights
  with a range, not a directional light.
- `Character` and `Player` add themselves to static lists in `Awake`, and `ZNetView.m_forceDisableInit` does not stop that.
- Characters in this user's game use the **EinherjarBodies** mod's bodies, so the model also exercises that mod. The model
  faces the camera at yaw 330 (160 showed its back).
- The user's saves are real: entering a world to test is fine, but do not change or delete anything in it.
  The game saves the character now and then, and the appearance panel changes it: before testing it, copy the character's `.fch` and `.fch.old`
  from `AppData/LocalLow/IronGate/Valheim/characters_local`, and put them back after (compare md5) so the look is as the user left it.
- BepInEx loads every DLL under `plugins/` however deep, so to test without a mod move its folder out of `plugins/` (renaming the folder is not enough),
  and move it back afterwards.
- The game window may now be borderless 1920x1080 at (0,0) with no title bar, so screenshot coordinates differ from the older ones in this file;
  take a screenshot and read the positions. The user may also close the game while testing.

## How this user wants to work

- Verify before building: use the game, clips and screenshots first. Do not guess and rebuild repeatedly.
- Ask before removing a feature. Build every step of a method up front, and check it in the game before saying it is fixed.
- Do not commit unless asked. Nothing is committed yet (the submodule is staged).
- Say plainly what was and was not verified.

## Status

Working, checked in game: opening and closing, the list with search, sets and pieces, the model wearing sets and weapons, the Item
and Outfit views with stats and costs, have/need counts, the quality buttons, Esc to close.

Also checked (2026-11-20): saving, loading and deleting an outfit. Typing works with `key` and the VK codes of the letters once the
name field is clicked; the field shows lowercase. Saved to BepInEx/config/VanityWardrobe/outfits.txt (the test entry was deleted again).

Also checked (2026-11-20): the weapon-type dropdown (Bows filter lists only bows) and mouse-wheel zoom, after a fix: the game reports
only 0.05 per wheel notch, so `PreviewInput.OnScroll` now steps by a fixed 0.3 in the wheel's direction.

Also checked (2026-11-20): the Russian text. Every label, tab, stat and recipe line is in Russian and the Cyrillic glyphs render. Set names
("Bronze", "Ashlands Medium Armor") stay English because they come from `m_setName` or prefab names, not the translation files. To test a
language, close the game, set the registry value `HKCU\Software\IronGate\Valheim` `language_h1730749655` (binary, UTF-8 name plus a 0 byte;
English is `45 6E 67 6C 69 73 68 00`) and launch; put English back afterwards. Esc did not open the pause menu with `key 27`.

Also checked (2026-11-20): a male character (BallBag, a fresh character in the "dgdgdgd" world). The male body, bald head and beard show, Bronze
armor and a knife fit, and "recipe not known yet" with 0 counts is right for a character with nothing. Characters: Gurty (ddd, female, the
usual one), BallBag and Nob (both male). To pick one, launch Valheim by hand (`cmd //c start "" steam://rungameid/892970`, about 50 s to the
main menu), click Start Game (968,760), click the name, Continue (234,710), then Start (587,796). `restart.sh` always uses the last character.
Entering the world as a character that has never been in it is its first spawn and saves into that character's file; a copy of BallBag's
file from before is in the scratchpad.

Also checked (2026-11-20): the role and source filters on Sets, Capes and Weapons (Mage, Tank, Damage, Archer give sensible lists; Valheim and
Other mods split as expected). Set grouping fix (checked in game): `Catalog.Words` now also splits a piece word glued to the end of a name (`Magelegs`), splits an
upper-case run from the next word (`DNMage`) and expands `DN` to Deep North, so Deep North Heavy and Deep North Mage form with 3 pieces each.
Guessed set names are title-cased (`FW Bronze`). Other abbreviations a mod uses would need an entry in `Catalog.Abbreviations`.

Also checked (2026-11-20): the biome filter (dropdown, "and earlier", the biome line in the details, the cache read at start, the first scan with no
cache). Not checked: the "Unknown" entry and the "still working out the biomes" note by eye, other languages' biome names.

Also checked (2026-11-20): the Release build and Thunderstore zip (DLL, nine translation files, README, CHANGELOG, manifest, icon). The zip was
not installed through r2modman or Gale. The icon is still a placeholder.

Not built yet, from the reference mod's feature list:
1. ~~Recolor~~ done, see Recolor below.
2. ~~Biomes~~ done, see Biomes below.
3. ~~Role filter and source-mod filter~~ done, see Role and source below.
4. ~~Translations~~ done: all nine languages exist (keys and `$1` placeholders checked against English). The seven new ones are machine-written, not checked in game or by native speakers.
5. ~~Weapon poses and the 400-row cap~~ done, see Poses and list below. The icon is still a placeholder.
6. Add VanityWardrobe to the "Plugins Created by Magic Mike" lists in the other mods' READMEs, if the user wants.
