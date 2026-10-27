// PUT THIS FILE INSIDE A FOLDER NAMED "Editor" (e.g. Assets/Editor/PlayerSetupWizard.cs)
// Open it from the top menu: Tools > Player Setup Wizard
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class PlayerSetupWizard : EditorWindow
{
    enum Role { Skip, Idle, Run, Jump, Fall, Attack1, Attack2, TakeHit, Death }

    class SheetEntry
    {
        public Texture2D tex;
        public List<Sprite> sprites;
        public Role role;
        public bool loop;
    }

    DefaultAsset sheetFolder;
    List<SheetEntry> entries = new List<SheetEntry>();
    float fps = 12f;
    bool pixelArt = true;
    bool cameraFollows = true;
    string outputFolder = "Assets/Animations/Player";
    Vector2 scroll;

    [MenuItem("Tools/Player Setup Wizard")]
    static void Open() => GetWindow<PlayerSetupWizard>("Player Setup");

    // ---------------------------------------------------------------- UI
    void OnGUI()
    {
        EditorGUILayout.LabelField("1. Drag in the FOLDER that holds all your sprite sheets", EditorStyles.boldLabel);
        var newFolder = (DefaultAsset)EditorGUILayout.ObjectField("Sheets Folder", sheetFolder, typeof(DefaultAsset), false);
        if (newFolder != sheetFolder) { sheetFolder = newFolder; Scan(); }

        if (sheetFolder == null)
        {
            EditorGUILayout.HelpBox("Drag the folder (from the Project window) that contains Idle, Run, Jump, Attack1... sheets.", MessageType.Info);
            return;
        }
        if (GUILayout.Button("Rescan folder")) Scan();
        if (entries.Count == 0)
        {
            EditorGUILayout.HelpBox("No images found in that folder.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. Check each sheet's role (auto-detected from the file name)", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(320));
        foreach (var e in entries)
        {
            EditorGUILayout.BeginHorizontal("box");
            Texture preview = e.sprites.Count > 0 ? AssetPreview.GetAssetPreview(e.sprites[0]) : null;
            if (preview == null) { preview = Texture2D.grayTexture; Repaint(); }
            GUILayout.Label(preview, GUILayout.Width(40), GUILayout.Height(40));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(e.tex.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(e.sprites.Count > 1 ? $"{e.sprites.Count} frames" : "⚠ not sliced (1 frame)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            e.role = (Role)EditorGUILayout.EnumPopup(e.role, GUILayout.Width(90));
            e.loop = EditorGUILayout.ToggleLeft("Loop", e.loop, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        var dupes = entries.Where(x => x.role != Role.Skip).GroupBy(x => x.role).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            EditorGUILayout.HelpBox("Used more than once (only the first will be used): " + string.Join(", ", dupes), MessageType.Warning);
        if (!entries.Any(x => x.role == Role.Idle))
            EditorGUILayout.HelpBox("You need one sheet set to Idle.", MessageType.Error);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3. Options", EditorStyles.boldLabel);
        fps = Mathf.Max(1f, EditorGUILayout.FloatField("Frames per second", fps));
        pixelArt = EditorGUILayout.Toggle(new GUIContent("Pixel art (crisp)", "Point filter + no compression on every sheet."), pixelArt);
        cameraFollows = EditorGUILayout.Toggle("Camera follows player", cameraFollows);
        outputFolder = EditorGUILayout.TextField("Save animations to", outputFolder);

        EditorGUILayout.Space();
        GUI.enabled = entries.Any(x => x.role == Role.Idle);
        if (GUILayout.Button("Build Player", GUILayout.Height(36))) Build();
        GUI.enabled = true;
    }

    // ---------------------------------------------------------------- Scanning
    void Scan()
    {
        entries.Clear();
        if (sheetFolder == null) return;
        string folder = AssetDatabase.GetAssetPath(sheetFolder);
        if (!AssetDatabase.IsValidFolder(folder)) return;

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) continue;
            var role = GuessRole(tex.name);
            entries.Add(new SheetEntry { tex = tex, sprites = LoadSprites(path), role = role, loop = DefaultLoop(role) });
        }
        entries = entries.OrderBy(e => e.role == Role.Skip).ThenBy(e => (int)e.role).ToList();
    }

    static List<Sprite> LoadSprites(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderBy(s => TrailingNumber(s.name)).ThenBy(s => s.name).ToList();

    static int TrailingNumber(string name)
    {
        var m = Regex.Match(name, @"(\d+)$");
        return m.Success ? int.Parse(m.Value) : int.MaxValue;
    }

    static Role GuessRole(string name)
    {
        string n = name.ToLower().Replace(" ", "").Replace("_", "").Replace("-", "");
        if (n.Contains("attack2") || n.Contains("attackb")) return Role.Attack2;
        if (n.Contains("attack")) return Role.Attack1;
        if (n.Contains("takehit") || n.Contains("hurt") || n.Contains("hit") || n.Contains("damage")) return Role.TakeHit;
        if (n.Contains("death") || n.Contains("die") || n.Contains("dead")) return Role.Death;
        if (n.Contains("fall")) return Role.Fall;
        if (n.Contains("jump")) return Role.Jump;
        if (n.Contains("run") || n.Contains("walk")) return Role.Run;
        if (n.Contains("idle")) return Role.Idle;
        return Role.Skip;
    }

    static bool DefaultLoop(Role r) => r == Role.Idle || r == Role.Run || r == Role.Jump || r == Role.Fall;

    // ---------------------------------------------------------------- Build
    void Build()
    {
        var used = entries.Where(e => e.role != Role.Skip && e.sprites.Count > 0)
                          .GroupBy(e => e.role).Select(g => g.First()).ToList();

        if (pixelArt)
        {
            foreach (var e in used)
            {
                string p = AssetDatabase.GetAssetPath(e.tex);
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    ti.filterMode = FilterMode.Point;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
                e.sprites = LoadSprites(p);
            }
        }

        EnsureFolder(outputFolder);

        // 1) Clips
        var clips = new Dictionary<Role, AnimationClip>();
        foreach (var e in used) clips[e.role] = CreateClip(e.role.ToString(), e.sprites, e.loop);

        // 2) Animator Controller
        var controller = BuildController(clips);

        // 3) Player object
        var idleSprite = used.First(e => e.role == Role.Idle).sprites[0];
        var player = new GameObject("Player");
        Undo.RegisterCreatedObjectUndo(player, "Create Player");

        var sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = idleSprite;

        var animator = player.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        var rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale = 3f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Collider hugs the visible character, not the empty space around it
        var b = TightBounds(idleSprite);
        var col = player.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(b.size.x * 0.7f, b.size.y);
        col.offset = new Vector2(0f, b.center.y);

        player.AddComponent<PlayerController>();

        if (cameraFollows && Camera.main != null)
        {
            Undo.SetTransformParent(Camera.main.transform, player.transform, "Camera follow");
            Camera.main.transform.localPosition = new Vector3(0, 0, -10);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = player;
        Debug.Log($"Player built with {clips.Count} animations: {string.Join(", ", clips.Keys)}. Press Play!");
    }

    AnimatorController BuildController(Dictionary<Role, AnimationClip> clips)
    {
        string ctrlPath = $"{outputFolder}/Player.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack1", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var layout = new Dictionary<Role, Vector3>
        {
            { Role.Idle, new Vector3(300, 100) },   { Role.Run, new Vector3(550, 100) },
            { Role.Jump, new Vector3(300, 250) },   { Role.Fall, new Vector3(550, 250) },
            { Role.Attack1, new Vector3(50, 0) },   { Role.Attack2, new Vector3(50, 100) },
            { Role.TakeHit, new Vector3(300, -60) },{ Role.Death, new Vector3(550, -60) },
        };

        var st = new Dictionary<Role, AnimatorState>();
        foreach (var kv in clips)
        {
            var s = sm.AddState(kv.Key.ToString(), layout[kv.Key]);
            s.motion = kv.Value;
            st[kv.Key] = s;
        }

        var idle = st[Role.Idle];
        sm.defaultState = idle;
        st.TryGetValue(Role.Run, out var run);
        st.TryGetValue(Role.Jump, out var jump);
        st.TryGetValue(Role.Fall, out var fall);

        var ground = new List<AnimatorState> { idle };
        if (run != null) ground.Add(run);
        var locomotion = new List<AnimatorState>(ground);
        if (jump != null) locomotion.Add(jump);
        if (fall != null) locomotion.Add(fall);

        // Idle <-> Run
        if (run != null)
        {
            T(idle, run).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            T(run, idle).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        }

        // Leaving the ground
        foreach (var g in ground)
        {
            if (jump != null)
            {
                var t = T(g, jump);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "VerticalVelocity");
            }
            if (fall != null)
            {
                var t = T(g, fall);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalVelocity");
            }
        }

        // Jump -> Fall at the top of the arc
        if (jump != null && fall != null)
            T(jump, fall).AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalVelocity");

        // Landing
        if (jump != null) T(jump, idle).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        if (fall != null) T(fall, idle).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // Attacks: from any movement state, play once, then back to Idle
        foreach (var (role, trigger) in new[] { (Role.Attack1, "Attack1"), (Role.Attack2, "Attack2") })
        {
            if (!st.TryGetValue(role, out var atk)) continue;
            foreach (var from in locomotion) T(from, atk).AddCondition(AnimatorConditionMode.If, 0, trigger);
            ReturnToIdle(atk, idle);
        }

        // Take hit: can interrupt anything (the script won't fire it after death)
        if (st.TryGetValue(Role.TakeHit, out var hit))
        {
            var t = sm.AddAnyStateTransition(hit);
            Instant(t); t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Hurt");
            ReturnToIdle(hit, idle);
        }

        // Death: from anywhere, never leaves (stays on last frame)
        if (st.TryGetValue(Role.Death, out var death))
        {
            var t = sm.AddAnyStateTransition(death);
            Instant(t); t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Die");
        }

        return controller;
    }

    static AnimatorStateTransition T(AnimatorState from, AnimatorState to)
    {
        var t = from.AddTransition(to);
        Instant(t);
        return t;
    }

    static void Instant(AnimatorStateTransition t) { t.hasExitTime = false; t.duration = 0f; }

    static void ReturnToIdle(AnimatorState s, AnimatorState idle)
    {
        var back = s.AddTransition(idle);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0f;
    }

    AnimationClip CreateClip(string name, List<Sprite> frames, bool loop)
    {
        var clip = new AnimationClip { frameRate = fps };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count / fps, value = frames[frames.Count - 1] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = $"{outputFolder}/{name}.anim";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static Bounds TightBounds(Sprite s)
    {
        var v = s.vertices;
        if (v == null || v.Length == 0) return s.bounds;
        Vector2 min = v[0], max = v[0];
        foreach (var p in v) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return new Bounds((min + max) / 2f, max - min);
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
}