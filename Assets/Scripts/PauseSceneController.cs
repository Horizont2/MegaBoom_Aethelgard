using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PauseSceneController : MonoBehaviour
{
    // Публічний прапорець, щоб будь-який скрипт знав, що гра на паузі
    public static bool IsPauseActive { get; private set; }

    [Tooltip("Camera inside the PauseLocation prefab (e.g. WaterfallPos). Enabled when the player opens pause, disabled otherwise.")]
    public Camera pauseCamera;

    [Tooltip("Optional: AudioListener on the pause camera. Disabled during gameplay so the main listener wins; enabled during pause for the waterfall ambience.")]
    public AudioListener pauseListener;

    [Tooltip("Optional Volume / post-process overrides that should only run during cinematic pause.")]
    public GameObject[] pauseOnlyObjects;

    [Header("Seasonal Foliage")]
    [Tooltip("Перетягніть сюди папки (контейнери) з деревами та кущами, які знаходяться всередині цього краєвиду.")]
    public Transform[] foliageRoots;

    private Camera prevMainCamera;
    private AudioListener prevListener;
    private bool armed;
    private bool foliageRegistered = false;
    private readonly List<Animator> cachedAnimators = new List<Animator>();
    private readonly List<ParticleSystem> cachedParticles = new List<ParticleSystem>();

    private void Awake()
    {
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
            var mainMod = p.main;
            mainMod.useUnscaledTime = true;
            cachedParticles.Add(p);
        }
    }

    public void EnterPause()
    {
        IsPauseActive = true;

        var player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.isControlBlocked = true;

        if (foliageRoots != null && foliageRoots.Length > 0)
        {
            SmartSeasonManager seasonMgr = SmartSeasonManager.Instance;
            if (seasonMgr == null) seasonMgr = FindFirstObjectByType<SmartSeasonManager>();

            if (seasonMgr != null)
            {
                foreach (Transform root in foliageRoots)
                {
                    if (root != null) seasonMgr.RegisterDynamicFoliage(root);
                }
                foliageRegistered = true;
            }
        }

        // ЖОРСТКЕ ВІДКЛЮЧЕННЯ ГОЛОВНОЇ КАМЕРИ
        if (Camera.main != null)
        {
            prevMainCamera = Camera.main;
            prevMainCamera.enabled = false;
        }
        if (pauseCamera != null) pauseCamera.enabled = true;

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
        IsPauseActive = false;

        var player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.isControlBlocked = false;

        if (pauseCamera != null) pauseCamera.enabled = false;

        // ПОВЕРНЕННЯ ГОЛОВНОЇ КАМЕРИ
        if (prevMainCamera != null) prevMainCamera.enabled = true;

        if (pauseListener != null) pauseListener.enabled = false;
        if (prevListener != null) prevListener.enabled = true;

        if (pauseOnlyObjects != null)
            foreach (var go in pauseOnlyObjects) if (go != null) go.SetActive(false);
    }

    private void OnEnable()
    {
        if (armed) EnterPause();
        armed = true;
    }

    private void OnDisable()
    {
        ExitPause();
    }
}