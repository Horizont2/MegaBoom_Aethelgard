using System.Collections.Generic;
using UnityEngine;

// Cinematic-pause scenery controller. Drop on the PauseLocation prefab
// root, point pauseCamera at the WaterfallPos camera inside the prefab,
// and the gameplay pause hook flips to it when the player opens the
// pause menu. The scenery underneath keeps playing (water flow, leaves,
// fish, ambient particles) even though Time.timeScale = 0, because
// every Animator / ParticleSystem inside the prefab gets switched to
// UnscaledTime mode at registration.
//
// Toggle from GlobalHUD.TogglePause via SetActive(true/false) — that
// stops the camera coming on during gameplay scenes that don't have a
// pause location.
[DisallowMultipleComponent]
public class PauseSceneController : MonoBehaviour
{
    [Tooltip("Camera inside the PauseLocation prefab (e.g. WaterfallPos). Enabled when the player opens pause, disabled otherwise.")]
    public Camera pauseCamera;

    [Tooltip("Optional: AudioListener on the pause camera. Disabled during gameplay so the main listener wins; enabled during pause for the waterfall ambience.")]
    public AudioListener pauseListener;

    [Tooltip("Optional Volume / post-process overrides that should only run during cinematic pause.")]
    public GameObject[] pauseOnlyObjects;

    private Camera prevMainCamera;
    private AudioListener prevListener;
    private bool armed;
    private readonly List<Animator>      cachedAnimators = new List<Animator>();
    private readonly List<ParticleSystem> cachedParticles = new List<ParticleSystem>();

    private void Awake()
    {
        // Walk every Animator + ParticleSystem under this prefab and
        // flip them to unscaled time so they keep playing while
        // Time.timeScale = 0. We do this once at Awake — adding new
        // child VFX at runtime would need a Re-cache call.
        var anims = GetComponentsInChildren<Animator>(true);
        foreach (var a in anims)
        {
            if (a == null) continue;
            a.updateMode = AnimatorUpdateMode.UnscaledTime;
            cachedAnimators.Add(a);
        }
        var pss = GetComponentsInChildren<ParticleSystem>(true);
        foreach (var p in pss)
        {
            if (p == null) continue;
            var main = p.main;
            main.useUnscaledTime = true;
            cachedParticles.Add(p);
        }

        // Default state: camera off, scenery hidden. GlobalHUD enables on pause.
        if (pauseCamera   != null) pauseCamera.enabled = false;
        if (pauseListener != null) pauseListener.enabled = false;
        if (pauseOnlyObjects != null)
            foreach (var go in pauseOnlyObjects) if (go != null) go.SetActive(false);

        armed = true;
    }

    public void EnterPause()
    {
        if (!armed) Awake();
        if (Camera.main != null) prevMainCamera = Camera.main;
        if (pauseCamera != null) pauseCamera.enabled = true;

        // Swap audio listeners — gameplay listener silenced, the
        // pause-cam one wakes up so we hear the waterfall instead of
        // the suddenly-muted combat scene.
        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        foreach (var l in listeners)
        {
            if (l == pauseListener) continue;
            if (l != null && l.enabled) { prevListener = l; l.enabled = false; break; }
        }
        if (pauseListener != null) pauseListener.enabled = true;

        if (pauseOnlyObjects != null)
            foreach (var go in pauseOnlyObjects) if (go != null) go.SetActive(true);
    }

    public void ExitPause()
    {
        if (pauseCamera != null) pauseCamera.enabled = false;
        if (pauseListener != null) pauseListener.enabled = false;
        if (prevListener != null) prevListener.enabled = true;
        if (pauseOnlyObjects != null)
            foreach (var go in pauseOnlyObjects) if (go != null) go.SetActive(false);
    }
}
