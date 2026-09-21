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
    [Header("The map")]
    [Tooltip("Assets/MapUI/Map_Background.png — the same art the map table uses, without the screen around it.")]
    public Sprite mapSprite;
    [Tooltip("How wide the map stands in the world, in metres. Only the ratio to the distances below matters.")]
    public float mapWorldWidth = 30f;

    [Header("The approach")]
    [Tooltip("Where the lens starts. Far enough that the map reads as an object in front of you rather than as a screen.")]
    public float startDistance = 46f;
    [Tooltip("Where it ends. At a 40 degree lens this is inside the map's own width, so it fills the frame.")]
    public float endDistance = 17f;
    public float approachSeconds = 3.2f;
    [Tooltip("Degrees of roll the lens still carries from the impact, easing out to level as it settles.")]
    public float settleRoll = 7f;
    public float fieldOfView = 40f;

    [Header("The curse")]
    [Tooltip("Where it starts, in map space. (0,0) is the bottom-left corner of the art, (1,1) the top-right.")]
    public Vector2 curseOrigin = new Vector2(0.62f, 0.56f);
    public float holdBeforeCurse = 0.5f;
    public float curseSeconds = 4.2f;
    [Tooltip("Dark smoke that crawls outward. Assets/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/Smoke ground.prefab is in the project and on a URP particle shader.")]
    public GameObject curseSmokePrefab;
    [Tooltip("How many emitters are walked outward. They are placed on a growing ring, not spawned at random, so the spread has a front rather than a sprinkle.")]
    [Range(4, 48)] public int curseEmitters = 18;
    [Range(0.05f, 3f)] public float curseSmokeScale = 0.9f;
    [Tooltip("What the land turns into. The map does not go black — a map you cannot read says nothing.")]
    public Color cursedTint = new Color(0.34f, 0.24f, 0.32f, 1f);
    [Tooltip("Seconds held on the fully cursed map before the episode ends.")]
    public float holdAfter = 1.4f;

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

    // Built and framed while the screen is still black, so the shot opens on a
    // map that is already there rather than on one arriving.
    public bool Prepare(Transform cam)
    {
        if (cam == null) return false;
        if (mapSprite == null)
        {
            Debug.LogWarning("[TrailerMapCurse] No map sprite assigned, so the closing beat is skipped.", this);
            return false;
        }

        var go = new GameObject("[TrailerMap]");
        go.transform.SetParent(transform, false);

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        mapRect = canvas.GetComponent<RectTransform>();

        // Sized from the sprite's own aspect, so swapping the art does not need
        // the numbers here touched.
        float aspect = mapSprite.rect.height / Mathf.Max(1f, mapSprite.rect.width);
        mapRect.sizeDelta = new Vector2(1000f, 1000f * aspect);
        float scale = mapWorldWidth / 1000f;
        mapRect.localScale = new Vector3(scale, scale, scale);

        var imgGo = new GameObject("Map", typeof(RectTransform));
        imgGo.transform.SetParent(mapRect, false);
        mapImage = imgGo.AddComponent<Image>();
        mapImage.sprite = mapSprite;
        mapImage.raycastTarget = false;
        mapImage.color = Color.white;
        var irt = mapImage.rectTransform;
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        // Staged in the void, at a fixed orientation. Deliberately independent
        // of where the camera was standing when the axe landed - that position
        // is in the middle of the legion and has nothing to offer this shot.
        Vector3 forward = Vector3.forward;
        mapRect.position = stagePosition;
        mapRect.rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Square on to the lens. A map seen at an angle is a table; a map seen
        // square is a statement. The roll is the only thing left over from
        // being hit, and it bleeds out over the approach.
        cam.position = stagePosition - forward * startDistance;
        cam.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, settleRoll);

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

    public IEnumerator Run(Transform cam)
    {
        if (mapRect == null || cam == null) yield break;

        Vector3 toMap = mapRect.position - cam.position;
        Vector3 dir = toMap.normalized;
        Vector3 from = mapRect.position - dir * startDistance;
        Vector3 to = mapRect.position - dir * endDistance;
        Quaternion level = Quaternion.LookRotation(dir, Vector3.up);

        Cue(AudioID.Trailer_Dread, AudioID.Enemy_Telegraph);

        // APPROACH. Eased at both ends: a lens that arrives at constant speed
        // and stops dead reads as a dolly rail, which is the one thing a
        // portentous shot must not look like.
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, approachSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            cam.position = Vector3.LerpUnclamped(from, to, k);
            // The roll bleeds out as it settles, so the last thing the impact
            // did to the camera is still visible for a moment and then is not.
            cam.rotation = level * Quaternion.Euler(0f, 0f, Mathf.Lerp(settleRoll, 0f, k));
            yield return null;
        }
        cam.position = to;
        cam.rotation = level;

        float wait = 0f;
        while (wait < holdBeforeCurse) { wait += Time.unscaledDeltaTime; yield return null; }

        // THE CURSE. Emitters are walked outward along a growing ring rather
        // than scattered at random, so the spread has a FRONT - an edge the eye
        // can follow across the land - instead of looking like smoke being
        // sprinkled over a picture.
        Cue(AudioID.Trailer_RiserToStrike, AudioID.Enemy_Telegraph);

        Vector2 half = mapRect.sizeDelta * 0.5f;
        Vector3 originLocal = new Vector3((curseOrigin.x - 0.5f) * mapRect.sizeDelta.x,
                                          (curseOrigin.y - 0.5f) * mapRect.sizeDelta.y, 0f);
        float maxRadius = Mathf.Sqrt(half.x * half.x + half.y * half.y);

        int placed = 0;
        Color clean = Color.white;

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, curseSeconds);
            float k = Mathf.Clamp01(t);

            // Place the next emitters as the front passes their radius. Squared
            // so the ring slows as it widens, the way something spreading over
            // a surface does rather than something being inflated.
            int want = Mathf.RoundToInt(curseEmitters * k);
            while (placed < want && placed < curseEmitters)
            {
                float frac = (placed + 1) / (float)curseEmitters;
                float radius = Mathf.Sqrt(frac) * maxRadius;
                // Golden angle, so successive emitters never line up into a
                // spiral the eye can pick out.
                float ang = placed * 2.39996f;
                Vector3 local = originLocal + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius;
                SpawnSmoke(mapRect.TransformPoint(local));
                placed++;
            }

            // The land itself goes over, behind the smoke.
            if (mapImage != null) mapImage.color = Color.Lerp(clean, cursedTint, Mathf.SmoothStep(0f, 1f, k));

            yield return null;
        }

        if (mapImage != null) mapImage.color = cursedTint;

        float after = 0f;
        while (after < holdAfter) { after += Time.unscaledDeltaTime; yield return null; }
    }

    private void SpawnSmoke(Vector3 worldPos)
    {
        if (curseSmokePrefab == null) return;

        // Just off the surface, facing out of the map, so the smoke rolls
        // ACROSS the land rather than away from the camera.
        Vector3 pos = worldPos + mapRect.forward * -0.2f;
        var fx = Instantiate(curseSmokePrefab, pos, Quaternion.LookRotation(-mapRect.forward, Vector3.up));
        fx.transform.localScale = Vector3.one * Mathf.Max(0.01f, curseSmokeScale);

        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // Unscaled, because the whole ending runs with the world stopped.
            main.useUnscaledTime = true;
        }

        spawned.Add(fx);
    }

    public void Cleanup()
    {
        for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();
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
