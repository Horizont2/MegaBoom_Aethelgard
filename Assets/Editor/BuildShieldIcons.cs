using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Renders a shop icon for every shield and drops it onto the shield's own
// WeaponData, so the list stops showing four identical blanks.
//
// ==== WHY PreviewRenderUtility AND NOT A CAMERA IN THE SCENE ====
//
// The obvious version of this tool — spawn the prefab somewhere far away, point
// a temporary Camera at it, call Render(), read the RenderTexture back — is the
// version that quietly produces four transparent PNGs under the Universal
// pipeline. A manual Camera.Render() outside the SRP's own loop is not
// something URP promises anything about, and in the editor it frequently
// returns an empty target.
//
// PreviewRenderUtility is the API Unity's own inspectors use to draw the little
// rotating model in an asset preview. It owns a private scene, its own camera
// and its own lights, it goes through whichever render pipeline is active, and
// BeginStaticPreview/EndStaticPreview hands back a finished Texture2D. It is
// also the only one of the two that cannot disturb the scene the user has open.
public static class BuildShieldIcons
{
    private const string IconFolder = "Assets/ShopIcons/Shields";
    private const int IconSize = 256;

    // A three-quarter view. Straight-on reads as a flat disc for a round shield
    // and hides the boss and the rim entirely; this angle is what makes four
    // shields look like four different objects in a list.
    private static readonly Vector3 ViewEuler = new Vector3(14f, 152f, 0f);

    [MenuItem("Tools/Combat/Generate Shield Icons")]
    public static void Generate()
    {
        List<WeaponData> shields = FindShields();
        if (shields.Count == 0)
        {
            EditorUtility.DisplayDialog("Shield icons",
                "No WeaponData assets with category Shield were found. Run Tools > Combat > Build Shield Set first.", "OK");
            return;
        }

        Directory.CreateDirectory(IconFolder);

        int made = 0, skipped = 0;
        var report = new System.Text.StringBuilder();

        try
        {
            for (int i = 0; i < shields.Count; i++)
            {
                WeaponData s = shields[i];
                EditorUtility.DisplayProgressBar("Shield icons", s.weaponName, (i + 1) / (float)shields.Count);

                GameObject model = s.shopPrefab != null ? s.shopPrefab : s.inGamePrefab;
                if (model == null)
                {
                    report.AppendLine($"  {s.name}: no shopPrefab or inGamePrefab — skipped.");
                    skipped++;
                    continue;
                }

                Texture2D shot = RenderIcon(model);
                if (shot == null)
                {
                    report.AppendLine($"  {s.name}: the preview came back empty — skipped.");
                    skipped++;
                    continue;
                }

                string path = $"{IconFolder}/{SanitiseFileName(s.name)}.png";
                File.WriteAllBytes(path, shot.EncodeToPNG());
                Object.DestroyImmediate(shot);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                ConfigureAsSprite(path);

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    report.AppendLine($"  {s.name}: wrote {path} but it did not import as a Sprite — check the importer.");
                    skipped++;
                    continue;
                }

                s.icon = sprite;
                EditorUtility.SetDirty(s);
                report.AppendLine($"  {s.name}: {path}");
                made++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Shield icons] {made} rendered, {skipped} skipped.\n{report}");
        EditorUtility.DisplayDialog("Shield icons",
            $"{made} icon(s) rendered and assigned.\n{(skipped > 0 ? skipped + " skipped — see the Console." : "")}", "OK");
    }

    private static List<WeaponData> FindShields()
    {
        var list = new List<WeaponData>();
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponData"))
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
            if (w != null && w.category == ItemCategory.Shield) list.Add(w);
        }
        // Cheapest first, so the console report reads in the same order as the
        // shop list does.
        list.Sort((a, b) => a.price.CompareTo(b.price));
        return list;
    }

    private static Texture2D RenderPass(PreviewRenderUtility pru, Color background)
    {
        pru.camera.backgroundColor = background;
        pru.BeginStaticPreview(new Rect(0f, 0f, IconSize, IconSize));
        pru.camera.Render();
        return pru.EndStaticPreview();
    }

    // alpha = 1 - (white - black) per channel, taking the strongest channel so a
    // saturated edge does not go see-through. Colour is the black pass divided
    // back out by that alpha, because the black pass is the colour already
    // multiplied by it.
    private static Texture2D Key(Texture2D onBlack, Texture2D onWhite)
    {
        Color[] b = onBlack.GetPixels();
        Color[] w = onWhite.GetPixels();
        if (b.Length != w.Length) return null;

        var outPx = new Color[b.Length];
        int solid = 0;

        for (int i = 0; i < b.Length; i++)
        {
            float a = 1f - Mathf.Max(Mathf.Max(w[i].r - b[i].r, w[i].g - b[i].g), w[i].b - b[i].b);
            a = Mathf.Clamp01(a);
            if (a > 0.02f) solid++;

            Color c = a > 0.001f ? new Color(b[i].r / a, b[i].g / a, b[i].b / a) : Color.clear;
            outPx[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
        }

        // Nothing was drawn. Writing this out is how a shield ends up with a
        // black square for an icon and nobody finds out until it is on screen.
        if (solid < b.Length / 400) return null;

        var tex = new Texture2D(onBlack.width, onBlack.height, TextureFormat.RGBA32, false);
        tex.SetPixels(outPx);
        tex.Apply(false);
        return tex;
    }

    private static Texture2D RenderIcon(GameObject prefab)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            GameObject inst = Object.Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;

            // Everything on a shield prefab that is not geometry is noise here —
            // a stray particle system or trail would render into the icon, and a
            // script's Awake has no business running during an editor preview.
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true)) if (ps != null) ps.gameObject.SetActive(false);
            foreach (var tr in inst.GetComponentsInChildren<TrailRenderer>(true)) if (tr != null) tr.enabled = false;

            if (!TryGetBounds(inst, out Bounds bounds))
            {
                Object.DestroyImmediate(inst);
                return null;
            }

            // Centre the model on the origin so the camera framing below does not
            // have to care where the prefab's pivot happens to be.
            inst.transform.position = -bounds.center;
            inst.transform.rotation = Quaternion.Euler(ViewEuler);
            TryGetBounds(inst, out bounds);

            pru.AddSingleGO(inst);

            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);

            pru.camera.transform.position = new Vector3(0f, 0f, -radius * 4f);
            pru.camera.transform.rotation = Quaternion.identity;
            pru.camera.orthographic = true;
            // A little air around the silhouette. Framed exactly to the bounds a
            // round shield touches all four edges and reads as cropped.
            pru.camera.orthographicSize = radius * 1.12f;
            pru.camera.nearClipPlane = 0.01f;
            pru.camera.farClipPlane = radius * 40f;
            pru.camera.clearFlags = CameraClearFlags.SolidColor;

            // Key from the upper left, a dim cool fill opposite it, so the rim
            // and the boss both read instead of the whole face going flat.
            pru.lights[0].intensity = 1.35f;
            pru.lights[0].transform.rotation = Quaternion.Euler(28f, 132f, 0f);
            pru.lights[0].color = new Color(1f, 0.97f, 0.9f);
            if (pru.lights.Length > 1)
            {
                pru.lights[1].intensity = 0.5f;
                pru.lights[1].transform.rotation = Quaternion.Euler(-14f, -48f, 0f);
                pru.lights[1].color = new Color(0.7f, 0.8f, 1f);
            }
            pru.ambientColor = new Color(0.32f, 0.33f, 0.36f, 0f);

            // ==== ENDSTATICPREVIEW HAS NO ALPHA, SO IT IS KEYED OUT ====
            //
            // Whatever the camera's background alpha is set to, the texture that
            // comes back is OPAQUE — the four icons this tool wrote were plain
            // RGB, which is why a shield in the list is a square with a
            // background rather than a shield.
            //
            // So it is rendered twice, once on black and once on white. Anything
            // solid looks the same in both; anything transparent differs by
            // exactly how transparent it is. That gives the alpha, and dividing
            // the black pass by it gives back the unmultiplied colour. It is the
            // oldest trick in compositing and it needs nothing from the pipeline.
            Texture2D onBlack = RenderPass(pru, Color.black);
            Texture2D onWhite = RenderPass(pru, Color.white);
            Object.DestroyImmediate(inst);

            if (onBlack == null || onWhite == null) return null;
            Texture2D tex = Key(onBlack, onWhite);
            Object.DestroyImmediate(onBlack);
            Object.DestroyImmediate(onWhite);
            return tex;
        }
        finally
        {
            pru.Cleanup();
        }
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static void ConfigureAsSprite(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        // A shop icon is a small, flat, high-contrast image — block compression
        // puts visible mush in exactly the thin rim lines that tell one shield
        // from another, for a few kilobytes.
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }

    private static string SanitiseFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
