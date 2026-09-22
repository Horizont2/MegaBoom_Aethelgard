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
    public float titleTrackingIn = 40f;
    public float titleTrackingOut = 18f;
    [Tooltip("The title starts a touch large and settles. Above 1.10 it reads as a zoom rather than as weight.")]
    public float titleScaleIn = 1.06f;
    // ==== THE ONLY CINZEL IN THE PROJECT IS THE LIGHT ONE ====
    //
    // Cinzel-VariableFont_wght SDF is baked from the variable font's DEFAULT
    // instance, which is Regular. The static ExtraBold is in the project as a
    // .ttf and has no SDF asset, so TextMeshPro cannot reach it — the title has
    // been set in a light Roman serif at 112 points this whole time, which is
    // most of why it reads thin rather than carved.
    //
    // Dilating the signed distance field thickens every stroke, which is what
    // TextMeshPro's own faux-bold does (boldStyle is 0.75 on this asset). Done
    // by hand here rather than through FontStyles.Bold, because that also adds
    // seven units of spacing and the tracking is already being animated.
    //
    // The real fix is one pass of Window > TextMesh Pro > Font Asset Creator on
    // static/Cinzel-ExtraBold.ttf; drop the result into titleFont and set this
    // back to zero.
    [Tooltip("Thickens the letterforms. 0 is the font as baked; 0.16 turns this project's Cinzel Regular into something with a title's weight.")]
    [Range(0f, 0.4f)] public float faceWeight = 0.16f;

    [Header("Rule")]
    // ==== WHAT REPLACES THE LIGHTNING ====
    //
    // A word on its own is a word. The hairline is what makes it a title: it
    // gives the name a base to sit on, it separates it from the line below
    // without a gap doing all the work, and — because it draws open from the
    // centre after the name has landed — it is a second small event, which is
    // what the strike used to be. Quieter, and it belongs to the type instead
    // of to the weather.
    [Tooltip("Width in reference pixels. Shorter than the title reads as designed; matching its width reads as an underline.")]
    public float ruleWidth = 460f;
    public float ruleOpen = 0.8f;
    public Color ruleColour = new Color(0.78f, 0.71f, 0.60f, 0.55f);

    public Color titleEmber = new Color(0.62f, 0.30f, 0.15f, 1f);
    public Color titleBone = new Color(0.94f, 0.91f, 0.85f, 1f);
    public Color lineColour = new Color(0.60f, 0.58f, 0.56f, 1f);

    [Header("Timing")]
    // ==== THE BREATH HAS TO OUTLAST THE FADE ====
    //
    // TrailerCinematicPolish fades up from black over 1.4 seconds. At 1.8 the
    // name started rising four tenths of a second after the picture arrived,
    // which is not a beat of quiet — it is the fade and the title happening at
    // once. Three gives a full second of settled fog with nothing in it, and
    // that second is the whole reason the name lands.
    public float breathSeconds = 3.0f;
    public float titleRise = 1.3f;
    public float titleHold = 1.2f;
    public float lineRise = 0.9f;
    public float lineHold = 1.6f;
    public float closeSeconds = 1.5f;

    [Header("Camera")]
    // ==== LOW AND LOOKING UP, NOT HIGH AND LOOKING DOWN ====
    //
    // At twenty-two metres the lens was above the treetops with the fog closing
    // everything past sixty metres, so the background was an even wash: no
    // horizon, no silhouette, nothing for an eye to hold. A title over a flat
    // gradient reads CLEANER than the footage before it, and cleaner reads as
    // cheaper.
    //
    // Ten metres puts the lens inside the fog with the canopy still above it, so
    // the trees at twenty to forty metres come through as dark shapes — scale,
    // for free, out of the scene that is already there. And three degrees UP
    // puts the top of the fog layer across the frame with sky above it, which is
    // both a horizon and something for the strikes to be seen against.
    [Tooltip("Metres above the terrain. Low enough that the treetops stay above the lens and read as silhouettes through the fog.")]
    public float cameraHeight = 10f;
    [Tooltip("Euler X. Positive looks down; a few degrees NEGATIVE lifts the top of the fog layer into frame and puts sky above it.")]
    public float cameraPitch = -3f;
    [Tooltip("Metres the camera drifts across the whole card. Small — this is a breath, not a move.")]
    public float driftMetres = 4.5f;
    public float driftDegrees = 2.2f;

    [Header("Weather")]
    public int embers = 90;
    public Color emberColour = new Color(1f, 0.55f, 0.22f, 0.55f);
    [Tooltip("Distant strikes during the breath. Off: a card this quiet does not want weather happening in it, and the hairline carries the moment the strike used to.")]
    public int distantStrikes = 0;

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
        // Straddling the lens, not sitting under it. The box used to hang four
        // metres BELOW the camera, which was right when the camera was
        // twenty-two metres up and pointed down; from ten metres looking
        // slightly up it put every ember along the bottom edge of the frame.
        go.transform.position = camT.position + camT.forward * 16f + Vector3.up * 1f;

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
        shape.scale = new Vector3(46f, 16f, 30f);

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
            Underlay(titleText, true);
        }

        // The hairline, between the two. Sized to nothing here; it draws open
        // after the name has settled.
        ruleRect = MakeRect(canvasGO.transform, new Vector2(0.5f, 0.477f), new Vector2(0f, 4f));
        ruleImage = ruleRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        ruleImage.sprite = Sprite.Create(Hairline(), new Rect(0f, 0f, 64f, 4f), new Vector2(0.5f, 0.5f));
        ruleImage.color = new Color(ruleColour.r, ruleColour.g, ruleColour.b, 0f);
        ruleImage.raycastTarget = false;

        var lineRect = MakeRect(canvasGO.transform, new Vector2(0.5f, 0.40f), new Vector2(1400f, 60f));
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
        Underlay(line, false);
    }

    // ==== BONE ON GREY IS NOT CONTRAST ====
    //
    // The title and the fog behind it sit at close to the same luminance, so the
    // words go soft at the edges — worst exactly where the fog happens to be
    // brightest, which moves, so it reads as the text wavering.
    //
    // TextMeshPro's underlay is a soft dark spread behind the glyphs. Not a drop
    // shadow offset to one side, which would read as a lower third; centred and
    // wide, so it is a darkening the type sits in rather than a second copy of
    // it. Everything is guarded: on a font material without the underlay pass
    // this simply does nothing instead of throwing.
    private void Underlay(TMPro.TMP_Text text, bool weighted)
    {
        if (text == null) return;

        // fontMaterial, not fontSharedMaterial: the shared one is the asset, and
        // writing to it would leave the change in the project after Play.
        Material m = text.fontMaterial;
        if (m == null) return;

        if (weighted && faceWeight > 0.001f && m.HasProperty("_FaceDilate"))
            m.SetFloat("_FaceDilate", faceWeight);

        m.EnableKeyword("UNDERLAY_ON");
        if (m.HasProperty("_UnderlayColor")) m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.55f));
        if (m.HasProperty("_UnderlayOffsetX")) m.SetFloat("_UnderlayOffsetX", 0f);
        if (m.HasProperty("_UnderlayOffsetY")) m.SetFloat("_UnderlayOffsetY", 0f);
        if (m.HasProperty("_UnderlayDilate")) m.SetFloat("_UnderlayDilate", 0.15f);
        if (m.HasProperty("_UnderlaySoftness")) m.SetFloat("_UnderlaySoftness", 0.55f);
    }

    private RectTransform ruleRect;
    private UnityEngine.UI.Image ruleImage;

    // A hard-edged bar is a bar. Fading to nothing at both ends is what makes
    // the same two pixels read as a rule someone drew.
    private static Texture2D s_hairline;
    private static Texture2D Hairline()
    {
        if (s_hairline != null) return s_hairline;

        const int W = 64, H = 4;
        s_hairline = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float u = Mathf.Abs(x / (W - 1f) - 0.5f) * 2f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - u), 0.7f);
            px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        s_hairline.SetPixels32(px);
        s_hairline.Apply(false);
        return s_hairline;
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

        float total = breathSeconds + titleRise + ruleOpen + titleHold + lineRise + lineHold + closeSeconds;
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

            yield return null;
        }
        titleGroup.alpha = 1f;
        titleRect.localScale = Vector3.one;

        // ---- 3. and the rule draws under it ----
        //
        // From the centre out, which is the only direction that does not imply
        // a reading order the card does not have. This is the beat the strike
        // used to be, and it is a better one: it belongs to the type, so it
        // cannot look like weather that wandered into a title.
        t = 0f;
        while (t < ruleOpen)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, ruleOpen));
            float e = 1f - (1f - k) * (1f - k) * (1f - k);

            ruleRect.sizeDelta = new Vector2(ruleWidth * e, 4f);
            ruleImage.color = new Color(ruleColour.r, ruleColour.g, ruleColour.b, ruleColour.a * e);
            yield return null;
        }
        ruleRect.sizeDelta = new Vector2(ruleWidth, 4f);
        ruleImage.color = ruleColour;

        yield return Beat(titleHold, false);

        // ---- 4. and then, separately, the line ----
        t = 0f;
        while (t < lineRise)
        {
            t += Time.unscaledDeltaTime;
            lineGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, lineRise)));
            yield return null;
        }
        lineGroup.alpha = 1f;

        yield return Beat(lineHold, false);

        // ---- 5. out ----
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
                // Same arithmetic as the close one: past about eighty metres
                // the fog has taken everything, so "distant" here is sixty to
                // ninety rather than the two to three hundred it was. They are
                // not the strike — they are what makes the one on the title
                // read as close.
                Vector3 at = camT.position
                           + camT.forward * Random.Range(55f, 95f)
                           + camT.right * Random.Range(-45f, 45f)
                           + Vector3.up * Random.Range(16f, 26f);
                Strike(at, Random.Range(50f, 80f), 2.4f, 0.4f);
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
