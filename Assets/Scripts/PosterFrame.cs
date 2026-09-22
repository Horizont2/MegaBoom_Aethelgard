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
// ==== RENDER BIG, DELIVER SMALL ====
//
// The default is 4K for a thumbnail that ships at 1920x1080. That is not waste:
// these cameras have antialiasing off, and downscaling a 4K render by half is
// the cheapest and best antialiasing there is. Do the downscale last, in an
// image editor, and the edges come out cleaner than anything the engine would
// have given at 1080 directly.
public static class PosterFrame
{
    public static KeyCode cleanKey = KeyCode.F9;
    public static KeyCode framedKey = KeyCode.F10;

    // 4K, to be halved on delivery. Steam wants the thumbnail at the video's own
    // size, which for a 1080p trailer is 1920x1080.
    public static int width = 3840;
    public static int height = 2160;

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

    private sealed class Listener : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(cleanKey)) GrabClean();
            else if (Input.GetKeyDown(framedKey)) GrabFramed();
        }
    }

    // ===================== the clean one =====================

    /// <summary>The shot camera at full size, without the letterbox or anything else screen-space.</summary>
    public static void GrabClean()
    {
        Camera cam = Shooting();
        if (cam == null) { Debug.LogWarning("[PosterFrame] No enabled camera to render."); return; }

        RenderTexture rt = null;
        RenderTexture wasActive = RenderTexture.active;
        RenderTexture wasTarget = cam.targetTexture;
        float wasAspect = cam.aspect;

        try
        {
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();

            // Without this the camera keeps the game view's aspect and the
            // framing quietly changes between what you saw and what you get.
            cam.aspect = (float)width / height;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var shot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply(false);

            Write(shot.EncodeToPNG(), "clean");
            Object.DestroyImmediate(shot);
        }
        finally
        {
            cam.targetTexture = wasTarget;
            cam.aspect = wasAspect;
            RenderTexture.active = wasActive;
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
        }
    }

    // ===================== the frame as it stands =====================

    /// <summary>Exactly what is on screen, letterbox and title included.</summary>
    public static void GrabFramed()
    {
        // Whole numbers only: superSize renders the game view that many times
        // over, so anything else would land on a size nobody asked for.
        int super = Mathf.Clamp(Mathf.RoundToInt((float)width / Mathf.Max(1, Screen.width)), 1, 4);
        string path = Path(("framed_x" + super));

        ScreenCapture.CaptureScreenshot(path, super);
        Debug.Log("[PosterFrame] Frame captured at " + (Screen.width * super) + "x" + (Screen.height * super) +
                  " (game view x" + super + ") -> " + path +
                  "\nIt is written at the end of this frame, so give it a moment before opening it.");
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

    private static void Write(byte[] png, string kind)
    {
        string path = Path(kind);
        File.WriteAllBytes(path, png);
        Debug.Log("[PosterFrame] " + width + "x" + height + " -> " + path +
                  "\nHalve it to 1920x1080 in an image editor before uploading; that downscale is the antialiasing.");
    }

    private static string Path(string kind)
    {
        // Beside the project rather than inside Assets, so a few dozen 4K PNGs
        // never end up in the asset database being imported.
        string dir = System.IO.Path.Combine(Directory.GetCurrentDirectory(), Folder);
        Directory.CreateDirectory(dir);

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string stamp = System.DateTime.Now.ToString("HHmmss");
        return System.IO.Path.Combine(dir, scene + "_" + kind + "_" + stamp + ".png");
    }
}
