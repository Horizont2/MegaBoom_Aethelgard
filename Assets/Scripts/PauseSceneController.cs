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
    // Snapshot of isControlBlocked at EnterPause so ExitPause can
    // restore it — otherwise unpausing during a tutorial / cinematic
    // clobbers the block that other systems already set.
    private bool prevPlayerControlBlocked = false;
    private bool armed;
    private bool foliageRegistered = false;
    private readonly List<Animator> cachedAnimators = new List<Animator>();
    private readonly List<ParticleSystem> cachedParticles = new List<ParticleSystem>();

    private void Awake()
    {
        // Defensive: make sure the pause camera starts OFF and the flag clear,
        // so a stray state (or a scene that loaded with it enabled) can't leave
        // the pause "location" on screen before any real pause happens.
        IsPauseActive = false;
        if (pauseCamera != null) pauseCamera.enabled = false;

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

    private void Start()
    {
        // Registered here as well as on the first pause. Doing it only on pause
        // meant the very first frame the player ever saw of this place was in
        // whatever season it was authored in, with the correct one arriving a
        // frame later. It is the same call twice and the second one is nearly
        // free - every renderer is remembered in a HashSet.
        RegisterSeasonalScenery();
    }

    // ==== THE PAUSE LOCATION WAS LIVING IN A DIFFERENT YEAR ====
    //
    // Camp trees and bushes are re-skinned per season by SmartSeasonManager and
    // this vista was outside all of it: high summer behind a pause menu opened
    // in a snowbound camp.
    //
    // ==== AND THEN IT REPAINTED THE WATER ====
    //
    // The first attempt handed SmartSeasonManager this whole transform when
    // foliageRoots was empty. That is wrong, and the reason is in the
    // manager's own filter: AddFoliage rejects only names that read as wood,
    // trunk or branch, and takes everything else. It gets away with that in
    // the camp because the camp points it at tree containers. Given a whole
    // location it swapped the leaf material onto the water, the props and the
    // logs.
    //
    // So candidates are picked by name here, the way the manager's own
    // wood/trunk test picks its exclusions, and only those subtrees are
    // handed over. foliageRoots still wins outright when it is filled in -
    // it is exact, and this is a guess.
    private static readonly string[] FoliageNameHints =
    {
        "tree", "bush", "shrub", "leaf", "leaves", "foliage", "plant", "grass",
        "vegetation", "flora", "fern", "hedge",
    };

    // Named like foliage but is not: these keep their own materials.
    private static readonly string[] FoliageNameBlockers =
    {
        "water", "river", "lake", "waterfall", "log", "stump", "trunk", "branch",
        "wood", "plank", "rock", "stone", "prop", "vfx", "particle", "fx",
    };

    private static bool LooksLikeFoliage(string name)
    {
        foreach (var bad in FoliageNameBlockers)
            if (name.IndexOf(bad, System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        foreach (var good in FoliageNameHints)
            if (name.IndexOf(good, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private void RegisterSeasonalScenery()
    {
        SmartSeasonManager seasonMgr = SmartSeasonManager.Instance;
        if (seasonMgr == null) seasonMgr = FindFirstObjectByType<SmartSeasonManager>();
        if (seasonMgr == null) return;

        if (foliageRoots != null && foliageRoots.Length > 0)
        {
            foreach (Transform root in foliageRoots)
                if (root != null) seasonMgr.RegisterDynamicFoliage(root);

            foliageRegistered = true;
            return;
        }

        // Nothing assigned, so guess - and register the highest match in each
        // branch rather than every match, so a "Trees" container is handed over
        // once instead of once per tree inside it.
        int found = 0;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == transform) continue;
            if (!LooksLikeFoliage(t.name)) continue;
            if (t.parent != null && t.parent != transform && LooksLikeFoliage(t.parent.name)) continue;

            seasonMgr.RegisterDynamicFoliage(t);
            found++;
        }

        if (found == 0)
        {
            Debug.LogWarning($"[PauseScene] '{name}' has nothing under it whose name reads as foliage, so the " +
                             "pause location will not follow the season. Drag its tree and bush containers into " +
                             "Foliage Roots.", this);
        }

        foliageRegistered = true;
    }

    public void EnterPause()
    {
        // Read the transition edge BEFORE flipping the flag so a double
        // invocation (Esc + a scripted pause request in the same frame)
        // doesn't re-snapshot the just-set true and get stuck on
        // ExitPause. Only the first call captures the real prior state.
        bool wasAlreadyPaused = IsPauseActive;
        IsPauseActive = true;

        var player = FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            if (!wasAlreadyPaused) prevPlayerControlBlocked = player.isControlBlocked;
            player.isControlBlocked = true;
        }

        RegisterSeasonalScenery();

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
        // Restore the pre-pause state instead of hard-clearing. A
        // tutorial or cinematic that had blocked control before pause
        // keeps its block after resume.
        if (player != null) player.isControlBlocked = prevPlayerControlBlocked;

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