#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;
using FMODUnity;

// Web browsers keep the audio context SUSPENDED until the first real user
// gesture (autoplay policy). The logo-scene music and the intro voiceover
// are plain Unity AudioSources set to Play-On-Awake, so they fire the instant
// the game loads — before any gesture — and are lost into a muted context;
// Unity's own resume comes too late (the logo has already played to silence).
//
// Fix: at startup, before the first scene's objects wake, freeze time and
// PAUSE the whole audio system, then show a one-tap gate. AudioListener.pause
// halts the audio DSP clock so Play-On-Awake sources queue instead of playing
// into the void, and timeScale = 0 holds the logo sequence so it doesn't run
// out before the player taps. On the first gesture we resume Unity audio, the
// FMOD mixer, and time — so the logo music and intro VO play from the top with
// sound. WebGL player builds only; the Steam/desktop build is untouched.
public class WebGLStartGate : MonoBehaviour
{
    private static bool s_bootstrapped;
    private bool _started;
    private int _releaseFrame = -1;
    private GUIStyle _style;

    // True while the gate is up AND for one frame after it's dismissed, so
    // the SAME tap that unlocks audio isn't also consumed by the title
    // screen's "press any key" (which would skip the title reveal entirely).
    // Other input-driven scripts check this on WebGL and ignore that tap.
    public static bool BlockingInput { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped) return;
        s_bootstrapped = true;

        BlockingInput = true;
        AudioListener.pause = true;   // hold all Unity audio until first gesture
        Time.timeScale = 0f;          // hold the logo sequence so it doesn't advance

        var go = new GameObject("[WebGLStartGate]");
        DontDestroyOnLoad(go);
        go.AddComponent<WebGLStartGate>();
    }

    private void Update()
    {
        if (!_started)
        {
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
                Begin();
            return;
        }

        // Keep swallowing input for one extra frame, then release + clean up.
        if (Time.frameCount > _releaseFrame)
        {
            BlockingInput = false;
            Destroy(gameObject);
        }
    }

    private void Begin()
    {
        if (_started) return;
        _started = true;

        Time.timeScale = 1f;
        AudioListener.pause = false;
        try { RuntimeManager.CoreSystem.mixerResume(); } catch { /* FMOD may not be up yet; Unity audio is already resumed */ }

        // Hold BlockingInput through the next frame so the dismiss tap can't
        // double-trigger the title transition; Update() releases it then.
        _releaseFrame = Time.frameCount + 1;
    }

    private void OnGUI()
    {
        if (_started) return;

        // Full-screen dim so the prompt reads over whatever the first scene draws.
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.04f)),
                wordWrap = true
            };
            _style.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height),
                  "Tap or press any key to start\nНатисніть, щоб почати", _style);

        // Belt-and-braces: some browsers deliver the gesture as a GUI event.
        if (Event.current != null &&
            (Event.current.type == EventType.MouseDown || Event.current.type == EventType.KeyDown))
            Begin();
    }
}
#endif
