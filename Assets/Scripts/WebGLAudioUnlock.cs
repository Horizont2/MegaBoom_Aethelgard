#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;
using FMODUnity;

// Browsers hold the Web-Audio context SUSPENDED until the first real user
// gesture (autoplay policy), and FMOD's own resume on WebGL is unreliable —
// so any audio played before the player first clicks/taps/keys (the logo
// music, the intro voiceover) comes out silent, while later gameplay audio
// works because a gesture has since unlocked the context.
//
// This persistent object watches for that first gesture and then force-
// resumes the FMOD mixer for a few frames (the context can take a moment to
// switch to "running"), so all subsequent audio plays. Compiled on WebGL
// player builds only.
public class WebGLAudioUnlock : MonoBehaviour
{
    private static bool s_bootstrapped;
    private bool _gestureSeen;
    private int _resumeFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_bootstrapped) return;
        s_bootstrapped = true;
        var go = new GameObject("[WebGLAudioUnlock]");
        DontDestroyOnLoad(go);
        go.AddComponent<WebGLAudioUnlock>();
    }

    private void Update()
    {
        if (!_gestureSeen)
        {
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0)
            {
                _gestureSeen = true;
                _resumeFrames = 30; // ~half a second of resume attempts
            }
            return;
        }

        if (_resumeFrames <= 0)
        {
            Destroy(gameObject);
            return;
        }
        _resumeFrames--;

        try { RuntimeManager.CoreSystem.mixerResume(); }
        catch (System.Exception e)
        {
            // CoreSystem may not be ready on the very first frame — keep
            // retrying on the remaining frames rather than giving up.
            Debug.LogWarning($"[WebGLAudioUnlock] mixerResume retry: {e.Message}");
        }
    }
}
#endif
