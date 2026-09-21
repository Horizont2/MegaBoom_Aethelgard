using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The last beat: the map of Aethelgard, and the curse taking all of it.
//
// ==== WHY THIS DOES NOT USE THE MAP CANVAS ====
//
// The obvious way to show the map is to bring in CampScene's MapCanvas. That
// canvas is not a map — it is a map SCREEN. It arrives with MapPanelUI,
// MapProgressionManager, twenty-four RegionUI components each running their own
// Update, twenty-four StormLayers, twenty-four labels, the dragger, the
// interactive viewer and every castle marker, and the first thing anyone wants
// from it in a trailer is all of that switched off. The brief for this shot
// literally begins with "turn off the storm layers and the castle icons".
//
// So it does not bring the screen, it brings the MAP: one sprite, on a plane,
// in the world. It is clean by construction rather than by cleanup — there are
// no storm layers or icons to disable because none of that machinery came
// along, nothing can be dragged or clicked mid-shot, and no region's pulsing
// fog animation is running behind the take.
//
// It also has to be a world plane rather than a screen-space canvas for a much
// simpler reason: a camera cannot push INTO a screen-space canvas. That canvas
// is drawn over everything at a fixed size, forever. A plane standing in the
// world is something a lens can approach, and approaching it is the shot.
[DisallowMultipleComponent]
public class TrailerMapCurse : MonoBehaviour
{
    // One piece of the map: a sprite with the rectangle it occupied on the map
    // background. Twenty-five of them rebuild the whole thing - the land, and
    // the twenty-four region shapes that give it its detail - without any of
    // the machinery that drives them on the live map screen.
    [System.Serializable]
    public class MapPiece
    {
        public string label;
        public Sprite sprite;
        public Vector2 anchoredPosition;
        public Vector2 size;
        public Color color = Color.white;
    }

    [Header("The map")]
    [Tooltip("The background first, then the region shapes. Baked from the live map so the layout matches exactly.")]
    public MapPiece[] mapPieces;
    [Tooltip("Size of the rect the pieces are laid out in. Must match the map background they were measured against, or nothing lines up.")]
    public Vector2 mapCanvasSize = new Vector2(1800f, 1783.6f);
    [Tooltip("How wide the map stands in the world, in metres. Only its ratio to the distances below matters.")]
    public float mapWorldWidth = 34f;
    [Tooltip("Opacity of the region shapes. On the live map they sit at zero and fade in as regions are taken; here they are the detail that makes it a map rather than a picture of parchment.")]
    [Range(0f, 1f)] public float shapeAlpha = 0.85f;

    [Header("Vignette")]
    public bool useVignette = true;
    [Range(0f, 1f)] public float vignetteIntensity = 0.45f;
    [Range(0.01f, 1f)] public float vignetteSmoothness = 0.4f;
    public Color vignetteColor = Color.black;

    [Header("The approach")]
    [Tooltip("Where the lens starts. Far enough that the map reads as an object in front of you rather than as a screen.")]
    public float startDistance = 48f;
    [Tooltip("Where it ends. Inside the map's own width at this lens, so it fills the frame.")]
    public float endDistance = 19f;
    public float approachSeconds = 3.4f;
    public float fieldOfView = 40f;

    [Header("The curse - the fog rolls in")]
    // ==== THE PROJECT ALREADY HAD THE RIGHT SHADER ====
    //
    // Assets/MapUI/SG_FlowingFog.shadergraph, with MAT_FlowingFog on top of it,
    // is the fog the map screen has always used for unexplored ground. Growing
    // tendrils across the map with LineRenderers was inventing a second visual
    // language for the same idea, in a trailer whose whole job is to look like
    // the game.
    //
    // So the curse is that fog, as one sheet, larger than the map, flying in
    // from beyond the top-left corner until it has covered everything. Three
    // layers at different speeds and scales rather than one, because a single
    // flat sheet sliding across reads as a sheet sliding across; three moving
    // at different rates reads as weather.
    [Tooltip("MAT_FlowingFog - the material the map screen already uses for unexplored ground.")]
    public Material fogMaterial;
    [Tooltip("FogOfWar.png, or whichever of the storm sheets you prefer.")]
    public Sprite fogSprite;
    [Tooltip("Seconds from the first wisp entering frame to the map being covered.")]
    public float fogSweepSeconds = 4.6f;
    public float holdBeforeCurse = 0.6f;
    [Tooltip("Direction it arrives from, in map space. (-1, 1) is beyond the top-left corner.")]
    public Vector2 fogFrom = new Vector2(-1f, 1f);
    [Tooltip("How much wider than the map each sheet is. It has to overshoot or its own edge crosses the frame.")]
    [Range(1.2f, 4f)] public float fogOversize = 2.2f;
    [Tooltip("Layers. Each is slower, larger and fainter than the one in front of it.")]
    [Range(1, 5)] public int fogLayers = 3;
    public Color fogTint = new Color(0.55f, 0.42f, 0.68f, 1f);
    [Range(0f, 1f)] public float fogOpacity = 0.92f;

    [Tooltip("What the land turns into underneath. The map does not go black - a map you cannot read says nothing.")]
    public Color cursedTint = new Color(0.46f, 0.36f, 0.44f, 1f);
    [Tooltip("Seconds held on the covered map before the episode ends.")]
    public float holdAfter = 1.6f;

    [Header("Where it is filmed")]
    // ==== THE MAP CANNOT BE FILMED WHERE THE MARCH IS ====
    //
    // The first version built the plane in front of wherever the camera was
    // standing - which, at the end of the march, is in the middle of three
    // hundred skeletons, on a night field, inside a volumetric fog volume that
    // is thirty-four metres tall and deliberately thick enough to swallow
    // ranks at fifty metres. The map came out buried: fogged, dark, with bodies
    // in front of it.
    //
    // None of that is fixable by moving the plane a bit further forward. It is
    // a different shot and it needs a different place: the whole beat is staged
    // far above the terrain, outside the fog volume and away from everything
    // else in the scene, with the camera clearing to flat colour so not one
    // pixel of the march can appear behind it. A void, in other words, which is
    // exactly what a map floating in front of you should be standing in.
    [Tooltip("Where the beat is staged, in world space. Well above the terrain and outside the fog volume - nothing else is up there.")]
    public Vector3 stagePosition = new Vector3(0f, 4000f, 0f);
    [Tooltip("What the lens clears to behind the map. Flat, so nothing of the scene can show through.")]
    public Color voidColor = new Color(0.02f, 0.02f, 0.03f, 1f);
    [Tooltip("Switch the volumetric fog off for the beat. The stage is outside its volume anyway; this is the belt to that pair of braces.")]
    public bool disableVolumetricFog = true;
    [Tooltip("Slow motes drifting between the lens and the map. A flat void is what makes a floating object look cheap - the map has to be standing IN something, even if that something is only dust catching the light.")]
    public GameObject voidMotesPrefab;
    [Range(0.1f, 6f)] public float voidMotesScale = 2.4f;

    [Header("Audio")]
    public bool playAudio = true;

    private RectTransform mapRect;
    private Image mapImage;
    private Canvas canvas;
    private readonly List<GameObject> spawned = new List<GameObject>();

    // Everything borrowed from the camera for the beat, put back in Cleanup.
    private CameraClearFlags prevClear;
    private Color prevBackground;
    private float prevFov;
    private Camera cachedCam;
    private Behaviour fogComponent;
    private readonly List<ParticleSystem> pausedOnCamera = new List<ParticleSystem>();

    private readonly List<Image> shapeImages = new List<Image>(24);
    private readonly List<Image> fogSheets = new List<Image>(4);
    private GameObject vignetteVolume;

    // Built and framed while the screen is still white, so the shot opens on a
    // map that is already there rather than on one arriving.
    public bool Prepare(Transform cam)
    {
        if (cam == null) return false;
        if (mapPieces == null || mapPieces.Length == 0)
        {
            Debug.LogWarning("[TrailerMapCurse] No map pieces assigned, so the closing beat is skipped.", this);
            return false;
        }

        var go = new GameObject("[TrailerMap]");
        go.transform.SetParent(transform, false);

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        mapRect = canvas.GetComponent<RectTransform>();
        mapRect.sizeDelta = mapCanvasSize;
        float scale = mapWorldWidth / Mathf.Max(1f, mapCanvasSize.x);
        mapRect.localScale = new Vector3(scale, scale, scale);

        // The land, then every region shape on top of it, each at the rectangle
        // it occupies on the real map. The first piece is the background and
        // keeps its own colour; the rest are detail and share one opacity.
        shapeImages.Clear();
        for (int i = 0; i < mapPieces.Length; i++)
        {
            var piece = mapPieces[i];
            if (piece == null || piece.sprite == null) continue;

            var pgo = new GameObject(string.IsNullOrEmpty(piece.label) ? "Piece" : piece.label, typeof(RectTransform));
            var prt = pgo.GetComponent<RectTransform>();
            prt.SetParent(mapRect, false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = piece.anchoredPosition;
            prt.sizeDelta = piece.size;
            prt.localPosition = new Vector3(prt.localPosition.x, prt.localPosition.y, -i * 0.01f);

            var img = pgo.AddComponent<Image>();
            img.sprite = piece.sprite;
            img.raycastTarget = false;
            img.preserveAspect = false;

            if (i == 0)
            {
                mapImage = img;
                img.color = piece.color;
            }
            else
            {
                Color c = piece.color;
                c.a = shapeAlpha;
                img.color = c;
                shapeImages.Add(img);
            }
        }

        if (mapImage == null)
        {
            Debug.LogWarning("[TrailerMapCurse] The first map piece has no sprite - it is meant to be the map " +
                             "background.", this);
        }

        // Staged in the void, square on to the lens, and LEVEL. There is no
        // settling roll any more: a map that rotates into alignment as the shot
        // opens reads as a card being turned over, which is the one thing a map
        // that has always been there must not do.
        Vector3 forward = Vector3.forward;
        mapRect.position = stagePosition;
        mapRect.rotation = Quaternion.LookRotation(forward, Vector3.up);

        cam.position = stagePosition - forward * startDistance;
        cam.rotation = Quaternion.LookRotation(forward, Vector3.up);

        cachedCam = cam.GetComponent<Camera>();
        if (cachedCam != null)
        {
            prevFov = cachedCam.fieldOfView;
            prevClear = cachedCam.clearFlags;
            prevBackground = cachedCam.backgroundColor;

            cachedCam.fieldOfView = fieldOfView;
            cachedCam.clearFlags = CameraClearFlags.SolidColor;
            cachedCam.backgroundColor = voidColor;
        }

        BuildVignette();

        // The rain is parented to the camera, so without this it follows the
        // lens into the void and rains on the map.
        pausedOnCamera.Clear();
        foreach (var ps in cam.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null || !ps.gameObject.activeSelf) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            pausedOnCamera.Add(ps);
        }

        // Found by name so this compiles with or without the fog package.
        if (disableVolumetricFog)
        {
            System.Type ft = System.Type.GetType("BKPureNature.PureVolumetricFog, BKPureNature.PureVolumetricFog");
            if (ft != null)
            {
                fogComponent = cam.GetComponent(ft) as Behaviour;
                if (fogComponent == null) fogComponent = FindFirstObjectByType(ft) as Behaviour;
                if (fogComponent != null) fogComponent.enabled = false;
            }
        }

        return true;
    }

    // Its own volume rather than the scene's, so nothing that was authored on
    // the Global Volume is touched and there is nothing to put back beyond
    // destroying this.
    private void BuildVignette()
    {
        if (!useVignette) return;

        vignetteVolume = new GameObject("[TrailerMapVignette]");
        vignetteVolume.transform.SetParent(transform, false);

        var v = vignetteVolume.AddComponent<UnityEngine.Rendering.Volume>();
        v.isGlobal = true;
        v.priority = 100f;   // over whatever the scene already has

        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        v.profile = profile;

        var vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vig.intensity.overrideState = true;
        vig.intensity.value = vignetteIntensity;
        vig.smoothness.overrideState = true;
        vig.smoothness.value = vignetteSmoothness;
        vig.color.overrideState = true;
        vig.color.value = vignetteColor;
    }

    public IEnumerator Run(Transform cam)
    {
        if (mapRect == null || cam == null) yield break;

        Vector3 dir = (mapRect.position - cam.position).normalized;
        Vector3 from = mapRect.position - dir * startDistance;
        Vector3 to = mapRect.position - dir * endDistance;

        // A small lateral arc on the way in. A lens travelling dead straight
        // down its own axis produces no parallax, and no parallax is most of
        // what makes a push-in read as an image being scaled rather than as a
        // camera moving through a space. It is a DRIFT, not a rotation - the
        // map never turns to face anything, because it was already facing us.
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float arc = mapWorldWidth * 0.07f;

        Cue(AudioID.Trailer_Dread, AudioID.Enemy_Telegraph);
        SpawnMotes(cam);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, approachSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            cam.position = Vector3.LerpUnclamped(from, to, k) + side * (Mathf.Sin(k * Mathf.PI) * arc);
            cam.rotation = Quaternion.LookRotation((mapRect.position - cam.position).normalized, Vector3.up);
            yield return null;
        }
        cam.position = to;
        cam.rotation = Quaternion.LookRotation((mapRect.position - cam.position).normalized, Vector3.up);

        float wait = 0f;
        while (wait < holdBeforeCurse) { wait += Time.unscaledDeltaTime; yield return null; }

        yield return StartCoroutine(RollTheFogIn());

        float after = 0f;
        while (after < holdAfter) { after += Time.unscaledDeltaTime; yield return null; }
    }

    private IEnumerator RollTheFogIn()
    {
        Cue(AudioID.Trailer_RiserToStrike, AudioID.Enemy_Telegraph);

        Vector2 sheet = mapCanvasSize * fogOversize;
        Vector2 enter = fogFrom.sqrMagnitude < 0.001f ? new Vector2(-1f, 1f) : fogFrom.normalized;

        // Far enough out that not one pixel of a sheet is in frame at the start,
        // whatever its size and whatever the lens is doing.
        float travel = sheet.magnitude;

        fogSheets.Clear();
        var starts = new List<Vector2>();
        for (int i = 0; i < fogLayers; i++)
        {
            float depth = i / Mathf.Max(1f, fogLayers - 1f);   // 0 = nearest

            var go = new GameObject("Fog_" + i, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(mapRect, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            // Each layer a little larger than the one in front, so their edges
            // never cross the frame together.
            rt.sizeDelta = sheet * (1f + depth * 0.35f);
            // In front of every map piece.
            rt.localPosition = new Vector3(0f, 0f, -2f - i * 0.05f);

            var img = go.AddComponent<Image>();
            img.sprite = fogSprite;
            img.raycastTarget = false;
            if (fogMaterial != null) img.material = fogMaterial;

            Color c = fogTint;
            // The layers behind are fainter, which is what gives the bank depth
            // instead of making it one opaque wall.
            c.a = 0f;
            img.color = c;

            Vector2 start = -enter * travel * (1f + depth * 0.25f);
            rt.anchoredPosition = start;
            starts.Add(start);
            fogSheets.Add(img);
        }

        Color cleanMap = mapImage != null ? mapImage.color : Color.white;
        var cleanShapes = new List<Color>(shapeImages.Count);
        for (int i = 0; i < shapeImages.Count; i++) cleanShapes.Add(shapeImages[i].color);

        float elapsed = 0f;
        while (elapsed < fogSweepSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / fogSweepSeconds);

            for (int i = 0; i < fogSheets.Count; i++)
            {
                float depth = i / Mathf.Max(1f, fogLayers - 1f);
                // Layers behind move slower and arrive later, so the bank rolls
                // rather than slides.
                float lag = depth * 0.22f;
                float kk = Mathf.Clamp01((k - lag) / Mathf.Max(0.05f, 1f - lag));
                float eased = Mathf.SmoothStep(0f, 1f, kk);

                var rt = fogSheets[i].rectTransform;
                rt.anchoredPosition = Vector2.LerpUnclamped(starts[i], Vector2.zero, eased);

                Color c = fogTint;
                c.a = fogOpacity * (1f - depth * 0.45f) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(kk * 2f));
                fogSheets[i].color = c;
            }

            // The land goes over underneath the bank, so what is still visible
            // through the gaps is already cursed.
            float tint = Mathf.SmoothStep(0f, 1f, k);
            if (mapImage != null) mapImage.color = Color.Lerp(cleanMap, cursedTint, tint);
            for (int i = 0; i < shapeImages.Count; i++)
            {
                Color target = cursedTint; target.a = cleanShapes[i].a;
                shapeImages[i].color = Color.Lerp(cleanShapes[i], target, tint);
            }

            yield return null;
        }

        if (mapImage != null) mapImage.color = cursedTint;
    }

    private void SpawnMotes(Transform cam)
    {
        if (voidMotesPrefab == null || cam == null) return;
        var motes = Instantiate(voidMotesPrefab, cam.position + cam.forward * (endDistance * 0.55f), Quaternion.identity, transform);
        motes.transform.localScale = Vector3.one * Mathf.Max(0.01f, voidMotesScale);
        foreach (var ps in motes.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;
        }
        spawned.Add(motes);
    }

    public void Cleanup()
    {
        for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();
        shapeImages.Clear();
        fogSheets.Clear();
        if (vignetteVolume != null) { Destroy(vignetteVolume); vignetteVolume = null; }
        if (canvas != null) Destroy(canvas.gameObject);
        canvas = null;
        mapRect = null;
        mapImage = null;

        // Hand the camera back exactly as it was borrowed. The episode may not
        // be the last thing this scene ever plays.
        if (cachedCam != null)
        {
            cachedCam.fieldOfView = prevFov;
            cachedCam.clearFlags = prevClear;
            cachedCam.backgroundColor = prevBackground;
            cachedCam = null;
        }
        for (int i = 0; i < pausedOnCamera.Count; i++)
            if (pausedOnCamera[i] != null) pausedOnCamera[i].gameObject.SetActive(true);
        pausedOnCamera.Clear();

        if (fogComponent != null) { fogComponent.enabled = true; fogComponent = null; }
    }

    private void Cue(string primary, string fallback)
    {
        if (!playAudio || AudioManager.Instance == null) return;
        if (!string.IsNullOrEmpty(primary) && AudioManager.Instance.HasEvent(primary))
        {
            AudioManager.Instance.PlaySFX(primary);
            return;
        }
        if (!string.IsNullOrEmpty(fallback) && AudioManager.Instance.HasEvent(fallback))
            AudioManager.Instance.PlaySFX(fallback);
    }
}
