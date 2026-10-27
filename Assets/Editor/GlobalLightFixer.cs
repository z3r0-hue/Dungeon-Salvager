// PUT THIS FILE INSIDE A FOLDER NAMED "Editor" (e.g. Assets/Editor/GlobalLightFixer.cs)
// Menu: Tools > Dungeon Salvager > Fix Duplicate Global Lights
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class GlobalLightFixer
{
    [MenuItem("Tools/Dungeon Salvager/Fix Duplicate Global Lights")]
    static void Fix()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string startScene = SceneManager.GetActiveScene().path;

        var report = new StringBuilder();
        int fixedCount = 0;

        // check EVERY scene in the project (Unity 6 Build Profiles can use their own scene list)
        var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
        if (scenes.Count == 0) { EditorUtility.DisplayDialog("No scenes", "No scenes found in Assets.", "OK"); return; }

        foreach (var path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            // EVERY Global 2D light: switched on or off, visible or hidden inside other objects
            var globals = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<Light2D>(true))
                .Where(l => l.lightType == Light2D.LightType.Global)
                .ToList();

            if (globals.Count <= 1)
            {
                report.AppendLine($"✓ {scene.name}: OK ({globals.Count} global light{(globals.Count == 1 ? ": " + GetPath(globals[0].transform) : "")})");
                continue;
            }

            // keep a switched-on one called "Global Light..." (or any switched-on one), remove the rest
            var keep = globals.FirstOrDefault(l => l.enabled && l.gameObject.activeInHierarchy && l.gameObject.name.StartsWith("Global Light"))
                    ?? globals.FirstOrDefault(l => l.enabled && l.gameObject.activeInHierarchy)
                    ?? globals[0];
            keep.enabled = true;
            var turnedOff = new List<string>();
            foreach (var l in globals)
            {
                if (l == keep) continue;
                turnedOff.Add(GetPath(l.transform) + (l.enabled && l.gameObject.activeInHierarchy ? "" : " (was switched off)"));
                Undo.DestroyObjectImmediate(l);   // removes only the light, not the object it was on
                fixedCount++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            report.AppendLine($"✗ {scene.name} ({path}): had {globals.Count}. Kept \"{GetPath(keep.transform)}\", removed the light from:");
            foreach (var t in turnedOff) report.AppendLine($"     - {t}");
        }

        if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene);

        Debug.Log("Global light check:\n" + report);
        EditorUtility.DisplayDialog(fixedCount > 0 ? "Fixed!" : "All good",
            report + (fixedCount > 0 ? "\nOnly the extra Light 2D components were removed (their objects are still there). Build again." : "\nNo duplicates found in your build scenes."),
            "OK");
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
