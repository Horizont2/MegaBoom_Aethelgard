using System.IO;
using UnityEngine;

// Grabs the still that Steam shows on a video before anyone presses play.
//
// ==== WHY NOT JUST PULL A FRAME OUT OF THE VIDEO ====
//
// Because the video has already been through an encoder. A frame lifted out of
// an H.264 file carries every compression artefact of the frame it happened to
// land on — and on this trailer that means banding through the fog, which is
// the single thing a still shows off worst. It is also stuck at whatever
// resolution the trailer was cut at, with the letterbox burned in.
//
// Rendering the frame again from the engine costs nothing and gives a clean
// one: no encoder, no bars, and as many pixels as you ask for.
//
// ==== TWO KEYS, BECAUSE THERE ARE TWO KINDS OF STILL ====
//
// F9 renders the SHOT CAMERA straight to a texture. Screen-space overlays are
// not part of a camera's own render, so this comes out with no letterbox, no
// HUD and no title — a full-bleed image, which is what a video thumbnail wants.
//
// F10 captures the frame as it stands, bars and title and all, for when you
// want the card itself as the still.
//
// ==== RENDER BIG, DELIVER SMALL, AND DO THE SHRINKING HERE ====
//
// The file that lands on disk is 1920x1080, ready to upload — no resizing step
// in an image editor, because a step a person has to remember is a step that
// eventually gets skipped.
//
// It is still RENDERED at twice that and scaled down before it is written.
// These cameras have antialiasing off, and a two-to-one downscale averages
// exactly four source pixels into each delivered one: a box filter, which is
// the cheapest and best antialiasing available and better than anything the
// engine would have produced at 1080 directly. The cost is one extra buffer
// for the length of one frame.
public static class PosterFrame
{
    public static KeyCode cleanKey = KeyCode.F9;
    public static KeyCode framedKey = KeyCode.F10;

    // What comes out. Steam wants a screenshot at the size it will be shown,
    // and for this game that is 1080p.
    public static int width = 1920;
    public static int height = 1080;

    [Tooltip("Rendered at this multiple of the output size, then scaled down. Two is a clean box filter; one switches supersampling off.")]
    public static int supersample = 2;

    private const string Folder = "PosterFrames";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
#if UNITY_EDITOR
        var go = new GameObject("PosterFrame");
        go.hideFlags = HideFlags.DontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Listener>();
#endif
    }

    private static Listener s_listener;

    private sealed class Listener : MonoBehaviour
    {
        private void Awake() { s_listener = this; }

        private void Update()
        {
            if (Input.GetKeyDown(cleanKey)) GrabClean();
            else if (Input.GetKeyDown(framedKey)) GrabFramed();
        }
    }

    // ===================== the clean one =====================

    /// <summary>The shot camera at full size, without the letterbox or anything else screen-space.</summary>
    /// <param name="explicitCam">The camera to render. Left null, the one currently making the picture is found.</param>
    public static void GrabClean(Camera explicitCam = null)
    {
        Camera cam = explicitCam != null ? explicitCam : Shooting();
        if (cam == null) { Debug.LogWarning("[PosterFrame] No enabled camera to render."); return; }

        int ss = Mathf.Clamp(supersample, 1, 4);
        int bigW = width * ss, bigH = height * ss;

        RenderTexture big = null, small = null;
        RenderTexture wasActive = RenderTexture.active;
        RenderTexture wasTarget = cam.targetTexture;
        float wasAspect = cam.aspect;

        try
        {
            big = new RenderTexture(bigW, bigH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            big.filterMode = FilterMode.Bilinear;     // the blit below depends on it
            big.Create();

            // Without this the camera keeps the game view's aspect and the
            // framing quietly changes between what you saw and what you get.
            cam.aspect = (float)width / height;
            cam.targetTexture = big;
            cam.Render();

            // A bilinear blit from exactly twice the size samples each
            // destination pixel at the meeting point of four source pixels, so
            // it averages all four. That is a box filter, which is what a
            // downscale should be — and why the supersample has to be a whole
            // number.
            small = RenderTexture.GetTemporary(width, height, 0,
                                               RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            small.filterMode = FilterMode.Bilinear;
            Graphics.Blit(big, small);

            RenderTexture.active = small;
            var shot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply(false);

            Write(shot.EncodeToPNG(), "clean", bigW, bigH);
            Object.DestroyImmediate(shot);
        }
        finally
        {
            cam.targetTexture = wasTarget;
            cam.aspect = wasAspect;
            RenderTexture.active = wasActive;
            if (small != null) RenderTexture.ReleaseTemporary(small);
            if (big != null) { big.Release(); Object.DestroyImmediate(big); }
        }
    }

    // ===================== the frame as it stands =====================

    /// <summary>Exactly what is on screen, letterbox and HUD included, delivered at the output size.</summary>
    public static void GrabFramed()
    {
        if (s_listener == null)
        {
            Debug.LogWarning("[PosterFrame] Not in Play Mode, so there is no frame to capture.");
            return;
        }
        s_listener.StartCoroutine(FramedRoutine());
    }

    // ==== WHY THIS ONE IS A COROUTINE ====
    //
    // It used to call ScreenCapture.CaptureScreenshot, which writes the file
    // itself — at the game view's size times the supersample, with no way to
    // scale it down. That is how the framed grab ended up being the one shot
    // that still needed resizing by hand.
    //
    // CaptureScreenshotAsTexture hands the pixels back instead, so they can be
    // scaled here like the clean grab is. It has one rule: it must be called
    // after everything has finished drawing, or it captures a half-built frame.
    // Hence the wait, and hence a coroutine.
    private static System.Collections.IEnumerator FramedRoutine()
    {
        yield return new WaitForEndOfFrame();

        int ss = Mathf.Clamp(supersample, 1, 4);
        Texture2D raw = ScreenCapture.CaptureScreenshotAsTexture(ss);
        if (raw == null)
        {
            Debug.LogWarning("[PosterFrame] The screen capture came back empty.");
            yield break;
        }

        int bigW = raw.width, bigH = raw.height;
        RenderTexture wasActive = RenderTexture.active;
        RenderTexture small = null;

        try
        {
            small = RenderTexture.GetTemporary(width, height, 0,
                                               RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            small.filterMode = FilterMode.Bilinear;
            raw.filterMode = FilterMode.Bilinear;
            Graphics.Blit(raw, small);

            RenderTexture.active = small;
            var shot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply(false);

            Write(shot.EncodeToPNG(), "framed", bigW, bigH);
            Object.DestroyImmediate(shot);
        }
        finally
        {
            RenderTexture.active = wasActive;
            if (small != null) RenderTexture.ReleaseTemporary(small);
            Object.DestroyImmediate(raw);
        }
    }

    // ===================== plumbing =====================

    // The camera that is actually making the picture: enabled, and last in the
    // depth order. Camera.main would be wrong the moment a shot hands the frame
    // to a camera that is not tagged MainCamera.
    private static Camera Shooting()
    {
        Camera best = null;
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isActiveAndEnabled || c.targetTexture != null) continue;
            if (best == null || c.depth >= best.depth) best = c;
        }
        return best;
    }

    private static void Write(byte[] png, string kind, int renderedW, int renderedH)
    {
        string path = Path(kind);
        File.WriteAllBytes(path, png);
        Debug.Log($"[PosterFrame] {width}x{height}, rendered at {renderedW}x{renderedH} and scaled down " +
                  $"-> {path}\nReady to upload as it is.");
    }

    private static string Path(string kind)
    {
        // Beside the project rather than inside Assets, so a pile of PNGs never
        // ends up in the asset database being imported as textures.
        string dir = System.IO.Path.Combine(Directory.GetCurrentDirectory(), Folder);
        Directory.CreateDirectory(dir);

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string stamp = System.DateTime.Now.ToString("HHmmss");
        return System.IO.Path.Combine(dir, scene + "_" + kind + "_" + stamp + ".png");
    }
}
