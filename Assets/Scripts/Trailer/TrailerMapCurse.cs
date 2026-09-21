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

    [Header("The curse - it GROWS, it does not spread")]
    // ==== A RING OF SMOKE IS NOT A CURSE, IT IS A DISC ====
    //
    // The first version walked emitters outward along a growing ring. That
    // covers the map evenly and it is worthless: a disc of smoke has no
    // direction, no structure and nothing to follow. Corruption reaching across
    // a land is not a cloud expanding, it is something GROWING through it -
    // roots, veins, frost on a window, lightning in slow motion. All of those
    // are the same shape, and it is the shape the eye is waiting for.
    //
    // So it grows. A handful of roots leave the origin at even angles, each
    // wanders as it advances, each periodically SPLITS into two, and the
    // children are thinner than the parent and split again. Evenly covered
    // because the roots start evenly and the splitting keeps filling the gaps -
    // but arrived at by growth, so there is always a moving tip to watch and
    // always a structure behind it that was not there a second ago.
    [Tooltip("Where it starts, in map space. (0,0) is the bottom-left corner of the art, (1,1) the top-right.")]
    public Vector2 curseOrigin = new Vector2(0.62f, 0.56f);
    public float holdBeforeCurse = 0.5f;
    [Tooltip("Seconds for the growth to reach the edges of the map.")]
    public float curseSeconds = 4.6f;

    [Tooltip("Roots leaving the origin. Spaced evenly around it, which is what makes the coverage even without it looking like a circle.")]
    [Range(2, 12)] public int rootBranches = 5;
    [Tooltip("How far a tip advances per step, as a fraction of the map's width. Smaller is smoother and costs more segments.")]
    [Range(0.004f, 0.05f)] public float stepLength = 0.014f;
    [Tooltip("Degrees a tip may wander per step. Zero grows spokes; too much grows a scribble.")]
    public float wander = 22f;
    [Tooltip("How strongly a tip is pulled back to growing AWAY from the origin. Without it the wander curls branches back on themselves and the growth never reaches the edges.")]
    [Range(0f, 1f)] public float outwardBias = 0.35f;
    [Tooltip("Steps between splits. Each split makes two thinner children.")]
    public int splitEvery = 9;
    [Tooltip("Degrees either side of the parent that children leave at.")]
    public float splitAngle = 26f;
    [Tooltip("How many times a branch may split before its line ends. Each generation is thinner.")]
    [Range(1, 8)] public int maxGenerations = 5;
    [Tooltip("Ceiling on live branches, so a bad combination of the numbers above cannot run away.")]
    [Range(16, 512)] public int maxBranches = 200;

    [Tooltip("Width of a root, in map units (the map is 1000 across).")]
    public float rootWidth = 7f;
    [Tooltip("Fraction of its parent's width each generation keeps.")]
    [Range(0.3f, 0.95f)] public float widthFalloff = 0.68f;
    public Color curseColor = new Color(0.16f, 0.03f, 0.2f, 1f);
    [Tooltip("Colour at the growing tips, so the front of the growth glows and the old growth does not.")]
    public Color curseTipColor = new Color(0.62f, 0.12f, 0.75f, 1f);

    [Tooltip("Optional puff left behind at a split. Sparse on purpose - the growth is the effect, smoke is seasoning.")]
    public GameObject curseSmokePrefab;
    [Range(0.05f, 3f)] public float curseSmokeScale = 0.55f;
    [Range(0f, 1f)] public float smokeOnSplitChance = 0.35f;

    [Tooltip("What the land turns into underneath. The map does not go black - a map you cannot read says nothing.")]
    public Color cursedTint = new Color(0.42f, 0.32f, 0.40f, 1f);
    [Tooltip("Seconds held on the fully cursed map before the episode ends.")]
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

    // One growing tendril. Lines are parented to the map so they travel with
    // it, and grown in LOCAL map space so all the numbers above can be written
    // against the art rather than against the world.
    private class Branch
    {
        public LineRenderer line;
        public List<Vector3> pts = new List<Vector3>(64);
        public Vector2 tip;
        public float angle;
        public int generation;
        public int stepsSinceSplit;
        public bool alive = true;
        public float width;
    }

    private readonly List<Branch> branches = new List<Branch>(64);
    private Material lineMaterial;

    public IEnumerator Run(Transform cam)
    {
        if (mapRect == null || cam == null) yield break;

        Vector3 dir = (mapRect.position - cam.position).normalized;
        Vector3 from = mapRect.position - dir * startDistance;
        Vector3 to = mapRect.position - dir * endDistance;
        Quaternion level = Quaternion.LookRotation(dir, Vector3.up);

        // A small lateral arc on the way in. A lens that travels dead straight
        // down its own axis has no parallax, and no parallax is most of what
        // makes a push-in read as an image being scaled rather than as a camera
        // moving through a space.
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float arc = mapWorldWidth * 0.09f;

        Cue(AudioID.Trailer_Dread, AudioID.Enemy_Telegraph);
        SpawnMotes(cam);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, approachSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            cam.position = Vector3.LerpUnclamped(from, to, k) + side * (Mathf.Sin(k * Mathf.PI) * arc);
            // The roll bleeds out as it settles, so the last thing the impact
            // did to the camera is visible for a moment and then is not - which
            // is what carries the hit through into this shot.
            cam.rotation = Quaternion.LookRotation((mapRect.position - cam.position).normalized, Vector3.up)
                         * Quaternion.Euler(0f, 0f, Mathf.Lerp(settleRoll, 0f, k));
            yield return null;
        }
        cam.position = to;
        cam.rotation = Quaternion.LookRotation((mapRect.position - cam.position).normalized, Vector3.up);

        float wait = 0f;
        while (wait < holdBeforeCurse) { wait += Time.unscaledDeltaTime; yield return null; }

        yield return StartCoroutine(GrowCurse());

        float after = 0f;
        while (after < holdAfter) { after += Time.unscaledDeltaTime; yield return null; }
    }

    private IEnumerator GrowCurse()
    {
        Cue(AudioID.Trailer_RiserToStrike, AudioID.Enemy_Telegraph);

        Vector2 size = mapRect.sizeDelta;
        Vector2 origin = new Vector2((curseOrigin.x - 0.5f) * size.x, (curseOrigin.y - 0.5f) * size.y);
        float step = stepLength * size.x;
        float reach = Mathf.Sqrt(size.x * size.x + size.y * size.y) * 0.5f;

        branches.Clear();
        for (int i = 0; i < rootBranches; i++)
        {
            // Even angles, offset so the first root is never axis-aligned - a
            // tendril running dead horizontally reads as a drawn line.
            float a = (360f / rootBranches) * i + 17f;
            branches.Add(NewBranch(origin, a, 0, rootWidth));
        }

        Color clean = mapImage != null ? mapImage.color : Color.white;

        // Steps are driven off the CLOCK rather than off frames, so the growth
        // takes curseSeconds on any machine instead of being fast on a good one.
        int stepsToEdge = Mathf.Max(8, Mathf.CeilToInt(reach / Mathf.Max(0.01f, step)));
        float stepInterval = curseSeconds / stepsToEdge;
        float acc = 0f;
        float elapsed = 0f;

        while (elapsed < curseSeconds)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            acc += dt;

            while (acc >= stepInterval)
            {
                acc -= stepInterval;
                Advance(origin, step, size);
            }

            if (mapImage != null)
                mapImage.color = Color.Lerp(clean, cursedTint, Mathf.SmoothStep(0f, 1f, elapsed / curseSeconds));

            yield return null;
        }

        if (mapImage != null) mapImage.color = cursedTint;
    }

    private Branch NewBranch(Vector2 at, float angle, int generation, float width)
    {
        var go = new GameObject("Tendril");
        go.transform.SetParent(mapRect, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.alignment = LineAlignment.TransformZ;   // flat on the map, not billboarded
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.material = LineMaterial();
        lr.textureMode = LineTextureMode.Stretch;
        lr.widthMultiplier = width;
        lr.startColor = curseColor;
        lr.endColor = curseTipColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;

        var b = new Branch { line = lr, tip = at, angle = angle, generation = generation, width = width };
        // Slightly proud of the map so it never z-fights the art.
        b.pts.Add(new Vector3(at.x, at.y, -0.6f));
        lr.positionCount = 1;
        lr.SetPosition(0, b.pts[0]);
        return b;
    }

    private void Advance(Vector2 origin, float step, Vector2 size)
    {
        float halfX = size.x * 0.5f, halfY = size.y * 0.5f;
        int live = branches.Count;

        for (int i = 0; i < live; i++)
        {
            var b = branches[i];
            if (!b.alive) continue;

            // Wander, then pulled back toward growing away from the origin so
            // the growth reaches the edges instead of curling into a knot.
            b.angle += Random.Range(-wander, wander);
            if (outwardBias > 0.001f)
            {
                Vector2 out2 = b.tip - origin;
                if (out2.sqrMagnitude > 1f)
                {
                    float outward = Mathf.Atan2(out2.y, out2.x) * Mathf.Rad2Deg;
                    b.angle = Mathf.LerpAngle(b.angle, outward, outwardBias);
                }
            }

            float r = b.angle * Mathf.Deg2Rad;
            b.tip += new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * step;

            // Off the art - the tendril stops at the coast.
            if (b.tip.x < -halfX || b.tip.x > halfX || b.tip.y < -halfY || b.tip.y > halfY)
            {
                b.alive = false;
                continue;
            }

            b.pts.Add(new Vector3(b.tip.x, b.tip.y, -0.6f));
            b.line.positionCount = b.pts.Count;
            b.line.SetPosition(b.pts.Count - 1, b.pts[b.pts.Count - 1]);

            b.stepsSinceSplit++;
            if (b.stepsSinceSplit >= splitEvery
                && b.generation < maxGenerations
                && branches.Count < maxBranches)
            {
                b.stepsSinceSplit = 0;
                float childWidth = b.width * widthFalloff;

                // Two children, one either side, and the parent carries on
                // between them. Three-way forks are what make a growth read as
                // a plant rather than as a crack.
                branches.Add(NewBranch(b.tip, b.angle + splitAngle, b.generation + 1, childWidth));
                branches.Add(NewBranch(b.tip, b.angle - splitAngle, b.generation + 1, childWidth));
                b.width = childWidth;
                b.line.widthMultiplier = childWidth;

                if (curseSmokePrefab != null && Random.value < smokeOnSplitChance)
                    SpawnSmoke(mapRect.TransformPoint(new Vector3(b.tip.x, b.tip.y, -0.6f)));
            }
        }
    }

    private Material LineMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        // Sprites/Default is what the project already uses for its runtime
        // LineRenderers, so it is known to render correctly here.
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        lineMaterial = new Material(sh);
        return lineMaterial;
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

    private void SpawnSmoke(Vector3 worldPos)
    {
        if (curseSmokePrefab == null) return;

        var fx = Instantiate(curseSmokePrefab, worldPos, Quaternion.LookRotation(-mapRect.forward, Vector3.up));
        fx.transform.localScale = Vector3.one * Mathf.Max(0.01f, curseSmokeScale);

        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;
        }

        spawned.Add(fx);
    }

    public void Cleanup()
    {
        for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();
        branches.Clear();
        if (lineMaterial != null) { Destroy(lineMaterial); lineMaterial = null; }
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
