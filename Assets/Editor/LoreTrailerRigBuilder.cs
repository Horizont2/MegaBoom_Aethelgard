using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Unity.Cinemachine;

// One-click scaffolder for the Hollow Siege LORE TRAILER camera rig.
//
//   Tools ▸ Lore Trailer ▸ Build Camera Rig
//
// It creates, under a single "LoreTrailer_Rig" object:
//   * one CinemachineCamera per shot (16), named + numbered per the script,
//     each with the correct body (Static / SplineDolly / OrbitalFollow),
//     aim (RotationComposer), handheld Noise (amplitude/frequency), and Lens
//     FOV already dialled in;
//   * a LoreTrailerShotNote on each, describing the shot and what to assign;
//   * a Timeline (.playable) with a CinemachineTrack whose CinemachineShot
//     clips are laid out in order at the scripted durations and already wired
//     to the cameras, bound to the scene's CinemachineBrain.
//
// What it deliberately does NOT do (this is manual, visual work it can't guess):
//   * position the cameras in the scene,
//   * draw the Spline paths and assign them to the SplineDolly components,
//   * assign per-shot targets other than the Player (hero) which it auto-finds.
// The rig root is left DISABLED so the new cameras don't fight the gameplay
// camera — enable it when you're ready to position/record.
//
// Targets Cinemachine 3.1.x + Timeline 1.8.x. If any line fails to compile in a
// different package version, just delete this one file — nothing else depends
// on it — and it can be regenerated.
public static class LoreTrailerRigBuilder
{
    private enum Body { Static, Spline, Orbital }

    private struct ShotSpec
    {
        public string name;
        public Body body;
        public bool composer;
        public float noiseAmp, noiseFreq;
        public float fov;
        public float duration;
        public bool followHero;
        public string note;

        public ShotSpec(string name, Body body, bool composer, float amp, float freq,
                        float fov, float duration, bool followHero, string note)
        {
            this.name = name; this.body = body; this.composer = composer;
            this.noiseAmp = amp; this.noiseFreq = freq; this.fov = fov;
            this.duration = duration; this.followHero = followHero; this.note = note;
        }
    }

    private static readonly ShotSpec[] Shots =
    {
        new ShotSpec("CM_01_MertvaDoroga", Body.Static,  false, 0f,   0f,   40f,  5f, false,
            "Кадр 01 · Мертва дорога. Нерухома, ледь помітний push (аніміуй Lens FOV 42→38 на Animation Track). Noise 0. Fade from black."),
        new ShotSpec("CM_02_Vershnik",     Body.Spline,  true,  0.4f, 0.3f, 38f,  6f, true,
            "Кадр 02 · Вершник. SplineDolly вздовж дороги, RotationComposer на героя (lead room попереду). Handheld noise (0.4/0.3). Намалюй Spline уздовж дороги й признач у SplineDolly."),
        new ShotSpec("CM_03_Kopyta",       Body.Static,  true,  0.2f, 0.2f, 34f,  4f, true,
            "Кадр 03 · Копита. Низька камера біля землі, tilt up на героя (RotationComposer). Легкий noise. Коротка слоу-мо на удар копит."),
        new ShotSpec("CM_04_RozkryttyaPustky", Body.Spline, true, 0f, 0f,   40f,  5f, true,
            "Кадр 04 · Розкриття пустки. Кран угору по дузі за героєм. Намалюй вертикально-дуговий Spline (угору+назад)."),
        new ShotSpec("CM_05_TabirOzhyvaye", Body.Spline, true,  0f,   0f,   40f,  6f, false,
            "Кадр 05 · Табір оживає (CampScene). Кран угору над табором. Target → центр табору/багаття (признач вручну)."),
        new ShotSpec("CM_06_Summer",       Body.Spline,  true,  0f,   0f,   42f,  6f, false,
            "Кадр 06 · Літо (зелений ліс). Кран над кронами. ЗАПАМ'ЯТАЙ цей рух — кадри 07/08 повторюють його один-в-один."),
        new ShotSpec("CM_07_Autumn",       Body.Spline,  true,  0f,   0f,   42f,  6f, false,
            "Кадр 07 · Осінь. ТОЙ САМИЙ рух, що кадр 06, над осіннім/пустельним біомом (падаюче листя). Word-card AUTUMN у DaVinci."),
        new ShotSpec("CM_08_Winter",       Body.Spline,  true,  0f,   0f,   42f,  6f, false,
            "Кадр 08 · Зима. ТОЙ САМИЙ рух, над сніговим біомом (падаючий сніг). Word-card WINTER у DaVinci."),
        new ShotSpec("CM_09_ZhyveNebo",    Body.Static,  false, 0f,   0f,   45f,  6f, false,
            "Кадр 09 · Живе небо. Майже нерухома, ледь push. Погода/час через DayNightCycle.ForceWeather + прискорений час на Timeline."),
        new ShotSpec("CM_10_Orda",         Body.Orbital, true,  0.15f,0.2f, 40f,  7f, false,
            "Кадр 10 · Орда. OrbitalFollow навколо центру орди, низька повільна орбіта. Target → центр орди. Вороги заморожені (EnemyAI.GlobalFreeze)."),
        new ShotSpec("CM_11_SkvernaHeroy", Body.Spline,  true,  0f,   0f,   38f,  6f, false,
            "Кадр 11 · Скверна й герой. Push-in на тотем скверни, потім силует героя. Target → тотем (і/або герой)."),
        new ShotSpec("CM_12_TronCraneDown",Body.Spline,  true,  0f,   0f,   30f, 11f, false,
            "Кадр 12 · Трон прокляття — кран ВНИЗ. Вертикальний Spline високо→низько, RotationComposer на замок. Вузький FOV. Target → замок."),
        new ShotSpec("CM_13_Vartovi",      Body.Static,  true,  0f,   0f,   35f, 10f, false,
            "Кадр 13 · Вартові трону. ШАБЛОН — продублюй цю камеру під КОЖНОГО боса, push-in на пару «бос+його тотем». На монтажі — жорсткі різи під біт. Target → бос+тотем."),
        new ShotSpec("CM_14_Klyatva",      Body.Static,  false, 0.3f, 0.25f,35f,  6f, true,
            "Кадр 14 · Клятва. Низький героїчний ракурс, handheld noise. Коротка слоу-мо на здійманні зброї (Time Dilation). Хвиля світла крізь морок."),
        new ShotSpec("CM_15_ZahalnyVyd",   Body.Spline,  true,  0f,   0f,   55f, 12f, false,
            "Кадр 15 · Загальний вид — кран УГОРУ (дзеркало кадру 12). Вертикальний Spline низько→високо, RotationComposer на замок. Широкий FOV. Фінальний кадр."),
        new ShotSpec("CM_16_Logo",         Body.Static,  false, 0f,   0f,   40f,  6f, false,
            "Кадр 16 · Лого. Статика / кінцева картка. Саме лого + WISHLIST NOW ON STEAM додаються в DaVinci."),
    };

    private const string RigName = "LoreTrailer_Rig";
    private const string TimelineDir = "Assets/LoreTrailer";
    private const string TimelinePath = TimelineDir + "/LoreTrailer_Timeline.playable";

    [MenuItem("Tools/Lore Trailer/Build Camera Rig")]
    public static void BuildRig()
    {
        // TrailerFind, so a PARKED rig still counts as "already exists" — with
        // GameObject.Find this guard never fired and rigs piled up.
        if (TrailerFind.ByName(RigName) != null &&
            !EditorUtility.DisplayDialog("Lore Trailer Rig",
                "A \"" + RigName + "\" already exists in the scene. Build another one?",
                "Build anyway", "Cancel"))
            return;

        // Timeline asset lives on disk.
        if (!AssetDatabase.IsValidFolder(TimelineDir))
            AssetDatabase.CreateFolder("Assets", "LoreTrailer");

        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, AssetDatabase.GenerateUniqueAssetPath(TimelinePath));

        var rig = new GameObject(RigName);
        Undo.RegisterCreatedObjectUndo(rig, "Build Lore Trailer Rig");
        var director = rig.AddComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.playOnAwake = false;

        var track = timeline.CreateTrack<CinemachineTrack>(null, "Cinemachine Shots");

        NoiseSettings noiseProfile = FindNoiseProfile();
        Transform hero = FindHero();
        int missingTargets = 0;
        double playhead = 0;

        foreach (var s in Shots)
        {
            // --- the virtual camera ---
            var go = new GameObject(s.name);
            go.transform.SetParent(rig.transform, false);

            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = s.fov;

            if (s.followHero && hero != null) cam.Target.TrackingTarget = hero;
            else if (s.body != Body.Static || s.composer) missingTargets++;

            switch (s.body)
            {
                case Body.Spline:  go.AddComponent<CinemachineSplineDolly>();   break;
                case Body.Orbital: go.AddComponent<CinemachineOrbitalFollow>(); break;
                case Body.Static:  /* no body component — camera keeps its own transform */ break;
            }

            if (s.composer) go.AddComponent<CinemachineRotationComposer>();

            if (s.noiseAmp > 0f)
            {
                var noise = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                noise.AmplitudeGain = s.noiseAmp;
                noise.FrequencyGain = s.noiseFreq;
                if (noiseProfile != null) noise.NoiseProfile = noiseProfile;
            }

            var noteComp = go.AddComponent<LoreTrailerShotNote>();
            noteComp.note = s.note;

            // --- its Timeline clip ---
            var clip = track.CreateClip<CinemachineShot>();
            clip.start = playhead;
            clip.duration = s.duration;
            clip.displayName = s.name;
            playhead += s.duration;

            var shotAsset = (CinemachineShot)clip.asset;
            var exposed = shotAsset.VirtualCamera;
            exposed.exposedName = System.Guid.NewGuid().ToString("N");
            shotAsset.VirtualCamera = exposed;
            director.SetReferenceValue(exposed.exposedName, cam);
            EditorUtility.SetDirty(shotAsset);
        }

        // Bind the track to the scene's CinemachineBrain (on the Main Camera).
        var brain = Object.FindFirstObjectByType<CinemachineBrain>();
        if (brain != null) director.SetGenericBinding(track, brain);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(rig);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Leave the rig off so it doesn't hijack the gameplay camera. Enable it
        // when you want to position cameras / record.
        rig.SetActive(false);
        Selection.activeGameObject = rig;

        Debug.Log(
            "[LoreTrailerRig] Built " + Shots.Length + " cameras + Timeline at '" + TimelinePath + "'.\n" +
            "NEXT STEPS:\n" +
            "  1. Enable the '" + RigName + "' object.\n" +
            "  2. Position each CM_* camera and, for Spline shots, draw a Spline (GameObject ▸ Spline) and assign it to the CinemachineSplineDolly.\n" +
            "  3. Assign the remaining Targets (camp / horde / totem / castle / bosses) — read each camera's LoreTrailerShotNote in the Inspector.\n" +
            "  4. Assign a Noise Profile on the handheld shots if none was auto-found.\n" +
            "  5. Open the Timeline window with the rig selected, fine-tune clip lengths, and record with Unity Recorder.\n" +
            (brain == null ? "  ⚠ No CinemachineBrain found on the Main Camera — add one and bind the Cinemachine track manually.\n" : "") +
            (noiseProfile == null ? "  ⚠ No NoiseSettings profile found — assign one (e.g. Handheld) on the noise components.\n" : "") +
            (hero == null ? "  ⚠ No 'Player'-tagged hero found — assign the hero Target on the ride/oath shots.\n" : "") +
            (missingTargets > 0 ? "  ℹ " + missingTargets + " shot(s) still need a Target assigned (see each note)." : ""));

        EditorGUIUtility.PingObject(rig);
    }

    private static NoiseSettings FindNoiseProfile()
    {
        // Prefer a "Handheld" profile; fall back to any NoiseSettings that ships
        // with Cinemachine.
        string[] guids = AssetDatabase.FindAssets("t:NoiseSettings Handheld");
        if (guids == null || guids.Length == 0) guids = AssetDatabase.FindAssets("t:NoiseSettings");
        if (guids != null && guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<NoiseSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        return null;
    }

    private static Transform FindHero()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        return p != null ? p.transform : null;
    }
}
