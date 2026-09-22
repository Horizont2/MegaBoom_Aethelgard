using System.Collections;
using UnityEngine;

// The last card of the trailer: the name, out of the fog.
//
// ==== WHY THE OLD ONE READ AS CHEAP ====
//
// It was TextMeshPro's default font, centred, alpha-faded up on flat black.
// Every one of those four is a tell, and together they say "slide" rather than
// "end of a film":
//
//   The default font is LiberationSans, a UI grotesque. A dark-fantasy siege
//   set in the same face as an options menu is the single loudest problem.
//
//   Flat black behind it. Nothing has moved for a second and a half, so the
//   picture has already ended and the words are an afterthought laid on top.
//
//   Alpha only. Everything else in this trailer is lit, fogged and moving; the
//   card was the one thing with no light in it at all.
//
//   It arrived complete. Titles that simply appear read as graphics. Titles
//   that settle — that come in slightly large and slightly loose and tighten
//   onto their final size — read as weight.
//
// ==== WHAT THIS DOES INSTEAD ====
//
// It is a SHOT, not a card. The camera sits in the fog and drifts the whole
// time, embers rise through it, and lightning goes off somewhere behind. The
// name comes up out of that, in Cinzel, tightening as it lands, lit by a strike
// on the frame it reaches full — so the title is inside the weather rather than
// printed over it. The second line waits until the first has settled, because
// two things appearing at once is a slide and one after another is a beat.
//
// Self-contained on purpose: it builds its own fog dressing, its own embers and
// its own UI, and grounds its own camera off the terrain. That means it runs in
// Trailer_Lvl_1, which opens instantly, instead of needing the castle — so it
// can be re-recorded in ten seconds rather than after a region generation.
public class TrailerTitleCard : MonoBehaviour
{
    [Header("Play")]
    public bool autoPlay = true;

    /// <summary>True once the card has played out. The editor's recorder watches this.</summary>
    public bool IsFinished { get; private set; }

    [Header("Words")]
    [Tooltip("Left empty, the project's own product name is used — so the card cannot say something the build does not.")]
    public string title = "";
    [Tooltip("Left empty, the project's company name is used. This is the line to change for a date or a wishlist call.")]
    public string secondLine = "";
    [Tooltip("A wordmark image. Assigned, it replaces the typeset title entirely.")]
    public Sprite titleSprite;

    [Header("Type")]
    public TMPro.TMP_FontAsset titleFont;
    public TMPro.TMP_FontAsset lineFont;
    public int titleSize = 112;
    public int lineSize = 26;
    [Tooltip("Letter spacing the title arrives at, and the one it settles to. Loose to tight is what makes it land.")]
    public float titleTrackingIn = 34f;
    public float titleTrackingOut = 15f;
    [Tooltip("The title starts a touch large and settles. Above 1.10 it reads as a zoom rather than as weight.")]
    public float titleScaleIn = 1.06f;
    public Color titleEmber = new Color(0.62f, 0.30f, 0.15f, 1f);
    public Color titleBone = new Color(0.94f, 0.91f, 0.85f, 1f);
    public Color lineColour = new Color(0.60f, 0.58f, 0.56f, 1f);

    [Header("Timing")]
    public float breathSeconds = 1.8f;     // fog alone, before the name
    public float titleRise = 1.3f;
    public float titleHold = 1.7f;
    public float lineRise = 0.9f;
    public float lineHold = 2.0f;
    public float closeSeconds = 1.5f;

    [Header("Camera")]
    [Tooltip("Metres above the terrain. High enough to clear the treetops and look ACROSS the top of the fog, while still inside the layer — that is what makes the background a field rather than a wall.")]
    public float cameraHeight = 22f;
    [Tooltip("Degrees below the horizon. A few, so the fog reads as a surface and not as a wall.")]
    public float cameraPitch = 7f;
    [Tooltip("Metres the camera drifts across the whole card. Small — this is a breath, not a move.")]
    public float driftMetres = 4.5f;
    public float driftDegrees = 2.2f;

    [Header("Weather")]
    public int embers = 90;
    public Color emberColour = new Color(1f, 0.55f, 0.22f, 0.55f);
    [Tooltip("Distant strikes during the breath, before the name.")]
    public int distantStrikes = 2;

    [Header("Sound")]
    public string windBed = AudioID.Trailer_WindDesolate;
    public string dreadBed = AudioID.Trailer_Dread;
    [Tooltip("The hit the name lands on.")]
    public string titleImpact = AudioID.Trailer_Impact;

    private Camera cam;
    private Transform camT;
    private Vector3 camHome;
    private float yawHome;
    private ParticleSystem emberSystem;

    private CanvasGroup titleGroup, lineGroup;
    private TMPro.TextMeshProUGUI titleText;
    private RectTransform titleRect;

    private void Start()
    {
        cam = GetComponentInChildren<Camera>(true);
        if (cam == null) cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[TitleCard] No camera."); enabled = false; return; }

        camT = cam.transform;
        PlaceCamera();
        BuildEmbers();
        BuildUI();

        if (autoPlay) StartCoroutine(Run());
    }

    // ==== THE CAMERA IS GROUNDED, NOT TYPED ====
    //
    // A hand-typed height only works where the rig happens to sit, and the whole
    // point of this card is that it can be dropped anywhere there is fog. So the
    // terrain under the lens decides, the same way the statue shot's does.
    private void PlaceCamera()
    {
        Vector3 p = camT.position;
        float ground = p.y;

        Terrain t = Terrain.activeTerrain;
        if (t != null)
        {
            float y = t.SampleHeight(p) + t.transform.position.y;
            if (y > -1000f && y < 5000f) ground = y;
        }

        camHome = new Vector3(p.x, ground + cameraHeight, p.z);
        yawHome = camT.eulerAngles.y;

        camT.position = camHome;
        camT.rotation = Quaternion.Euler(cameraPitch, yawHome, 0f);
        cam.fieldOfView = 46f;
    }

    // Warm motes rising through the fog. Sparse and small: this is the last
    // ember of everything that burned in the four shots before it, not a fire.
    private void BuildEmbers()
    {
        var go = new GameObject("Embers");
        go.transform.SetParent(transform, false);
        go.transform.position = camT.position + camT.forward * 14f - Vector3.up * 4f;

        emberSystem = go.AddComponent<ParticleSystem>();

        var main = emberSystem.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startColor = emberColour;
        main.startLifetime = 7f;
        main.startSpeed = 0.35f;
        main.startSize = 0.09f;
        main.gravityModifier = -0.02f;          // they rise
        main.maxParticles = Mathf.Max(8, embers);
        main.useUnscaledTime = true;
        // Hierarchy, not Local: the emitter hangs off this transform, and with
        // Local scaling a parent that is ever scaled silently resizes every
        // particle in it.
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var shape = emberSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(40f, 6f, 26f);

        var em = emberSystem.emission;
        em.rateOverTime = embers / 7f;          // lifetime's worth, so the field stays even

        // Drift sideways as they rise, or a hundred motes all going straight up
        // reads as a fountain.
        var noise = emberSystem.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.18f;

        var col = emberSystem.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var pr = emberSystem.GetComponent<ParticleSystemRenderer>();
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", SoftDot());
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", SoftDot());
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);   // additive: they are light
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            pr.material = mat;
        }
        else pr.enabled = false;

        // Pre-warmed, so the first visible frame already has a field of embers
        // in it rather than an empty sky filling up.
        emberSystem.Play();
        emberSystem.Simulate(7f, true, false, false);
        emberSystem.Play();
    }

    private static Texture2D s_dot;
    private static Texture2D SoftDot()
    {
        if (s_dot != null) return s_dot;

        const int N = 32;
        s_dot = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = Mathf.Clamp01(1f - d); a *= a;
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        s_dot.SetPixels32(px);
        s_dot.Apply(true);
        return s_dot;
    }

    // ==== BELOW THE LETTERBOX, NOT ABOVE IT ====
    //
    // Sorting order 4900, under TrailerCinematicPolish's 5000. The matte has to
    // draw over the words — it is a frame, not a layer — and the closing fade
    // has to cover them, or the card ends with the name still sitting on black
    // after everything else has gone.
    private void BuildUI()
    {
        var canvasGO = new GameObject("TitleCardUI");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4900;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // The title sits slightly ABOVE centre. Dead centre with a second line
        // under it puts the pair low in frame, and an audience reads that as
        // badly placed without being able to say why.
        titleRect = MakeRect(canvasGO.transform, new Vector2(0.5f, 0.56f), new Vector2(1600f, 220f));
        titleGroup = titleRect.gameObject.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;

        if (titleSprite != null)
        {
            var img = titleRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = titleSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        else
        {
            titleText = titleRect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            // Never assigned null: TextMeshPro throws on a null font asset
            // rather than falling back, and picks its own default perfectly well
            // when the field is simply left alone.
            if (titleFont != null) titleText.font = titleFont;
            else Debug.LogWarning("[TitleCard] No title font assigned — falling back to TextMeshPro's default, " +
                                  "which is a UI grotesque and is most of why the old card looked cheap. " +
                                  "Assign Assets/DownloadedFonts/Cinzel/Cinzel-VariableFont_wght SDF.");

            titleText.text = string.IsNullOrEmpty(title) ? Application.productName.ToUpperInvariant()
                                                         : title.ToUpperInvariant();
            titleText.fontSize = titleSize;
            titleText.color = titleEmber;
            titleText.alignment = TMPro.TextAlignmentOptions.Center;
            titleText.characterSpacing = titleTrackingIn;
            titleText.raycastTarget = false;
        }

        var lineRect = MakeRect(canvasGO.transform, new Vector2(0.5f, 0.435f), new Vector2(1400f, 60f));
        lineGroup = lineRect.gameObject.AddComponent<CanvasGroup>();
        lineGroup.alpha = 0f;

        var line = lineRect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (lineFont != null) line.font = lineFont;
        line.text = (string.IsNullOrEmpty(secondLine) ? Application.companyName : secondLine).ToUpperInvariant();
        line.fontSize = lineSize;
        line.color = lineColour;
        line.alignment = TMPro.TextAlignmentOptions.Center;
        line.characterSpacing = 9f;
        line.raycastTarget = false;
    }

    private static RectTransform MakeRect(Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        return rt;
    }

    // ===================== the card =====================

    public void Play() { if (!IsFinished) StartCoroutine(Run()); }

    private IEnumerator Run()
    {
        TrailerLogGuard.Arm();
        var polish = TrailerCinematicPolish.GetOrCreate();
        TrailerAudio.SilenceStaleBeds();

        // Also the frame the editor's recorder starts on.
        polish.OpenTrailer();
        Loop(windBed);
        Loop(dreadBed);

        float total = breathSeconds + titleRise + titleHold + lineRise + lineHold + closeSeconds;
        StartCoroutine(DriftCamera(total));

        // ---- 1. the fog alone ----
        //
        // A card needs a frame of nothing in front of it. Without this beat the
        // name arrives on the cut and the cut does all the work; with it the
        // audience has already settled and the name is the thing that happens.
        yield return Beat(breathSeconds, distantStrikes > 0);

        // ---- 2. the name arrives ----
        Cue(titleImpact);
        float t = 0f;
        while (t < titleRise)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, titleRise));
            // Ease-out: fast at first, then it settles. An ease-in would read as
            // the title being pushed into place.
            float e = 1f - (1f - k) * (1f - k) * (1f - k);

            titleGroup.alpha = e;
            titleRect.localScale = Vector3.one * Mathf.Lerp(titleScaleIn, 1f, e);
            if (titleText != null)
            {
                titleText.characterSpacing = Mathf.Lerp(titleTrackingIn, titleTrackingOut, e);
                titleText.color = Color.Lerp(titleEmber, titleBone, e * e);
            }

            // The strike lands on the frame the name reaches full, so the two
            // read as one event: the sky opens and the title is what is under it.
            if (k >= 0.82f && !struck)
            {
                struck = true;
                Strike(camT.position + camT.forward * 120f + Vector3.up * 55f, 190f, 3.4f, 0.55f);
            }
            yield return null;
        }
        titleGroup.alpha = 1f;
        titleRect.localScale = Vector3.one;

        yield return Beat(titleHold, false);

        // ---- 3. and then, separately, the line ----
        t = 0f;
        while (t < lineRise)
        {
            t += Time.unscaledDeltaTime;
            lineGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, lineRise)));
            yield return null;
        }
        lineGroup.alpha = 1f;

        yield return Beat(lineHold, false);

        // ---- 4. out ----
        DropOut();
        t = 0f;
        while (t < closeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, closeSeconds));
            var c = Color.black; c.a = k * k;
            polish.SetFlash(c);
            yield return null;
        }
        polish.SetFlash(Color.black);

        yield return new WaitForSecondsRealtime(0.5f);
        IsFinished = true;
    }

    private bool struck;

    // A beat that keeps the clock running, optionally with weather in it.
    private IEnumerator Beat(float seconds, bool weather)
    {
        float t = 0f;
        float nextStrike = seconds / Mathf.Max(1, distantStrikes + 1);

        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;

            if (weather && t >= nextStrike)
            {
                nextStrike += seconds / Mathf.Max(1, distantStrikes + 1);
                // Far away, off to one side, dim. These are not the strike — they
                // are what makes the one on the title read as close.
                Vector3 at = camT.position
                           + camT.forward * Random.Range(200f, 340f)
                           + camT.right * Random.Range(-160f, 160f)
                           + Vector3.up * Random.Range(60f, 110f);
                Strike(at, Random.Range(120f, 200f), 1.5f, 0.42f);
            }
            yield return null;
        }
    }

    private void Strike(Vector3 at, float size, float strength, float seconds)
    {
        SkyFlashFlare.Flash(at, new Color(0.72f, 0.80f, 1f), size, strength, seconds);
    }

    private IEnumerator DriftCamera(float total)
    {
        float t = 0f;
        while (t < total + 1f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, total));
            // Linear, deliberately. Every other move in this trailer eases; a
            // card is the one place where the drift should never arrive
            // anywhere, because arriving is an event and this beat has only one.
            camT.position = camHome + camT.right * (driftMetres * k) + Vector3.up * (driftMetres * 0.25f * k);
            camT.rotation = Quaternion.Euler(cameraPitch, yawHome + driftDegrees * k, 0f);
            yield return null;
        }
    }

    // ===================== sound =====================

    private readonly System.Collections.Generic.List<string> beds = new System.Collections.Generic.List<string>();

    private void Cue(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
    }

    private void Loop(string id)
    {
        if (AudioManager.Instance == null || string.IsNullOrEmpty(id)) return;
        AudioManager.Instance.PlaySFX(id);
        beds.Add(id);
    }

    private void DropOut()
    {
        if (AudioManager.Instance == null) return;
        for (int i = 0; i < beds.Count; i++) AudioManager.Instance.StopLoopedBed(beds[i]);
        beds.Clear();
    }

    private void OnDestroy() { DropOut(); }
}
