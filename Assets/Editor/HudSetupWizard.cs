// PUT THIS FILE INSIDE A FOLDER NAMED "Editor"  (e.g. Assets/Editor/HudSetupWizard.cs)
// Open your Level2 scene, then: Tools > Dungeon Salvager > HUD Setup (Timer, Bag, Pause)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HudSetupWizard : EditorWindow
{
    PlayerController player;
    float timerMinutes = 3f;
    int slotCount = 12;
    int maxWeight = 12;
    string levelSelectScene = "LevelSelect";
    bool makeItems = true;
    bool placeTestPickups = true;
    bool patchPlayer = true;

    const string ItemFolder = "Assets/DungeonSalvager/Items";
    const string PickupFolder = "Assets/DungeonSalvager/Pickups";

    static readonly Color TXT = new Color(0.886f, 0.91f, 0.933f);
    static readonly Color MUTED = new Color(0.59f, 0.635f, 0.69f);
    static readonly Color TEAL = new Color(0.604f, 0.847f, 0.816f);

    string spriteFolder;
    [Range(0.4f, 1f)] float panelOpacity = 0.92f;
    static readonly Color Fallback = new Color(0.08f, 0.1f, 0.14f, 0.8f);
    TMP_FontAsset titleFont, bodyFont;

    [MenuItem("Tools/Dungeon Salvager/HUD Setup (Timer, Bag, Pause)")]
    static void Open() => GetWindow<HudSetupWizard>("HUD Setup");

    void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    // ================================================================== window
    Vector2 scroll;

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawWindow();
        EditorGUILayout.EndScrollView();
    }

    void DrawWindow()
    {
        EditorGUILayout.LabelField("Quick fixes", EditorStyles.boldLabel);
        if (GUILayout.Button("Recreate Items + Pickup Prefabs", GUILayout.Height(30))) EditorApplication.delayCall += RecreatePickups;
        if (GUILayout.Button("Repair HUD (fix white boxes)", GUILayout.Height(30))) EditorApplication.delayCall += Repair;
        if (GUILayout.Button("Add Extraction Portal (win condition)", GUILayout.Height(30))) EditorApplication.delayCall += AddPortal;
        EditorGUILayout.Space(12);

        EditorGUILayout.LabelField("Build the HUD", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Open scene: " + SceneManager.GetActiveScene().name);
        if (SceneManager.GetActiveScene().name != "Level2")
            EditorGUILayout.HelpBox("You usually want to run this in your Level2 scene.", MessageType.Warning);

        EditorGUILayout.Space();
        player = (PlayerController)EditorGUILayout.ObjectField("Player (HeroKnight)", player, typeof(PlayerController), true);
        timerMinutes = Mathf.Max(0.25f, EditorGUILayout.FloatField("Collapse timer (minutes)", timerMinutes));
        slotCount = Mathf.Clamp(EditorGUILayout.IntField("Bag slots", slotCount), 4, 16);
        maxWeight = Mathf.Max(1, EditorGUILayout.IntField("Bag weight limit", maxWeight));
        levelSelectScene = EditorGUILayout.TextField("Level Select scene name", levelSelectScene);

        EditorGUILayout.Space();
        makeItems = EditorGUILayout.ToggleLeft("Create items (20 fruits, 3 scrap, 2 tonics) + mix recipes + pickup prefabs", makeItems);
        GUI.enabled = makeItems;
        placeTestPickups = EditorGUILayout.ToggleLeft("Place a few test pickups next to the player", placeTestPickups);
        GUI.enabled = true;
        patchPlayer = EditorGUILayout.ToggleLeft("Fix PlayerController: no attacking when clicking buttons or while paused (makes a backup)", patchPlayer);
        panelOpacity = EditorGUILayout.Slider(new GUIContent("Panel opacity", "Lower = more see-through panels, slots and buttons"), panelOpacity, 0.4f, 1f);

        EditorGUILayout.Space();
        if (player == null)
            EditorGUILayout.HelpBox("Drag your player (the object with PlayerController) into the Player field.", MessageType.Error);
        GUI.enabled = player != null;
        if (GUILayout.Button("Build HUD", GUILayout.Height(38))) EditorApplication.delayCall += Build;
        GUI.enabled = true;

    }

    // ================================================================== build
    void Build()
    {
        if (ScriptsInEditorFolder()) return;
        missing.Clear();
        spriteFolder = FindSpriteFolder();
        if (spriteFolder == null)
        {
            EditorUtility.DisplayDialog("Sprites not found", "Couldn't find this pack's sprites (icon_pause.png). Drag the Sprites folder from the zip into your Assets first.", "OK");
            return;
        }

        var existing = GameObject.Find("GameHUD");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Rebuild HUD?", "This scene already has a GameHUD. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        ImportSprites();
        FindFonts();

        // ---- the bag on the player
        var inv = player.GetComponent<Inventory>();
        if (inv == null) inv = Undo.AddComponent<Inventory>(player.gameObject);
        inv.player = player;
        inv.slotCount = slotCount;
        inv.maxWeight = maxWeight;

        // ---- items, recipes, pickups
        if (makeItems)
        {
            var items = CreateItems();
            CreateRecipes(inv, items);
            CreatePickupPrefabs(items);
            if (placeTestPickups) PlaceTestPickups(items);
        }
        EditorUtility.SetDirty(inv);

        // ---- canvas
        EnsureEventSystem();
        var canvasGO = new GameObject("GameHUD", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasGO, "Build HUD");
        canvasGO.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)canvasGO.transform;

        var hearts = BuildHearts(root);
        var timer = BuildTimer(root);
        var pauseBtnIcon = BuildPauseButton(root, out Button pauseButton);
        var ui = BuildBag(root, inv);
        var pause = BuildPauseMenu(root, canvasGO, pauseBtnIcon, ui);

        UnityEventTools.AddPersistentListener(pauseButton.onClick, new UnityAction(pause.TogglePause));
        UnityEventTools.AddPersistentListener(timer.onTimeUp, new UnityAction(pause.ShowTimeUp));

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = canvasGO;

        ApplyLook(canvasGO);
        bool patched = patchPlayer && PatchPlayerController();
        if (missing.Count > 0)
            Debug.LogWarning("HUD Setup: couldn't find these sprites: " + string.Join(", ", missing) + ". Make sure the zip's Sprites folder is in Assets.");

        EditorUtility.DisplayDialog("HUD built!",
            "Added GameHUD (hearts, timer, pause button, bag, pause menu) and put an Inventory on " + player.name + ".\n\n" +
            (makeItems ? "Items are in " + ItemFolder + ", pickup prefabs in " + PickupFolder + ". Drag pickups into your level.\n\n" : "") +
            (patched ? "PlayerController was updated (backup saved in the Backups folder next to Assets).\n\n" : "") +
            "Save the scene (Ctrl+S) and press Play.\nTAB = bag, ESC or P = pause.", "OK");
    }

    // ================================================================== HUD pieces
    HeartsUI BuildHearts(RectTransform root)
    {
        var go = NewRect("Hearts", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(240, 64));
        var hl = go.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8; hl.childControlWidth = hl.childControlHeight = false; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var h = go.gameObject.AddComponent<HeartsUI>();
        h.player = player; h.fullHeart = S("icon_heart"); h.emptyHeart = S("icon_heart_empty");
        int n = Mathf.Max(1, player.maxHealth);
        h.hearts = new Image[n];
        for (int i = 0; i < n; i++)
        {
            var img = Img("Heart" + (i + 1), go, h.fullHeart);
            img.rectTransform.sizeDelta = new Vector2(64, 64);
            h.hearts[i] = img;
        }
        return h;
    }

    GameTimer BuildTimer(RectTransform root)
    {
        var panel = Img("Timer", root, S("ui_panel"), sliced: true);
        Place(panel.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(320, 116));

        var glass = Img("Hourglass", panel.rectTransform, S("icon_hourglass"));
        TopLeft(glass.rectTransform, 26, 22, 64, 64);

        var time = Txt("TimeText", panel.rectTransform, "3:00", 56, TXT, TextAlignmentOptions.Center, titleFont);
        TopLeft(time.rectTransform, 100, 10, 200, 70);

        var back = Img("SandBack", panel.rectTransform, S("ui_bar_back"), sliced: true);
        TopLeft(back.rectTransform, 26, 80, 268, 24);
        var fill = Img("SandFill", back.rectTransform, S("ui_white"));
        Stretch(fill.rectTransform, 6);
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        fill.color = new Color(0.94f, 0.77f, 0.3f);

        var t = panel.gameObject.AddComponent<GameTimer>();
        t.startTime = timerMinutes * 60f;
        t.timeText = time;
        t.sandBar = fill;
        return t;
    }

    Image BuildPauseButton(RectTransform root, out Button button)
    {
        var bg = Img("PauseButton", root, S("ui_slot"), sliced: true, raycast: true);
        Place(bg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -36), new Vector2(84, 84));
        button = bg.gameObject.AddComponent<Button>();
        Tint(button);
        var icon = Img("Icon", bg.rectTransform, S("icon_pause"));
        Center(icon.rectTransform, 52, 52);
        var hint = Txt("KeyHint", bg.rectTransform, "ESC", 16, MUTED, TextAlignmentOptions.Center, bodyFont);
        Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(84, 24));
        return icon;
    }

    InventoryUI BuildBag(RectTransform root, Inventory inv)
    {
        var ui = root.gameObject.AddComponent<InventoryUI>();
        ui.inventory = inv;
        ui.slotNormal = S("ui_slot"); ui.slotSelected = S("ui_slot_selected"); ui.slotDisabled = S("ui_slot_disabled");
        ui.bagClosedSprite = S("icon_bag"); ui.bagOpenSprite = S("icon_bag_open");

        // ---- bag button
        var bb = Img("BagButton", root, S("ui_slot"), sliced: true, raycast: true);
        Place(bb.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 56), new Vector2(116, 116));
        var bagBtn = bb.gameObject.AddComponent<Button>(); Tint(bagBtn);
        UnityEventTools.AddPersistentListener(bagBtn.onClick, new UnityAction(ui.ToggleBag));
        ui.bagButtonIcon = Img("BagIcon", bb.rectTransform, S("icon_bag"));
        Center(ui.bagButtonIcon.rectTransform, 80, 80);
        var badge = Img("Badge", bb.rectTransform, S("ui_badge"));
        Place(badge.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-12, -12), new Vector2(38, 38));
        ui.countText = Txt("CountText", badge.rectTransform, "0", 20, new Color(0.16f, 0.1f, 0.02f), TextAlignmentOptions.Center, bodyFont);
        Stretch(ui.countText.rectTransform, 0);
        ui.countText.fontStyle = FontStyles.Bold;
        var hint = Txt("KeyHint", bb.rectTransform, "TAB", 18, MUTED, TextAlignmentOptions.Center, bodyFont);
        Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(116, 26));

        // ---- panel
        int cols = 4, rows = Mathf.CeilToInt(slotCount / (float)cols);
        float gridH = rows * 80 + (rows - 1) * 10;
        float panelH = 110 + gridH + 20 + 96 + 26;
        var panel = Img("BagPanel", root, S("ui_panel"), sliced: true, raycast: true);
        Place(panel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 194), new Vector2(402, panelH));
        ui.bagPanel = panel.gameObject;
        var pr = panel.rectTransform;

        ui.titleText = Txt("Title", pr, "BAG", 32, TEAL, TextAlignmentOptions.Left, titleFont);
        TopLeft(ui.titleText.rectTransform, 26, 16, 260, 44);

        var close = Img("CloseButton", pr, S("ui_close"), sliced: true, raycast: true);
        Place(close.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -18), new Vector2(44, 44));
        var closeBtn = close.gameObject.AddComponent<Button>(); Tint(closeBtn);
        UnityEventTools.AddPersistentListener(closeBtn.onClick, new UnityAction(ui.CloseBag));

        var wl = Txt("WeightLabel", pr, "WEIGHT", 16, MUTED, TextAlignmentOptions.Left, bodyFont);
        TopLeft(wl.rectTransform, 26, 72, 90, 28); wl.fontStyle = FontStyles.Bold;
        var wb = Img("WeightBack", pr, S("ui_bar_back"), sliced: true);
        TopLeft(wb.rectTransform, 116, 70, 186, 30);
        ui.weightFill = Img("WeightFill", wb.rectTransform, S("ui_white"));
        Stretch(ui.weightFill.rectTransform, 6);
        ui.weightFill.type = Image.Type.Filled; ui.weightFill.fillMethod = Image.FillMethod.Horizontal;
        ui.weightText = Txt("WeightText", pr, "0/12", 20, TXT, TextAlignmentOptions.Right, bodyFont);
        Place(ui.weightText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -68), new Vector2(84, 32));
        ui.weightText.fontStyle = FontStyles.Bold;

        var grid = NewRect("SlotGrid", pr, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -110), new Vector2(350, gridH));
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(80, 80); gl.spacing = new Vector2(10, 10);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = cols;
        ui.slotGrid = grid;
        var slots = new List<InventorySlotUI>();
        for (int i = 0; i < slotCount; i++)
        {
            var s = Img("Slot" + (i + 1), grid, S("ui_slot"), sliced: true, raycast: true);
            var b = s.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.ColorTint;
            var c = b.colors; c.highlightedColor = new Color(1.15f, 1.15f, 1.15f); c.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            c.disabledColor = Color.white; c.selectedColor = Color.white; b.colors = c;
            var icon = Img("Icon", s.rectTransform, null);
            Center(icon.rectTransform, 60, 60);
            icon.color = Color.white; icon.enabled = false;
            var slot = s.gameObject.AddComponent<InventorySlotUI>();
            slot.icon = icon;
            slots.Add(slot);
        }
        ui.slots = slots.ToArray();

        var info = Img("InfoStrip", pr, S("ui_panel_small"), sliced: true);
        TopLeft(info.rectTransform, 26, 110 + gridH + 20, 350, 96);
        ui.infoIcon = Img("InfoIcon", info.rectTransform, null);
        TopLeft(ui.infoIcon.rectTransform, 16, 16, 64, 64);
        ui.infoIcon.color = Color.white; ui.infoIcon.enabled = false;
        ui.infoName = Txt("InfoName", info.rectTransform, "", 24, TXT, TextAlignmentOptions.Left, bodyFont);
        TopLeft(ui.infoName.rectTransform, 94, 12, 244, 36); ui.infoName.fontStyle = FontStyles.Bold;
        ui.infoDescription = Txt("InfoDescription", info.rectTransform, "Click an item", 16, MUTED, TextAlignmentOptions.Left, bodyFont);
        TopLeft(ui.infoDescription.rectTransform, 94, 48, 244, 40);
        ui.infoDescription.textWrappingMode = TextWrappingModes.Normal;

        // ---- action menu (kept separate so it can sit outside the panel)
        var menu = Img("ActionMenu", root, S("ui_panel"), sliced: true, raycast: true);
        Place(menu.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(200, 236));
        ui.actionMenu = menu.rectTransform;
        ui.actionTitle = Txt("ActionTitle", menu.rectTransform, "Item", 20, TEAL, TextAlignmentOptions.Center, bodyFont);
        TopLeft(ui.actionTitle.rectTransform, 10, 14, 180, 36); ui.actionTitle.fontStyle = FontStyles.Bold;
        ui.eatButton = MenuButton("EatButton", menu.rectTransform, "EAT", 24, 58, 152, 48);
        ui.mixButton = MenuButton("MixButton", menu.rectTransform, "MIX", 24, 112, 152, 48);
        ui.dropButton = MenuButton("DropButton", menu.rectTransform, "DROP", 24, 166, 152, 48);

        EditorUtility.SetDirty(ui);
        return ui;
    }

    PauseMenu BuildPauseMenu(RectTransform root, GameObject canvasGO, Image pauseIcon, InventoryUI ui)
    {
        var pm = canvasGO.AddComponent<PauseMenu>();
        pm.levelSelectScene = levelSelectScene;
        pm.pauseButtonIcon = pauseIcon;
        pm.pauseIcon = S("icon_pause"); pm.playIcon = S("icon_play");
        pm.inventoryUI = ui;

        var overlay = Img("PauseOverlay", root, S("ui_white"), raycast: true);
        overlay.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        Stretch(overlay.rectTransform, 0);
        pm.pauseOverlay = overlay.gameObject;

        var panel = Img("PausePanel", overlay.rectTransform, S("ui_panel"), sliced: true, raycast: true);
        Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 470));
        pm.titleText = Txt("Title", panel.rectTransform, "PAUSED", 52, TEAL, TextAlignmentOptions.Center, titleFont);
        TopLeft(pm.titleText.rectTransform, 20, 22, 400, 110);
        pm.bodyText = MakeBody(panel.rectTransform);
        pm.player = player;
        pm.inventory = ui.inventory;

        var resume = MenuButton("ResumeButton", panel.rectTransform, "RESUME", 70, 140, 300, 62);
        UnityEventTools.AddPersistentListener(resume.onClick, new UnityAction(pm.Resume));
        pm.resumeButton = resume.gameObject;
        var restart = MenuButton("RestartButton", panel.rectTransform, "RESTART", 70, 214, 300, 62);
        UnityEventTools.AddPersistentListener(restart.onClick, new UnityAction(pm.Restart));
        var ls = MenuButton("LevelSelectButton", panel.rectTransform, "LEVEL SELECT", 70, 288, 300, 62);
        UnityEventTools.AddPersistentListener(ls.onClick, new UnityAction(pm.GoToLevelSelect));
        var quit = MenuButton("QuitButton", panel.rectTransform, "QUIT GAME", 70, 362, 300, 62);
        UnityEventTools.AddPersistentListener(quit.onClick, new UnityAction(pm.QuitGame));

        overlay.gameObject.SetActive(false);
        EditorUtility.SetDirty(pm);
        return pm;
    }

    TextMeshProUGUI MakeBody(RectTransform panel)
    {
        var body = Txt("Body", panel, "", 21, TXT, TextAlignmentOptions.Center, bodyFont);
        TopLeft(body.rectTransform, 24, 132, 392, 76);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.gameObject.SetActive(false);
        return body;
    }

    Button MenuButton(string name, RectTransform parent, string label, float x, float y, float w, float h)
    {
        var bg = Img(name, parent, S("ui_button"), sliced: true, raycast: true);
        TopLeft(bg.rectTransform, x, y, w, h);
        var b = bg.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.SpriteSwap;
        b.spriteState = new SpriteState
        {
            highlightedSprite = S("ui_button_hover"), selectedSprite = S("ui_button"),
            pressedSprite = S("ui_button_pressed"), disabledSprite = S("ui_button_disabled")
        };
        var t = Txt("Label", bg.rectTransform, label, h > 55 ? 28 : 24, TXT, TextAlignmentOptions.Center, titleFont);
        Stretch(t.rectTransform, 0);
        t.rectTransform.offsetMin = new Vector2(0, 4);
        return b;
    }

    // ================================================================== items
    class Def { public string file, name, desc; public bool eat; public int heal, value, weight; }

    Dictionary<string, ItemData> CreateItems()
    {
        EnsureFolder(ItemFolder);
        var defs = new List<Def>();
        string[] fruits = { "red_apple", "green_apple", "pear", "orange", "lemon", "lime", "banana", "cherries", "strawberry", "grapes",
                            "watermelon", "peach", "plum", "pineapple", "mango", "kiwi", "coconut", "blueberries", "pomegranate", "avocado" };
        foreach (var f in fruits)
            defs.Add(new Def { file = "fruit_" + f, name = Nice(f), desc = "Restores 1 heart", eat = true, heal = 1, value = 2, weight = 1 });
        defs.Add(new Def { file = "item_scrap_gear", name = "Brass Gear", desc = "Scrap worth 10", value = 10, weight = 1 });
        defs.Add(new Def { file = "item_scrap_plate", name = "Iron Plate", desc = "Scrap worth 15", value = 15, weight = 3 });
        defs.Add(new Def { file = "item_scrap_core", name = "Power Core", desc = "Scrap worth 40", value = 40, weight = 2 });
        defs.Add(new Def { file = "item_fruit_tonic", name = "Fruit Tonic", desc = "Restores 2 hearts", eat = true, heal = 2, value = 5, weight = 1 });
        defs.Add(new Def { file = "item_energy_tonic", name = "Energy Tonic", desc = "Restores all hearts", eat = true, heal = 99, value = 20, weight = 1 });

        var result = new Dictionary<string, ItemData>();
        foreach (var d in defs)
        {
            string path = $"{ItemFolder}/{d.name.Replace(" ", "")}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null && AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path); // broken leftover
            if (item == null)   // never overwrite items you've already edited
            {
                item = CreateInstance<ItemData>();
                item.itemName = d.name; item.description = d.desc; item.canEat = d.eat;
                item.healAmount = d.heal; item.value = d.value; item.weight = d.weight;
                item.icon = S(d.file);
                AssetDatabase.CreateAsset(item, path);
            }
            result[d.name] = item;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    void CreateRecipes(Inventory inv, Dictionary<string, ItemData> it)
    {
        if (inv.recipes != null && inv.recipes.Count > 0) return;   // keep your own recipes
        void R(string a, string b, string r)
        {
            if (it.ContainsKey(a) && it.ContainsKey(b) && it.ContainsKey(r))
                inv.recipes.Add(new Inventory.MixRecipe { itemA = it[a], itemB = it[b], result = it[r] });
        }
        R("Red Apple", "Strawberry", "Fruit Tonic");
        R("Banana", "Grapes", "Fruit Tonic");
        R("Orange", "Lemon", "Fruit Tonic");
        R("Watermelon", "Mango", "Fruit Tonic");
        R("Power Core", "Red Apple", "Energy Tonic");
        R("Power Core", "Banana", "Energy Tonic");
        R("Brass Gear", "Fruit Tonic", "Energy Tonic");
    }

    void CreatePickupPrefabs(Dictionary<string, ItemData> items)
    {
        EnsureFolder(PickupFolder);
        var psr = player != null ? player.GetComponent<SpriteRenderer>() : null;
        foreach (var kv in items)
        {
            var item = kv.Value;
            string path = $"{PickupFolder}/{item.itemName.Replace(" ", "")}Pickup.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null)
            {
                var go = new GameObject(item.itemName.Replace(" ", "") + "Pickup");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = item.icon;
                if (psr != null)
                {
                    sr.sortingLayerID = psr.sortingLayerID;
                    sr.sortingOrder = psr.sortingOrder - 1;
                    if (psr.sharedMaterial != null) sr.sharedMaterial = psr.sharedMaterial;
                }
                var col = go.AddComponent<CircleCollider2D>();
                col.isTrigger = true; col.radius = 0.45f;
                go.AddComponent<ItemPickup>().item = item;
                existing = PrefabUtility.SaveAsPrefabAsset(go, path);
                DestroyImmediate(go);
            }
            if (item.worldPrefab == null) { item.worldPrefab = existing; EditorUtility.SetDirty(item); }
        }
        AssetDatabase.SaveAssets();
    }

    void PlaceTestPickups(Dictionary<string, ItemData> items)
    {
        var parent = new GameObject("TestPickups");
        Undo.RegisterCreatedObjectUndo(parent, "Build HUD");
        var psr = player.GetComponent<SpriteRenderer>();
        float y = psr != null ? psr.bounds.min.y + 0.6f : player.transform.position.y;
        string[] names = { "Red Apple", "Strawberry", "Banana", "Grapes", "Power Core", "Brass Gear" };
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"{PickupFolder}/{names[i].Replace(" ", "")}Pickup.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.scene);
            go.transform.SetParent(parent.transform, true);
            go.transform.position = new Vector3(player.transform.position.x + 2f + i * 1.5f, y, 0f);
        }
    }

    // ================================================================== sprites & fonts
    static string FindSpriteFolder()
    {
        // icon_pause.png only exists in this pack, so older folders with a ui_panel.png are skipped
        foreach (var g in AssetDatabase.FindAssets("icon_pause t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string dir = Path.GetDirectoryName(p).Replace('\\', '/');
            if (Path.GetFileNameWithoutExtension(p) == "icon_pause" && File.Exists($"{dir}/ui_slot.png")) return dir;
        }
        return null;
    }

    void ImportSprites()
    {
        var borders = new Dictionary<string, int>
        {
            { "ui_panel", 6 }, { "ui_panel_small", 4 }, { "ui_slot", 4 }, { "ui_slot_selected", 4 }, { "ui_slot_disabled", 4 },
            { "ui_button", 4 }, { "ui_button_hover", 4 }, { "ui_button_pressed", 4 }, { "ui_button_disabled", 4 },
            { "ui_bar_back", 2 }, { "ui_close", 3 }
        };
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { spriteFolder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string n = Path.GetFileNameWithoutExtension(p);
            if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.spritePixelsPerUnit = (n.StartsWith("ui_") || n.StartsWith("icon_")) ? 25 : 16;
            if (borders.TryGetValue(n, out int b)) ti.spriteBorder = new Vector4(b, b, b, b);
            ti.SaveAndReimport();
        }
    }

    readonly HashSet<string> missing = new HashSet<string>();

    Sprite S(string name)
    {
        string path = $"{spriteFolder}/{name}.png";
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path)
                 ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (sp == null)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Sprite"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                sp = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault();
                if (sp != null) break;
            }
        }
        if (sp == null) missing.Add(name);
        return sp;
    }

    void FindFonts()
    {
        bodyFont = TMP_Settings.defaultFontAsset;
        titleFont = AssetDatabase.FindAssets("t:TMP_FontAsset")
            .Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(f => f != null && f.name.ToLower().Contains("spider")) ?? bodyFont;
    }

    // ================================================================== extraction portal
    void AddPortal()
    {
        if (ScriptsInEditorFolder()) return;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (player == null) { EditorUtility.DisplayDialog("No player", "Couldn't find your Player (the object with PlayerController).", "OK"); return; }

        missing.Clear();
        spriteFolder = FindSpriteFolder();
        if (spriteFolder == null) { EditorUtility.DisplayDialog("Sprites not found", "Drag the zip's Sprites folder into Assets first.", "OK"); return; }
        ImportSprites();
        FindFonts();

        var frames = Enumerable.Range(0, 4).Select(i => S("portal_" + i)).Where(f => f != null).ToArray();
        if (frames.Length == 0) { EditorUtility.DisplayDialog("Portal art missing", "Couldn't find portal_0.png ... portal_3.png. Put them in the same Sprites folder as icon_pause.png.", "OK"); return; }

        var old = GameObject.Find("ExtractionPortal");
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("Replace portal?", "There's already an ExtractionPortal in this scene. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(old);
        }

        // Size and layer it to match the player
        var psr = player.GetComponent<SpriteRenderer>();
        Bounds pb = psr != null && psr.sprite != null ? psr.bounds : new Bounds(player.transform.position, new Vector3(1, 1.5f, 0));
        float height = Mathf.Max(1f, pb.size.y * 1.8f);

        var portal = new GameObject("ExtractionPortal");
        Undo.RegisterCreatedObjectUndo(portal, "Add Extraction Portal");
        var sr = portal.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        if (psr != null) { sr.sortingLayerID = psr.sortingLayerID; sr.sortingOrder = psr.sortingOrder - 1; }
        var unlit = AssetDatabase.FindAssets("Sprite-Unlit-Default t:Material")
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(m => m != null);
        if (unlit != null) sr.sharedMaterial = unlit;           // glows even in a dark level
        else if (psr != null && psr.sharedMaterial != null) sr.sharedMaterial = psr.sharedMaterial;

        float scale = height / frames[0].bounds.size.y;
        portal.transform.localScale = new Vector3(scale, scale, 1f);
        portal.transform.position = new Vector3(pb.center.x + 6f, pb.min.y + height / 2f, 0f);

        var box = portal.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(frames[0].bounds.size.x * 0.5f, frames[0].bounds.size.y * 0.75f);
        box.offset = new Vector2(0f, -frames[0].bounds.size.y * 0.08f);
        var ep = portal.AddComponent<ExtractionPortal>();
        ep.frames = frames;

        // Teach the existing pause screen to show results
        var hud = GameObject.Find("GameHUD");
        bool upgraded = hud != null && UpgradeEndScreen(hud);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = portal;
        SceneView.lastActiveSceneView?.FrameSelected();

        EditorUtility.DisplayDialog("Portal added!",
            "The ExtractionPortal is next to your player for now.\n\n" +
            "1. Drag it to the END of your map (it's selected).\n2. Save the scene (Ctrl+S) and press Play.\n\n" +
            "Touch it = EXTRACTED! (you keep your loot)\nTimer runs out = DUNGEON COLLAPSED\nHealth hits 0 = YOU DIED" +
            (hud == null ? "\n\n⚠ No GameHUD in this scene: click Build HUD first, or the end screens won't show." :
             upgraded ? "" : "\n\n⚠ Couldn't find PauseOverlay/PausePanel inside GameHUD, so the result text wasn't added."), "OK");
    }

    bool UpgradeEndScreen(GameObject hud)
    {
        var pm = hud.GetComponent<PauseMenu>();
        if (pm == null) return false;
        Undo.RecordObject(pm, "Add end screen");
        pm.player = player;
        pm.inventory = player.GetComponent<Inventory>();
        if (pm.bodyText == null)
        {
            var panel = hud.transform.Find("PauseOverlay/PausePanel") as RectTransform;
            if (panel == null) return false;
            var existing = panel.Find("Body");
            pm.bodyText = existing != null ? existing.GetComponent<TextMeshProUGUI>() : MakeBody(panel);
            if (existing == null) Undo.RegisterCreatedObjectUndo(pm.bodyText.gameObject, "Add end screen");
        }
        // make sure the timer still ends the run
        var timer = hud.GetComponentInChildren<GameTimer>(true);
        if (timer != null)
        {
            bool hooked = false;
            for (int i = 0; i < timer.onTimeUp.GetPersistentEventCount(); i++)
                if (timer.onTimeUp.GetPersistentMethodName(i) == "ShowTimeUp") hooked = true;
            if (!hooked) { Undo.RecordObject(timer, "Hook timer"); UnityEventTools.AddPersistentListener(timer.onTimeUp, new UnityAction(pm.ShowTimeUp)); EditorUtility.SetDirty(timer); }
        }
        EditorUtility.SetDirty(pm);
        return true;
    }

    // ================================================================== recreate pickups
    static bool ScriptsInEditorFolder()
    {
        foreach (var name in new[] { "ItemPickup", "Inventory", "ItemData", "InventoryUI", "PauseMenu", "GameTimer" })
            foreach (var g in AssetDatabase.FindAssets(name + " t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name && p.Replace('\\', '/').Contains("/Editor/"))
                {
                    EditorUtility.DisplayDialog("Scripts in the wrong folder",
                        $"{p}\n\nThis game script is inside an 'Editor' folder, so Unity can't put it on objects.\n\n" +
                        "Delete that copy (keep the one in Assets/Scripts), or move it out of the Editor folder. Then try again.", "OK");
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(p));
                    return true;
                }
            }
        return false;
    }

    void RecreatePickups()
    {
        if (ScriptsInEditorFolder()) return;
        missing.Clear();
        spriteFolder = FindSpriteFolder();
        if (spriteFolder == null)
        {
            EditorUtility.DisplayDialog("Sprites not found", "Couldn't find this pack's sprites (icon_pause.png). Drag the zip's Sprites folder into Assets first.", "OK");
            return;
        }
        ImportSprites();
        var items = CreateItems();
        CreatePickupPrefabs(items);

        // make sure every item knows its pickup (used by DROP)
        foreach (var item in items.Values)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PickupFolder}/{item.itemName.Replace(" ", "")}Pickup.prefab");
            if (prefab != null && item.worldPrefab != prefab) { item.worldPrefab = prefab; EditorUtility.SetDirty(item); }
        }
        AssetDatabase.SaveAssets();

        foreach (var inv in FindObjectsByType<Inventory>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(inv, "Fix recipes");
            inv.recipes.RemoveAll(r => r == null || r.itemA == null || r.itemB == null || r.result == null);
            CreateRecipes(inv, items);          // only fills it in if the list is empty
            EditorUtility.SetDirty(inv);
            EditorSceneManager.MarkSceneDirty(inv.gameObject.scene);
        }

        var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(PickupFolder);
        if (folder != null) { Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); }
        EditorUtility.DisplayDialog("Done", "Pickup prefabs are back in " + PickupFolder + ".\n\nDrag them into your level (not the fruit pictures).", "OK");
    }

    // ================================================================== repair
    static readonly Dictionary<string, string> SpriteFor = new Dictionary<string, string>
    {
        { "Timer", "ui_panel" }, { "Hourglass", "icon_hourglass" }, { "SandBack", "ui_bar_back" }, { "SandFill", "ui_white" },
        { "PauseButton", "ui_slot" }, { "BagButton", "ui_slot" }, { "BagIcon", "icon_bag" }, { "Badge", "ui_badge" },
        { "BagPanel", "ui_panel" }, { "CloseButton", "ui_close" }, { "WeightBack", "ui_bar_back" }, { "WeightFill", "ui_white" },
        { "InfoStrip", "ui_panel_small" }, { "ActionMenu", "ui_panel" }, { "PauseOverlay", "ui_white" }, { "PausePanel", "ui_panel" },
        { "EatButton", "ui_button" }, { "MixButton", "ui_button" }, { "DropButton", "ui_button" },
        { "ResumeButton", "ui_button" }, { "RestartButton", "ui_button" }, { "LevelSelectButton", "ui_button" }, { "QuitButton", "ui_button" },
    };

    void Repair()
    {
        var hud = GameObject.Find("GameHUD");
        if (hud == null) { EditorUtility.DisplayDialog("No HUD", "There's no GameHUD in this scene. Click Build HUD first.", "OK"); return; }
        spriteFolder = FindSpriteFolder();
        if (spriteFolder == null)
        {
            EditorUtility.DisplayDialog("Sprites not found", "Couldn't find this pack's sprites (icon_pause.png). Drag the zip's Sprites folder into Assets first.", "OK");
            return;
        }
        missing.Clear();
        ImportSprites();

        foreach (var img in hud.GetComponentsInChildren<Image>(true))
        {
            Undo.RecordObject(img, "Repair HUD");
            string n = img.gameObject.name;
            string parent = img.transform.parent != null ? img.transform.parent.name : "";
            Sprite sp = null;
            if (n == "Icon" && parent == "PauseButton") sp = S("icon_pause");
            else if (n.StartsWith("Slot") && img.GetComponent<InventorySlotUI>() != null) sp = S("ui_slot");
            else if (n.StartsWith("Heart")) sp = S("icon_heart");
            else if (SpriteFor.TryGetValue(n, out string sn)) sp = S(sn);
            else if (n == "Icon" || n == "InfoIcon") { img.color = Color.white; if (img.sprite == null) img.enabled = false; continue; }

            if (sp != null)
            {
                img.sprite = sp;
                if (n == "PauseOverlay" || n.EndsWith("Fill")) { }                       // keep their own colour
                else if (img.color == Fallback || img.color == Color.white) img.color = Color.white;
            }
            else if (img.sprite == null) img.color = Fallback;

            var b = img.GetComponent<Button>();
            if (b != null && b.transition == Selectable.Transition.SpriteSwap)
                b.spriteState = new SpriteState { highlightedSprite = S("ui_button_hover"), selectedSprite = S("ui_button"), pressedSprite = S("ui_button_pressed"), disabledSprite = S("ui_button_disabled") };
        }

        var ui = hud.GetComponent<InventoryUI>();
        if (ui != null)
        {
            Undo.RecordObject(ui, "Repair HUD");
            ui.slotNormal = S("ui_slot"); ui.slotSelected = S("ui_slot_selected"); ui.slotDisabled = S("ui_slot_disabled");
            ui.bagClosedSprite = S("icon_bag"); ui.bagOpenSprite = S("icon_bag_open");
            EditorUtility.SetDirty(ui);
        }
        var pm = hud.GetComponent<PauseMenu>();
        if (pm != null) { Undo.RecordObject(pm, "Repair HUD"); pm.pauseIcon = S("icon_pause"); pm.playIcon = S("icon_play"); EditorUtility.SetDirty(pm); }
        var hearts = hud.GetComponentInChildren<HeartsUI>(true);
        if (hearts != null) { Undo.RecordObject(hearts, "Repair HUD"); hearts.fullHeart = S("icon_heart"); hearts.emptyHeart = S("icon_heart_empty"); EditorUtility.SetDirty(hearts); }

        ApplyLook(hud);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("HUD repaired",
            missing.Count == 0 ? "All pictures are back. Save the scene (Ctrl+S) and press Play."
                               : "Fixed what I could, but these sprites are still missing: " + string.Join(", ", missing) + ".\nMake sure the zip's Sprites folder is inside Assets.", "OK");
    }

    // Panels, slots and buttons get the chosen see-through amount
    void ApplyLook(GameObject hud)
    {
        foreach (var img in hud.GetComponentsInChildren<Image>(true))
        {
            if (img.sprite == null) continue;
            string sn = img.sprite.name;
            if (sn.StartsWith("ui_panel") || sn.StartsWith("ui_slot") || sn.StartsWith("ui_button") || sn == "ui_bar_back")
            {
                var c = img.color; c.a = panelOpacity; img.color = c;
                EditorUtility.SetDirty(img);
            }
        }
    }

    // ================================================================== player fix
    bool PatchPlayerController()
    {
        string guid = AssetDatabase.FindAssets("PlayerController t:MonoScript")
            .FirstOrDefault(g => Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)) == "PlayerController.cs");
        if (guid == null) return false;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        string full = Path.GetFullPath(path);
        string code = File.ReadAllText(full);
        string original = code;

        // 1) do nothing while paused
        if (!code.Contains("Time.timeScale == 0f"))
        {
            int u = code.IndexOf("void Update()");
            int brace = u >= 0 ? code.IndexOf('{', u) : -1;
            if (brace >= 0) code = code.Insert(brace + 1, "\n        if (Time.timeScale == 0f) return; // paused or bag open\n");
        }
        // 2) clicking a UI button shouldn't make the player attack
        if (!code.Contains("IsPointerOverGameObject"))
        {
            int r = code.IndexOf("ReadInput(out moveX");
            int end = r >= 0 ? code.IndexOf(';', r) : -1;
            if (end >= 0)
                code = code.Insert(end + 1, "\n        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) { atk1 = false; atk2 = false; }");
        }
        if (code == original) return false;

        string backupDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Backups"));
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, $"PlayerController_{DateTime.Now:yyyyMMdd_HHmmss}.cs.txt"), original);
        File.WriteAllText(full, code);
        AssetDatabase.ImportAsset(path);
        return true;
    }

    // ================================================================== UI helpers
    static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        Place(r, anchor, pivot, pos, size);
        return r;
    }

    static Image Img(string name, Transform parent, Sprite sprite, bool sliced = false, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = raycast;
        if (sprite == null) img.color = Fallback;
        if (sliced) img.type = Image.Type.Sliced;
        else img.preserveAspect = true;
        return img;
    }

    static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color col, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = col; t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Tint(Button b)
    {
        b.transition = Selectable.Transition.ColorTint;
        var c = b.colors;
        c.highlightedColor = new Color(0.8f, 1f, 1f); c.pressedColor = new Color(0.65f, 0.75f, 0.75f); c.selectedColor = Color.white;
        b.colors = c;
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
    }
    static void TopLeft(RectTransform r, float x, float y, float w, float h) => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));
    static void Center(RectTransform r, float w, float h) => Place(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
    static void Stretch(RectTransform r, float pad)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(pad, pad); r.offsetMax = new Vector2(-pad, -pad);
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
        Undo.RegisterCreatedObjectUndo(es, "Build HUD");
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = "Assets";
        foreach (var part in folder.Split('/').Skip(1))
        {
            string next = $"{parent}/{part}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part);
            parent = next;
        }
    }

    static string Nice(string snake) =>
        string.Join(" ", snake.Split('_').Select(w => char.ToUpper(w[0]) + w.Substring(1)));
}
