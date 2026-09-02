using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds a cinematic post-processing look for the lore trailer and drops it into
// the current scene as a high-priority global Volume, so each act's scene can be
// graded in one click without touching the gameplay volume.
//
//   Tools ▸ Lore Trailer ▸ Apply Cinematic Post FX (Act I / Road)
//
// It creates a VolumeProfile asset with a filmic grade (tonemap + color grade +
// bloom + vignette + film grain + chromatic aberration) and, crucially, MOTION
// BLUR — which is what actually sells the speed of the gallop. It also enables
// post-processing on the Main Camera so the volume is visible.
//
// Presets let the same tool grade the other acts later (warm camp, seasons,
// corruption, throne) — add a menu item calling Apply(Preset.X).
public static class TrailerCinematicPostFX
{
    public enum Preset { RoadMoody, CampWarm, Summer, Autumn, Winter, Corruption, Throne }

    private const string Dir = "Assets/LoreTrailer";

    [MenuItem("Tools/Lore Trailer/Apply Cinematic Post FX (Act I - Road)")]
    public static void ApplyActIMenu() { Apply(Preset.RoadMoody); }

    public static void Apply(Preset preset, bool showDialog = true)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "LoreTrailer");

        string path = $"{Dir}/TrailerPostFX_{preset}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }

        BuildProfile(profile, preset);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Scene volume (global, high priority so it wins over the gameplay one).
        var go = GameObject.Find("Trailer_PostFX");
        if (go == null)
        {
            go = new GameObject("Trailer_PostFX");
            Undo.RegisterCreatedObjectUndo(go, "Create Trailer PostFX");
        }
        var vol = go.GetComponent<Volume>();
        if (vol == null) vol = Undo.AddComponent<Volume>(go);
        vol.isGlobal = true;
        vol.priority = 100f;
        vol.sharedProfile = profile;
        EditorUtility.SetDirty(vol);

        // Post-processing must be enabled on the output (Main) camera.
        EnableCameraPostFX();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        if (!showDialog) return;
        EditorUtility.DisplayDialog("Cinematic Post FX",
            $"Applied '{preset}' grade:\n" +
            "  • Filmic tonemap + color grade\n" +
            "  • Motion blur (sells the gallop speed)\n" +
            "  • Bloom, vignette, film grain, subtle chromatic aberration\n\n" +
            "A high-priority 'Trailer_PostFX' volume was added to this scene and\n" +
            "post-processing was enabled on the Main Camera.\n\n" +
            "Tweak values on the profile at " + path + ".", "OK");
    }

    private static void BuildProfile(VolumeProfile profile, Preset preset)
    {
        // Shared filmic base
        var tone = GetOrAdd<Tonemapping>(profile);
        tone.mode.overrideState = true; tone.mode.value = TonemappingMode.Neutral;

        var bloom = GetOrAdd<Bloom>(profile);
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f;
        bloom.threshold.overrideState = true; bloom.threshold.value = 1.15f;
        bloom.scatter.overrideState = true; bloom.scatter.value = 0.6f;

        var vig = GetOrAdd<Vignette>(profile);
        vig.intensity.overrideState = true; vig.intensity.value = 0.14f;
        vig.smoothness.overrideState = true; vig.smoothness.value = 0.65f;

        // Grain + chromatic aberration kept VERY subtle — the earlier heavy grain
        // + desaturation is what made it read like a black-and-white TV.
        var grain = GetOrAdd<FilmGrain>(profile);
        grain.type.overrideState = true; grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.overrideState = true; grain.intensity.value = 0.05f;
        grain.response.overrideState = true; grain.response.value = 0.7f;

        var ca = GetOrAdd<ChromaticAberration>(profile);
        ca.intensity.overrideState = true; ca.intensity.value = 0.02f;

        var mb = GetOrAdd<MotionBlur>(profile);
        mb.mode.overrideState = true; mb.mode.value = MotionBlurMode.CameraOnly;
        mb.quality.overrideState = true; mb.quality.value = MotionBlurQuality.High;
        mb.intensity.overrideState = true; mb.intensity.value = 0.25f;   // was harsh on close cams
        mb.clamp.overrideState = true; mb.clamp.value = 0.05f;

        // Depth of field is added but OFF by default — TrailerDoFFocus turns it on
        // ONLY during the close shots and auto-focuses on the horse, so the wide
        // reveal stays fully sharp (everything visible). Moderate aperture keeps
        // the background softened, not obliterated.
        var dof = GetOrAdd<DepthOfField>(profile);
        dof.active = false;
        dof.mode.overrideState = true; dof.mode.value = DepthOfFieldMode.Bokeh;
        dof.focusDistance.overrideState = true; dof.focusDistance.value = 8f;
        dof.focalLength.overrideState = true; dof.focalLength.value = 50f;
        dof.aperture.overrideState = true; dof.aperture.value = 5.6f;

        // Per-preset color grade
        var col = GetOrAdd<ColorAdjustments>(profile);
        col.postExposure.overrideState = true;
        col.contrast.overrideState = true;
        col.colorFilter.overrideState = true;
        col.saturation.overrideState = true;
        col.hueShift.overrideState = true;
        col.hueShift.value = 0f;

        // NOTE: saturation stays >= 0 — a moody grade should be RICH, not grey.
        // exposure, contrast, saturation, colorFilter
        switch (preset)
        {
            case Preset.RoadMoody:   Grade(col, 0.0f, 4f, 6f, new Color(0.97f, 0.99f, 1.02f)); break;    // gentle, cool but natural
            case Preset.CampWarm:    Grade(col, 0.05f, 8f, 10f, new Color(1.00f, 0.94f, 0.84f)); break;  // golden hearth
            case Preset.Summer:      Grade(col, 0.08f, 6f, 14f, new Color(1.00f, 0.99f, 0.92f)); break;  // lush warm
            case Preset.Autumn:      Grade(col, 0.02f, 9f, 10f, new Color(1.00f, 0.90f, 0.74f)); break;  // amber
            case Preset.Winter:      Grade(col, 0.05f, 10f, -6f, new Color(0.90f, 0.95f, 1.05f)); break; // pale cold
            case Preset.Corruption:  Grade(col, -0.1f, 12f, 4f, new Color(0.94f, 0.86f, 1.05f)); break;  // sickly purple
            case Preset.Throne:      Grade(col, -0.15f, 12f, 0f, new Color(0.86f, 0.90f, 1.05f)); break; // cold dread
        }
    }

    private static void Grade(ColorAdjustments col, float exposure, float contrast, float saturation, Color filter)
    {
        col.postExposure.value = exposure;
        col.contrast.value = contrast;
        col.saturation.value = saturation;
        col.colorFilter.value = filter;
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing)) return existing;
        var comp = profile.Add<T>(false);
        comp.name = typeof(T).Name;
        if (!AssetDatabase.Contains(comp)) AssetDatabase.AddObjectToAsset(comp, profile);
        return comp;
    }

    private static void EnableCameraPostFX()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            Undo.RecordObject(data, "enable post fx");
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
        }
    }
}
