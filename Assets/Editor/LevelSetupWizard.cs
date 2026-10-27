// PUT THIS FILE INSIDE A FOLDER NAMED "Editor" (e.g. Assets/Editor/LevelSetupWizard.cs)
// Open it from the top menu: Tools > Level Setup Wizard
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSetupWizard : EditorWindow
{
    SceneAsset mainMenuScene;
    SceneAsset level1Scene;
    string levelSelectName = "LevelSelect";
    string level2Name = "Level2";
    string playButtonName = "PlayButton";

    [MenuItem("Tools/Level Setup Wizard")]
    static void Open() => GetWindow<LevelSetupWizard>("Level Setup");

    void OnEnable()
    {
        if (mainMenuScene == null) mainMenuScene = FindScene("MainMenu");
        if (level1Scene == null) level1Scene = FindScene("SampleScene");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Your existing scenes", EditorStyles.boldLabel);
        mainMenuScene = (SceneAsset)EditorGUILayout.ObjectField("Main Menu scene", mainMenuScene, typeof(SceneAsset), false);
        level1Scene = (SceneAsset)EditorGUILayout.ObjectField("Level 1 (game) scene", level1Scene, typeof(SceneAsset), false);
        playButtonName = EditorGUILayout.TextField("Play button name", playButtonName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("New scenes to create", EditorStyles.boldLabel);
        levelSelectName = EditorGUILayout.TextField("Level Select scene", levelSelectName);
        level2Name = EditorGUILayout.TextField("Level 2 scene", level2Name);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This will:\n" +
            $"• Create '{levelSelectName}' with the same look as your Main Menu, with LEVEL 1 / LEVEL 2 / LEVEL 3 (locked) / BACK buttons\n" +
            $"• Create '{level2Name}' as a copy of your Level 1 scene (an existing one is never overwritten)\n" +
            $"• Make the PLAY button open '{levelSelectName}'\n" +
            "• Add all scenes to Build Settings, with Main Menu first", MessageType.Info);

        GUI.enabled = mainMenuScene != null && level1Scene != null;
        if (GUILayout.Button("Set Up Levels", GUILayout.Height(36))) Build();
        GUI.enabled = true;
    }

    // ------------------------------------------------------------------ main flow
    void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string startScene = SceneManager.GetActiveScene().path;

        string menuPath = AssetDatabase.GetAssetPath(mainMenuScene);
        string level1Path = AssetDatabase.GetAssetPath(level1Scene);
        string menuFolder = Path.GetDirectoryName(menuPath).Replace('\\', '/');
        string levelFolder = Path.GetDirectoryName(level1Path).Replace('\\', '/');
        string selectPath = $"{menuFolder}/{levelSelectName}.unity";
        string level2Path = $"{levelFolder}/{level2Name}.unity";

        string menuName = Path.GetFileNameWithoutExtension(menuPath);
        string level1Name = Path.GetFileNameWithoutExtension(level1Path);

        // 1) Level 2 = copy of Level 1 (never overwrite existing work)
        if (!File.Exists(level2Path))
            AssetDatabase.CopyAsset(level1Path, level2Path);
        else
            Debug.Log($"'{level2Name}' already exists, kept it as is.");

        // 2) Level Select = copy of Main Menu, then swap the buttons
        bool buildSelect = true;
        if (File.Exists(selectPath))
            buildSelect = EditorUtility.DisplayDialog("Level Select already exists",
                $"'{levelSelectName}' already exists. Rebuild it? Any changes you made to it will be lost.", "Rebuild", "Keep it");
        if (buildSelect)
        {
            AssetDatabase.DeleteAsset(selectPath);
            AssetDatabase.CopyAsset(menuPath, selectPath);
            AssetDatabase.Refresh();
            BuildLevelSelect(selectPath, menuName, level1Name, level2Name);
        }

        // 3) Play button -> Level Select
        bool wired = WireMainMenu(menuPath);

        // 4) Build Settings
        SetBuildScenes(menuPath, selectPath, level1Path, level2Path);

        if (!string.IsNullOrEmpty(startScene) && File.Exists(startScene))
            EditorSceneManager.OpenScene(startScene);

        EditorUtility.DisplayDialog("Done!",
            "Levels are set up.\n\nOpen your Main Menu scene and press Play to test:\nPLAY → Level Select → Level 1 / Level 2." +
            (wired ? "" : $"\n\n⚠ Couldn't find a button named '{playButtonName}', so the Play button wasn't connected. Check the Console."),
            "OK");
    }

    // ------------------------------------------------------------------ level select
    void BuildLevelSelect(string path, string menuName, string level1Name, string level2Name)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        // Remove the main menu's manager (its script belongs to the main menu)
        foreach (var root in scene.GetRootGameObjects())
            if (root.name.ToLower().Contains("menumanager")) Object.DestroyImmediate(root);

        var canvas = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(c => c.isRootCanvas);
        if (canvas == null) { Debug.LogError("No Canvas found in the Level Select copy."); return; }

        // Remember the PLAY text's style, then remove the old menu buttons
        var allTexts = canvas.GetComponentsInChildren<TMP_Text>(true);
        var template = allTexts.FirstOrDefault(t => t.gameObject.name == playButtonName)
                    ?? allTexts.FirstOrDefault(t => t.text.Trim().ToUpper() == "PLAY");

        TMP_FontAsset font = template ? template.font : TMP_Settings.defaultFontAsset;
        Material fontMat = template ? template.fontSharedMaterial : null;
        Color color = template ? template.color : new Color(0.36f, 0.6f, 0.6f);
        FontStyles style = template ? template.fontStyle : FontStyles.Bold;
        float baseSize = template ? template.fontSize : 100f;

        var toDelete = new List<GameObject>();
        foreach (var t in canvas.GetComponentsInChildren<Transform>(true))
        {
            if (t == canvas.transform) continue;
            var txt = t.GetComponent<TMP_Text>();
            string label = txt ? txt.text.Trim().ToUpper() : "";
            if (t.name.ToLower().Contains("button") || label == "PLAY" || label == "QUIT")
                toDelete.Add(t.gameObject);
        }
        foreach (var go in toDelete)
            if (go != null) Object.DestroyImmediate(go);

        // Scene loader for the buttons
        var manager = new GameObject("LevelSelectManager");
        var loader = manager.AddComponent<SceneLoader>();

        // Button column, centered on the door
        float size = baseSize * 0.6f;
        var column = new GameObject("LevelButtons", typeof(RectTransform));
        column.layer = LayerMask.NameToLayer("UI");
        column.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)column.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.4f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size * 9f, size * 1.25f * 4f);
        rt.anchoredPosition = Vector2.zero;

        var layout = column.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = size * 0.1f;

        void MakeButton(string objName, string label, string sceneToLoad, bool interactable, float fontSize)
        {
            var go = new GameObject(objName, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(column.transform, false);

            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.font = font;
            if (fontMat) txt.fontSharedMaterial = fontMat;
            txt.color = color;
            txt.fontStyle = style;
            txt.fontSize = fontSize;
            txt.alignment = TextAlignmentOptions.Center;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize * 1.15f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = txt;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.pressedColor = new Color(0.5f, 0.5f, 0.5f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = colors;
            btn.interactable = interactable;

            if (!string.IsNullOrEmpty(sceneToLoad))
                UnityEventTools.AddStringPersistentListener(btn.onClick, new UnityAction<string>(loader.LoadScene), sceneToLoad);
        }

        MakeButton("Level1Button", "LEVEL 1", level1Name, true, size);
        MakeButton("Level2Button", "LEVEL 2", level2Name, true, size);
        MakeButton("Level3Button", "LEVEL 3 - LOCKED", null, false, size);
        MakeButton("BackButton", "BACK", menuName, true, size * 0.75f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ------------------------------------------------------------------ main menu
    bool WireMainMenu(string menuPath)
    {
        var scene = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        var play = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .FirstOrDefault(t => t.name == playButtonName);
        if (play == null)
        {
            Debug.LogWarning($"No object named '{playButtonName}' in the Main Menu. Select your Play button, add a SceneLoader " +
                             $"to any object, and set the button's On Click to SceneLoader.LoadScene(\"{levelSelectName}\").");
            return false;
        }

        // Put the SceneLoader on MainMenuManager if it exists, otherwise on a new object
        var holder = roots.FirstOrDefault(r => r.name == "MainMenuManager") ?? new GameObject("SceneLoader");
        var loader = holder.GetComponent<SceneLoader>() ?? holder.AddComponent<SceneLoader>();

        var btn = play.GetComponent<Button>();
        if (btn == null)
        {
            btn = play.gameObject.AddComponent<Button>();
            btn.targetGraphic = play.GetComponent<Graphic>();
        }

        // Replace whatever Play did before with "open Level Select"
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(btn.onClick, i);
        UnityEventTools.AddStringPersistentListener(btn.onClick, new UnityAction<string>(loader.LoadScene), levelSelectName);

        EditorUtility.SetDirty(btn);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    // ------------------------------------------------------------------ helpers
    static void SetBuildScenes(params string[] required)
    {
        var existing = EditorBuildSettings.scenes.ToList();
        var result = new List<EditorBuildSettingsScene>();
        foreach (var p in required)
            result.Add(new EditorBuildSettingsScene(p, true));
        foreach (var s in existing)
            if (!required.Contains(s.path)) result.Add(s);
        EditorBuildSettings.scenes = result.ToArray();
    }

    static SceneAsset FindScene(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:Scene {name}", new[] { "Assets" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name)
                return AssetDatabase.LoadAssetAtPath<SceneAsset>(p);
        }
        return null;
    }
}
