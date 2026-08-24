using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FMODUnity;
using FMOD.Studio;

public static class AudioID
{
    public const string UI_Click = "UI/Click";
    public const string UI_Hover = "UI/Hover";
    public const string UI_QuestAccept = "UI/QuestAccept";
    public const string UI_QuestComplete = "UI/QuestComplete";
    public const string UI_Error = "UI/Error";
    public const string UI_LevelUp = "UI/LVLUP";
    public const string UI_Purchase = "UI/Purchase";
    public const string UI_GameOver = "UI/GameOver";

    public const string Player_Dash = "Player/Dash";
    public const string Player_Swing = "Player/Swing";
    public const string Player_HitEnemy_Sword = "Player/Hit_Enemy_Sword";
    public const string Player_HitEnemy_Bow = "Player/Hit_Enemy_Bow";
    public const string Player_HitEnemy_Hammer = "Player/Hit_Enemy_Hammer";
    public const string Player_HitResource_Stone = "Player/Hit_Stone";
    public const string Player_HitResource_Wood = "Player/Hit_Wood";
    public const string Player_Hurt = "Player/Hurt";
    public const string Player_Throw = "Player/Throw";
    public const string Player_Heal = "Player/Heal";
    public const string Player_Footstep = "Player/Footsteps";
    public const string Explosion = "Player/Explosion";

    // Unikalne stringi dla kompatybilności ze starymi skryptami
    public const string Player_HitEnemy = "Player/Hit_Enemy_Legacy";
    public const string Player_HitResource = "Player/Hit_Res_Legacy";

    public const string Enemy_Agro = "Enemy/Agro";
    public const string Enemy_Telegraph = "Enemy/Telegraph";
    public const string Enemy_Attack = "Enemy/Charge";
    public const string Enemy_Hurt = "Enemy/Hurt";
    public const string Enemy_Die = "Enemy/Die";
    public const string Enemy_Footstep = "Enemy/Footsteps";
    public const string Enemy_Hit = "Enemy/Hit";
    public const string Arrow_Fire = "Enemy/Arrow_Fire";
    public const string Arrow_Hit = "Enemy/Arrow_Hit";

    public const string Camp_CollectItem = "ENV/Camp_Collect_Item";
    public const string Camp_CollectGem = "ENV/Camp_Collect_Gem";
    public const string Camp_BuildStart = "ENV/Camp_Build";
    public const string Camp_BuildDone = "ENV/Camp_Done";
    public const string NPC_Work = "ENV/NPC_Work";
    public const string Env_Thunder = "AMB/AMB_Thunder";
    public const string Env_ChestOpen = "ENV/WoodChestOpen";

    public const string Animal_CatMeow = "Animals/CatMeow";
    public const string Animal_Chicken = "Animals/Chicken";

    public const string Boss_Roar = "Enemy/Boss/Roar";
    public const string Boss_Stagger = "Enemy/Stagger";
    public const string Boss_Execute = "Enemy/Boss/Boss Execute";
    public const string Region_VictoryStinger = "Enemy/Boss/Victory Stinger";
    public const string Region_Shockwave = "Enemy/Shockwave";
    public const string Cinematic_Whoosh = "Enemy/Boss/Cinematic Whoosh";

    public const string Encounter_Cleared = "UI/Encounter_Cleared";
    public const string Totem_Activate = "UI/Activate Totem";
    public const string Player_PerfectDodge = "Player/Player Perfect Dodge";

    public const string Music_Camp = "Music/Music_Camp";
    public const string Music_Battle = "Music/Music_Journey";
    public const string Music_Level1 = "Music/Music_Level1";

    public const string Ambient_Wind = "AMB/AMB_Wind";
    // Fire crackle for the camp bonfire — needs an FMOD event at this
    // path; CampfireAudio will silently no-op if the event isn't wired.
    public const string Ambient_CampFire = "AMB/AMB_CampFire";
    public const string Ambient_Howl = "AMB/AMB_Howl";
    public const string Ambient_Crow = "AMB/AMB_Crow";
    public const string Ambient_DistantThunder = "AMB/AMB_Distant_Thunder";
    public const string Ambient_LeafRustle = "AMB/AMB_Leaf_Rustle";

    // ── NEW / previously-missing sounds (wire the FMOD events in the Inspector) ──
    public const string Ambient_Rain = "AMB/AMB_Rain";           // looping rain bed
    public const string UI_Open = "UI/Open";
    public const string UI_Close = "UI/Close";
    public const string UI_Back = "UI/Back";
    public const string UI_Toggle = "UI/Toggle";
    public const string UI_Achievement = "UI/Achievement";
    public const string Player_LevelUp = "Player/LevelUp";
    public const string Player_XPPickup = "Player/XP_Pickup";
    public const string Player_CoinPickup = "Player/Coin_Pickup";
    public const string Player_LowHealth = "Player/LowHealth";   // looping heartbeat warning
    public const string Player_Land = "Player/Land";
    public const string Player_Crit = "Player/Crit";
    public const string Enemy_Spawn = "Enemy/Spawn";             // rise-from-ground
    public const string Boss_Slam = "Enemy/Boss/Slam";
    public const string Boss_Enrage = "Enemy/Boss/Enrage";
    public const string Env_TreeFall = "ENV/TreeFall";
    public const string Env_StoneBreak = "ENV/StoneBreak";
    public const string Region_AnchorDestroy = "UI/AnchorDestroy";
    public const string Region_PurifyComplete = "UI/PurifyComplete";
}

[System.Serializable]
public class SoundGroup
{
    public EventReference fmodEvent;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("=== UI SOUNDS ===")]
    public SoundGroup uiClick;
    public SoundGroup uiHover;
    public SoundGroup uiQuestAccept;
    public SoundGroup uiQuestComplete;
    public SoundGroup uiError;
    public SoundGroup uiLevelUp;
    public SoundGroup uiPurchase;
    public SoundGroup uiGameOver;

    [Header("=== PLAYER SOUNDS ===")]
    public SoundGroup playerDash;
    public SoundGroup playerSwing;
    public SoundGroup playerHitEnemySword;
    public SoundGroup playerHitEnemyBow;
    public SoundGroup playerHitEnemyHammer;
    public SoundGroup playerHitResourceStone;
    public SoundGroup playerHitResourceWood;
    public SoundGroup playerHurt;
    public SoundGroup playerThrow;
    public SoundGroup playerHeal;
    public SoundGroup playerFootstep;
    public SoundGroup explosion;

    [Header("=== ENEMY SOUNDS ===")]
    public SoundGroup enemyAgro;
    public SoundGroup enemyTelegraph;
    public SoundGroup enemyAttack;
    public SoundGroup enemyHurt;
    public SoundGroup enemyDie;
    public SoundGroup enemyFootstep;
    public SoundGroup enemyHit;
    [Tooltip("Bow release / arrow launch — played by the archer when it fires.")]
    public SoundGroup arrowFire;
    [Tooltip("Arrow impact — played where the arrow lands / hits the player.")]
    public SoundGroup arrowHit;

    [Header("=== ENVIRONMENT & CAMP ===")]
    public SoundGroup campCollectItem;
    public SoundGroup campCollectGem;
    public SoundGroup campBuildStart;
    public SoundGroup campBuildDone;
    public SoundGroup npcWork;
    public SoundGroup envThunder;
    public SoundGroup envChestOpen;

    [Header("=== ANIMALS ===")]
    public SoundGroup animalCatMeow;
    public SoundGroup animalChicken;

    [Header("=== REGION + BOSS CINEMATIC ===")]
    public SoundGroup bossRoar;
    public SoundGroup bossStagger;
    public SoundGroup bossExecute;
    public SoundGroup regionVictoryStinger;
    public SoundGroup regionShockwave;
    public SoundGroup cinematicWhoosh;

    [Header("=== GAMEPLAY FEEL ===")]
    public SoundGroup encounterCleared;
    public SoundGroup totemActivate;
    public SoundGroup playerPerfectDodge;

    [Header("=== AMBIENT SOUNDSCAPE ===")]
    public SoundGroup ambientWind;
    [Tooltip("Looping fire crackle for the camp bonfire. Wire an FMOD fire-loop event or CampfireAudio stays silent.")]
    public SoundGroup ambientCampFire;
    public SoundGroup ambientHowl;
    public SoundGroup ambientCrow;
    public SoundGroup ambientDistantThunder;
    public SoundGroup ambientLeafRustle;
    [Tooltip("Looping rain bed — wire an FMOD event set to loop.")]
    public SoundGroup ambientRain;

    [Header("=== NEW / MISSING SOUNDS (wire FMOD events) ===")]
    public SoundGroup uiOpen;
    public SoundGroup uiClose;
    public SoundGroup uiBack;
    public SoundGroup uiToggle;
    public SoundGroup uiAchievement;
    public SoundGroup playerLevelUp;
    public SoundGroup playerXpPickup;
    public SoundGroup playerCoinPickup;
    public SoundGroup playerLowHealth;
    public SoundGroup playerLand;
    public SoundGroup playerCrit;
    public SoundGroup enemySpawn;
    public SoundGroup bossSlam;
    public SoundGroup bossEnrage;
    public SoundGroup envTreeFall;
    public SoundGroup envStoneBreak;
    public SoundGroup regionAnchorDestroy;
    public SoundGroup regionPurifyComplete;

    [Header("=== MUSIC ===")]
    public SoundGroup musicCamp;
    public SoundGroup musicBattle;
    public SoundGroup musicLevel1;

    [Header("=== DIALOGUES ===")]
    public SoundGroup dialogue1;
    public SoundGroup dialogue2;
    public SoundGroup dialogue3;
    public SoundGroup dialogue4;
    public SoundGroup dialogue5;
    public SoundGroup dialogue6;
    public SoundGroup dialogue7;
    public SoundGroup dialogue8;
    public SoundGroup dialogue9;
    public SoundGroup dialogue10;

    private Dictionary<string, SoundGroup> sfxDictionary;
    private EventInstance currentMusicInstance;
    private string currentMusicName;

    private FMOD.Studio.Bus masterBus;
    private FMOD.Studio.Bus musicBus;
    private FMOD.Studio.Bus sfxBus;
    private FMOD.Studio.Bus uiBus;
    private FMOD.Studio.Bus ambientBus;
    private FMOD.Studio.Bus voiceBus;

    // Cache the settings-driven bus volumes so runtime fades (music
    // crossfade, focus loss, dialogue ducking) can multiply them
    // without permanently trampling the user's slider values.
    private float musicUserVol = 1f;
    private float masterUserVol = 1f;
    private float musicDuckMultiplier = 1f;
    private float masterFadeMultiplier = 1f;

    private Coroutine masterFadeRoutine;
    private Coroutine musicFadeRoutine;
    private Coroutine musicDuckRoutine;

    // Long-running / looped SFX instances keyed by an integer handle.
    // Callers keep the handle around and pass it to StopLoopingSFX so
    // the sound can be cleanly stopped (with fade) — used for things
    // like the camp-building loop that would otherwise keep hammering
    // after construction ended, or the bush-rustle that outlived the
    // bush.
    private readonly Dictionary<int, EventInstance> loopedInstances = new Dictionary<int, EventInstance>();
    // Single tracked instance per accidentally-looping "one-shot" event (see PlaySFX).
    private readonly Dictionary<string, EventInstance> _loopingBeds = new Dictionary<string, EventInstance>();
    private int nextLoopId = 1;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            // A second AudioManager exists in this scene. The first one to load
            // (e.g. the BootLogo instance) wins and persists via DontDestroyOnLoad
            // — but if THIS duplicate was authored with FMOD events the survivor
            // lacks (because the survivor's scene serialization went stale after
            // new SoundGroup fields were added), donate them before dying. This
            // makes audio self-healing regardless of which scene loads first.
            Instance.AdoptMissingEvents(this);
            Destroy(gameObject);
            return;
        }

        InitializeDictionaries();
    }

    // Copy any wired FMOD events from `other` into slots this instance is
    // missing (null group or null event). Only fills gaps — never overwrites
    // an already-wired event.
    private void AdoptMissingEvents(AudioManager other)
    {
        if (other == null) return;
        var otherDict = other.BuildDictionarySnapshot();
        if (sfxDictionary == null) InitializeDictionaries();
        int adopted = 0;
        foreach (var kv in otherDict)
        {
            SoundGroup src = kv.Value;
            if (src == null || src.fmodEvent.IsNull) continue;
            if (!sfxDictionary.TryGetValue(kv.Key, out SoundGroup mine)) continue;
            if (mine == null) { sfxDictionary[kv.Key] = src; adopted++; }
            else if (mine.fmodEvent.IsNull) { mine.fmodEvent = src.fmodEvent; adopted++; }
        }
        if (adopted > 0)
        {
            Debug.LogWarning($"[AudioManager] Adopted {adopted} missing FMOD event(s) from a duplicate instance — the persistent AudioManager's scene serialization is stale. Re-open that scene and re-apply the AudioManager once to make this permanent.");
            if (RuntimeManager.IsInitialized) PreloadAllSampleData();
        }
    }

    // Build a name→SoundGroup map straight from this component's serialized
    // fields (mirrors InitializeDictionaries) without disturbing state.
    private Dictionary<string, SoundGroup> BuildDictionarySnapshot()
    {
        var d = sfxDictionary;
        if (d != null) return d;
        InitializeDictionaries();
        return sfxDictionary;
    }

    private IEnumerator Start()
    {
        // Wait until FMOD is initialised before touching buses. On WebGL banks
        // load ASYNCHRONOUSLY, so the old plain Start() fetched the buses and set
        // their volume BEFORE they existed — the saved volumes were lost and
        // everything played quiet + stuttery until the player wiggled a slider
        // (which re-set the volume once the buses were finally valid).
        float t = 0f;
        while (!RuntimeManager.IsInitialized && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }

        masterBus = RuntimeManager.GetBus("bus:/");
        musicBus = RuntimeManager.GetBus("bus:/Music");
        sfxBus = RuntimeManager.GetBus("bus:/Sound FX");
        uiBus = RuntimeManager.GetBus("bus:/Ui");
        ambientBus = RuntimeManager.GetBus("bus:/Ambient");
        voiceBus = RuntimeManager.GetBus("bus:/Voice");

        // Give the buses a moment to resolve valid before applying volumes.
        t = 0f;
        while (!masterBus.isValid() && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }

        RuntimeManager.StudioSystem.getBankCount(out int bankCount);
        if (bankCount <= 1)
            Debug.LogWarning($"[AudioManager] FMOD reports {bankCount} bank(s) loaded. SFX may be silent until FMOD → Import Banks is run.");

        PreloadAllSampleData();
        LoadAudioSettings();   // now the saved Settings_*Vol land on valid buses
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Kill any looping SFX carried over from the previous scene (region rain
        // / storm ambience, enemy vocals, etc.) BEFORE the new scene starts its
        // own — otherwise they bleed through (rain heard in the camp).
        StopAllLoopedSFX();

        // Match every gameplay context to a track so the game is never
        // silent on scene entry. Menu / Shop / camp share the calmer
        // camp theme; combat scenes get the battle score.
        switch (scene.name)
        {
            case "CampScene":
            case "ShopScene":
                PlayMusic(AudioID.Music_Camp);
                break;
            case "Menu":
                // ААА Архітектура: Аудіоменеджер перевіряє стан гри.
                // Якщо інтро ще НЕ грало в цій сесії, ми мовчимо і чекаємо,
                // поки IntroCinematicManager сам не увімкне музику після завершення.
                if (IntroCinematicManager.HasPlayedThisSession)
                {
                    PlayMusic(AudioID.Music_Camp);
                }
                break;
            case "Lvl_1":
                // Level 1 gets its own theme; falls back to the battle score
                // until the dedicated FMOD event is authored.
                PlayMusicOrFallback(AudioID.Music_Level1, AudioID.Music_Battle);
                break;
            case "GameScene":
                PlayMusic(AudioID.Music_Battle);
                break;
        }
    }

    // Walk every SoundGroup once and pre-load its sample data. FMOD's
    // AutomaticSampleLoading is disabled in this project's settings, so
    // PlayOneShot on an unloaded sample plays as silence until the load
    // finishes — for very short events (footsteps, hits, clicks) the
    // one-shot ends before the audio ever reaches the mixer, which is
    // why the same key sometimes made sound and sometimes didn't.
    // Also logs every event that fails to resolve so misconfigured FMOD
    // references become visible in the Console instead of silent.
    private void PreloadAllSampleData()
    {
        if (sfxDictionary == null) return;
        int loaded = 0, failed = 0;
        var reported = new HashSet<System.Guid>();
        foreach (var kv in sfxDictionary)
        {
            SoundGroup g = kv.Value;
            if (g == null || g.fmodEvent.IsNull) continue;
            System.Guid gidNet;
            try { gidNet = System.Guid.Parse(g.fmodEvent.Guid.ToString()); } catch { gidNet = System.Guid.Empty; }
            if (!reported.Add(gidNet)) continue; // avoid loading same event twice

            FMOD.RESULT r = RuntimeManager.StudioSystem.getEventByID(g.fmodEvent.Guid, out FMOD.Studio.EventDescription desc);
            if (r != FMOD.RESULT.OK || !desc.isValid())
            {
                Debug.LogWarning($"[AudioManager] Event '{kv.Key}' GUID {g.fmodEvent.Guid} does NOT resolve: {r}. Bank not loaded or GUID stale — reimport banks via FMOD → Import Banks.");
                failed++;
                continue;
            }
            FMOD.RESULT lr = desc.loadSampleData();
            if (lr != FMOD.RESULT.OK)
                Debug.LogWarning($"[AudioManager] loadSampleData for '{kv.Key}' returned {lr} — sample may play silent on first use.");
            else
                loaded++;
        }
        GameLog.Info($"[AudioManager] Preloaded {loaded} FMOD event(s), {failed} failed to resolve.");
    }

    private void LoadAudioSettings()
    {
        // Persisted volumes use the same 0-100 scale as SettingsUI/SettingsApplier.
        // Defaults leave headroom below max so nothing clips out of the box
        // — master 80, music 65, sfx 90, ui 70, ambient 75, voice 100.
        float masterVol = PlayerPrefs.GetFloat("Settings_MasterVol", 80f) / 100f;
        float musicVol = PlayerPrefs.GetFloat("Settings_MusicVol", 65f) / 100f;
        float sfxVol = PlayerPrefs.GetFloat("Settings_SFXVol", 90f) / 100f;
        float uiVol = PlayerPrefs.GetFloat("Settings_UIVol", 70f) / 100f;
        float ambientVol = PlayerPrefs.GetFloat("Settings_AmbientVol", 75f) / 100f;
        float voiceVol = PlayerPrefs.GetFloat("Settings_VoiceVol", 100f) / 100f;

        masterUserVol = masterVol;
        musicUserVol = musicVol;
        _musicUserScale = Mathf.Clamp01(musicVol); // apply Music slider to the instance too

        masterBus.setVolume(masterVol * masterFadeMultiplier);
        musicBus.setVolume(musicVol * musicDuckMultiplier);
        sfxBus.setVolume(sfxVol);
        uiBus.setVolume(uiVol);
        ambientBus.setVolume(ambientVol);
        voiceBus.setVolume(voiceVol);
    }

    // Set* methods take a normalized 0-1 volume and only update the FMOD bus.
    // Persistence is owned by SettingsUI (in 0-100 scale) — we must NOT write
    // back here or we'd corrupt the stored value into a 0-1 float that the
    // slider then reads as ~0% on the next launch.
    public void SetMasterVolume(float vol) { masterUserVol = vol; masterBus.setVolume(vol * masterFadeMultiplier); }
    public void SetMusicVolume(float vol)
    {
        musicUserVol = vol;
        musicBus.setVolume(vol * musicDuckMultiplier);
        // The music FMOD events aren't routed through bus:/Music (they only
        // respond to Master), so the bus set above does nothing to them. Apply
        // the user's Music volume straight onto the current music instance too.
        _musicUserScale = Mathf.Clamp01(vol);
        ApplyMusicInstanceVolume();
    }
    public void SetSFXVolume(float vol) { sfxBus.setVolume(vol); }
    public void SetUIVolume(float vol) { uiBus.setVolume(vol); }
    public void SetAmbientVolume(float vol) { ambientBus.setVolume(vol); }
    public void SetVoiceVolume(float vol) { voiceBus.setVolume(vol); }

    // Smoothly ramp the master bus to `targetMultiplier * user volume`.
    // Used by focus loss / recovery so Alt-Tab no longer hard-snaps
    // audio in and out. Runs on unscaled time so it still eases when
    // the game is paused.
    public void FadeMasterVolume(float targetMultiplier, float duration)
    {
        if (!masterBus.isValid()) return;
        if (masterFadeRoutine != null) StopCoroutine(masterFadeRoutine);
        masterFadeRoutine = StartCoroutine(FadeMasterRoutine(targetMultiplier, Mathf.Max(0.01f, duration)));
    }

    private IEnumerator FadeMasterRoutine(float target, float duration)
    {
        float start = masterFadeMultiplier;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            masterFadeMultiplier = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
            masterBus.setVolume(masterUserVol * masterFadeMultiplier);
            yield return null;
        }
        masterFadeMultiplier = target;
        masterBus.setVolume(masterUserVol * masterFadeMultiplier);
        masterFadeRoutine = null;
    }

    // Duck the music bus to `duckLevel` (0-1) of the user's set volume
    // for the given hold duration, then release. Used by the death
    // cinematic + dialogue system so voiceover isn't fighting the
    // score. Zero or negative `holdSeconds` keeps ducking until
    // UnduckMusic is called.
    public void DuckMusic(float duckLevel, float fadeIn, float holdSeconds, float fadeOut)
    {
        if (!musicBus.isValid()) return;
        if (musicDuckRoutine != null) StopCoroutine(musicDuckRoutine);
        musicDuckRoutine = StartCoroutine(MusicDuckRoutine(Mathf.Clamp01(duckLevel), fadeIn, holdSeconds, fadeOut));
    }

    public void UnduckMusic(float fadeOut = 0.6f)
    {
        if (!musicBus.isValid()) return;
        if (musicDuckRoutine != null) StopCoroutine(musicDuckRoutine);
        musicDuckRoutine = StartCoroutine(RampMusicMultiplier(1f, Mathf.Max(0.01f, fadeOut)));
    }

    // Duck the CURRENT music EventInstance directly. Unlike DuckMusic (which
    // lowers the music BUS), this works even when a track isn't routed through
    // that bus — e.g. the intro cutscene music, which only responded to Master.
    // Implemented as a PERSISTENT multiplier so the music crossfade (which sets
    // the instance volume every frame) can't override it. Held until unducked.
    private Coroutine musicInstanceDuckRoutine;
    private float _musicBaseVol = 1f;      // owned by the crossfade (fade-in level)
    private float _musicInstanceDuck = 1f; // owned by the duck
    private float _musicUserScale = 1f;    // owned by the Music volume slider

    // Whenever any factor changes, re-apply the combined volume.
    private void ApplyMusicInstanceVolume()
    {
        if (currentMusicInstance.isValid())
            currentMusicInstance.setVolume(Mathf.Clamp01(_musicBaseVol) * Mathf.Clamp01(_musicInstanceDuck) * Mathf.Clamp01(_musicUserScale));
    }

    public void DuckMusicInstance(float level, float fade = 0.5f)
    {
        if (musicInstanceDuckRoutine != null) StopCoroutine(musicInstanceDuckRoutine);
        musicInstanceDuckRoutine = StartCoroutine(RampMusicInstance(Mathf.Clamp01(level), Mathf.Max(0.01f, fade)));
    }

    public void UnduckMusicInstance(float fade = 0.6f)
    {
        if (musicInstanceDuckRoutine != null) StopCoroutine(musicInstanceDuckRoutine);
        musicInstanceDuckRoutine = StartCoroutine(RampMusicInstance(1f, Mathf.Max(0.01f, fade)));
    }

    private IEnumerator RampMusicInstance(float target, float duration)
    {
        float start = _musicInstanceDuck;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _musicInstanceDuck = Mathf.Lerp(start, target, t / duration);
            ApplyMusicInstanceVolume();
            yield return null;
        }
        _musicInstanceDuck = target;
        ApplyMusicInstanceVolume();
        musicInstanceDuckRoutine = null;
    }

    // ---- Combat music: swap to the battle track while the player is fighting,
    // then crossfade back to whatever was playing once the fight is over. Call
    // NotifyCombat() from combat events; it keeps the battle track alive for
    // `sustain` seconds and reverts automatically after the last one.
    private float _combatUntil = -1f;
    private string _combatBaseTrack;
    private bool _inCombatMusic;

    public void NotifyCombat(float sustain = 6f)
    {
        _combatUntil = Time.unscaledTime + sustain;
        if (_inCombatMusic) return;
        _inCombatMusic = true;
        if (currentMusicName == AudioID.Music_Battle) return; // already battle (e.g. GameScene)
        _combatBaseTrack = currentMusicName;                  // remember what to return to
        PlayMusic(AudioID.Music_Battle);
    }

    private void Update()
    {
        if (_inCombatMusic && Time.unscaledTime > _combatUntil)
        {
            _inCombatMusic = false;
            if (!string.IsNullOrEmpty(_combatBaseTrack) && _combatBaseTrack != AudioID.Music_Battle)
                PlayMusic(_combatBaseTrack);
            _combatBaseTrack = null;
        }
    }

    private IEnumerator MusicDuckRoutine(float target, float fadeIn, float hold, float fadeOut)
    {
        yield return StartCoroutine(RampMusicMultiplier(target, Mathf.Max(0.01f, fadeIn)));
        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
        if (hold > 0f) yield return StartCoroutine(RampMusicMultiplier(1f, Mathf.Max(0.01f, fadeOut)));
        musicDuckRoutine = null;
    }

    private IEnumerator RampMusicMultiplier(float target, float duration)
    {
        float start = musicDuckMultiplier;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            musicDuckMultiplier = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
            musicBus.setVolume(musicUserVol * musicDuckMultiplier);
            yield return null;
        }
        musicDuckMultiplier = target;
        musicBus.setVolume(musicUserVol * musicDuckMultiplier);
    }

    private void InitializeDictionaries()
    {
        sfxDictionary = new Dictionary<string, SoundGroup>();

        sfxDictionary.Add(AudioID.UI_Click, uiClick);
        sfxDictionary.Add(AudioID.UI_Hover, uiHover);
        sfxDictionary.Add(AudioID.UI_QuestAccept, uiQuestAccept);
        sfxDictionary.Add(AudioID.UI_QuestComplete, uiQuestComplete);
        sfxDictionary.Add(AudioID.UI_Error, uiError);
        sfxDictionary.Add(AudioID.UI_LevelUp, uiLevelUp);
        sfxDictionary.Add(AudioID.UI_Purchase, uiPurchase);
        sfxDictionary.Add(AudioID.UI_GameOver, uiGameOver);

        sfxDictionary.Add(AudioID.Player_Dash, playerDash);
        sfxDictionary.Add(AudioID.Player_Swing, playerSwing);
        sfxDictionary.Add(AudioID.Player_HitEnemy_Sword, playerHitEnemySword);
        sfxDictionary.Add(AudioID.Player_HitEnemy_Bow, playerHitEnemyBow);
        sfxDictionary.Add(AudioID.Player_HitEnemy_Hammer, playerHitEnemyHammer);
        sfxDictionary.Add(AudioID.Player_HitResource_Stone, playerHitResourceStone);
        sfxDictionary.Add(AudioID.Player_HitResource_Wood, playerHitResourceWood);
        sfxDictionary.Add(AudioID.Player_Hurt, playerHurt);
        sfxDictionary.Add(AudioID.Player_Throw, playerThrow);
        sfxDictionary.Add(AudioID.Player_Heal, playerHeal);
        sfxDictionary.Add(AudioID.Player_Footstep, playerFootstep);
        sfxDictionary.Add(AudioID.Explosion, explosion);

        // Kompatybilność wsteczna dodana bez powtórzeń w słowniku
        sfxDictionary.Add(AudioID.Player_HitEnemy, playerHitEnemySword);
        sfxDictionary.Add(AudioID.Player_HitResource, playerHitResourceWood);

        sfxDictionary.Add(AudioID.Enemy_Agro, enemyAgro);
        sfxDictionary.Add(AudioID.Enemy_Telegraph, enemyTelegraph);
        sfxDictionary.Add(AudioID.Enemy_Attack, enemyAttack);
        sfxDictionary.Add(AudioID.Enemy_Hurt, enemyHurt);
        sfxDictionary.Add(AudioID.Enemy_Die, enemyDie);
        sfxDictionary.Add(AudioID.Enemy_Footstep, enemyFootstep);
        sfxDictionary.Add(AudioID.Enemy_Hit, enemyHit);
        sfxDictionary.Add(AudioID.Arrow_Fire, arrowFire);
        sfxDictionary.Add(AudioID.Arrow_Hit, arrowHit);

        sfxDictionary.Add(AudioID.Camp_CollectItem, campCollectItem);
        sfxDictionary.Add(AudioID.Camp_CollectGem, campCollectGem);
        sfxDictionary.Add(AudioID.Camp_BuildStart, campBuildStart);
        sfxDictionary.Add(AudioID.Camp_BuildDone, campBuildDone);
        sfxDictionary.Add(AudioID.NPC_Work, npcWork);
        sfxDictionary.Add(AudioID.Env_Thunder, envThunder);
        sfxDictionary.Add(AudioID.Env_ChestOpen, envChestOpen);

        sfxDictionary.Add(AudioID.Animal_CatMeow, animalCatMeow);
        sfxDictionary.Add(AudioID.Animal_Chicken, animalChicken);

        sfxDictionary.Add(AudioID.Boss_Roar, bossRoar);
        sfxDictionary.Add(AudioID.Boss_Stagger, bossStagger);
        sfxDictionary.Add(AudioID.Boss_Execute, bossExecute);
        sfxDictionary.Add(AudioID.Region_VictoryStinger, regionVictoryStinger);
        sfxDictionary.Add(AudioID.Region_Shockwave, regionShockwave);
        sfxDictionary.Add(AudioID.Cinematic_Whoosh, cinematicWhoosh);

        sfxDictionary.Add(AudioID.Encounter_Cleared, encounterCleared);
        sfxDictionary.Add(AudioID.Totem_Activate, totemActivate);
        sfxDictionary.Add(AudioID.Player_PerfectDodge, playerPerfectDodge);

        sfxDictionary.Add(AudioID.Ambient_Wind, ambientWind);
        sfxDictionary.Add(AudioID.Ambient_CampFire, ambientCampFire);
        sfxDictionary.Add(AudioID.Ambient_Howl, ambientHowl);
        sfxDictionary.Add(AudioID.Ambient_Crow, ambientCrow);
        sfxDictionary.Add(AudioID.Ambient_DistantThunder, ambientDistantThunder);
        sfxDictionary.Add(AudioID.Ambient_LeafRustle, ambientLeafRustle);
        sfxDictionary.Add(AudioID.Ambient_Rain, ambientRain);

        // New / previously-missing sounds.
        sfxDictionary.Add(AudioID.UI_Open, uiOpen);
        sfxDictionary.Add(AudioID.UI_Close, uiClose);
        sfxDictionary.Add(AudioID.UI_Back, uiBack);
        sfxDictionary.Add(AudioID.UI_Toggle, uiToggle);
        sfxDictionary.Add(AudioID.UI_Achievement, uiAchievement);
        sfxDictionary.Add(AudioID.Player_LevelUp, playerLevelUp);
        sfxDictionary.Add(AudioID.Player_XPPickup, playerXpPickup);
        sfxDictionary.Add(AudioID.Player_CoinPickup, playerCoinPickup);
        sfxDictionary.Add(AudioID.Player_LowHealth, playerLowHealth);
        sfxDictionary.Add(AudioID.Player_Land, playerLand);
        sfxDictionary.Add(AudioID.Player_Crit, playerCrit);
        sfxDictionary.Add(AudioID.Enemy_Spawn, enemySpawn);
        sfxDictionary.Add(AudioID.Boss_Slam, bossSlam);
        sfxDictionary.Add(AudioID.Boss_Enrage, bossEnrage);
        sfxDictionary.Add(AudioID.Env_TreeFall, envTreeFall);
        sfxDictionary.Add(AudioID.Env_StoneBreak, envStoneBreak);
        sfxDictionary.Add(AudioID.Region_AnchorDestroy, regionAnchorDestroy);
        sfxDictionary.Add(AudioID.Region_PurifyComplete, regionPurifyComplete);

        sfxDictionary.Add(AudioID.Music_Camp, musicCamp);
        sfxDictionary.Add(AudioID.Music_Battle, musicBattle);
        sfxDictionary.Add(AudioID.Music_Level1, musicLevel1);

        sfxDictionary.Add("Dialogue/Dialogue1", dialogue1);
        sfxDictionary.Add("Dialogue/Dialogue2", dialogue2);
        sfxDictionary.Add("Dialogue/Dialogue3", dialogue3);
        sfxDictionary.Add("Dialogue/Dialogue4", dialogue4);
        sfxDictionary.Add("Dialogue/Dialogue5", dialogue5);
        sfxDictionary.Add("Dialogue/Dialogue6", dialogue6);
        sfxDictionary.Add("Dialogue/Dialogue7", dialogue7);
        sfxDictionary.Add("Dialogue/Dialogue8", dialogue8);
        sfxDictionary.Add("Dialogue/Dialogue9", dialogue9);
        sfxDictionary.Add("Dialogue/Dialogue10", dialogue10);
    }

    public void PlayUI(string soundName) { PlaySFX(soundName); }

    // Graceful fallbacks: a handful of AudioIDs have no dedicated FMOD event
    // authored yet (arrow impact, XP/coin pickup, boss slam/enrage). Rather
    // than play silence at those moments, route them to the nearest wired
    // event so the action always has audible feedback. Remove an entry once
    // its real event is wired in the AudioManager inspector.
    private static readonly Dictionary<string, string> s_sfxFallback = new Dictionary<string, string>
    {
        { AudioID.Arrow_Hit,          AudioID.Enemy_Hit },
        { AudioID.Player_XPPickup,    AudioID.Camp_CollectGem },
        { AudioID.Player_CoinPickup,  AudioID.Camp_CollectItem },
        { AudioID.Boss_Slam,          AudioID.Region_Shockwave },
        { AudioID.Boss_Enrage,        AudioID.Boss_Roar },
    };

    // Play a sound EXACTLY once, guaranteed to stop after `maxSeconds`, even if
    // the FMOD event was authored looping. Regular PlaySFX routes a looping
    // event into _loopingBeds where it repeats until the scene changes — which
    // is why the victory stinger "played many times". This creates a private
    // tracked instance and force-stops it.
    public void PlaySFXOnce(string soundName, float maxSeconds = 5f)
    {
        if (sfxDictionary == null || !sfxDictionary.TryGetValue(soundName, out SoundGroup group)) return;
        if (group == null || group.fmodEvent.IsNull) return;
        FMOD.RESULT r = RuntimeManager.StudioSystem.getEventByID(group.fmodEvent.Guid, out FMOD.Studio.EventDescription desc);
        if (r != FMOD.RESULT.OK || !desc.isValid()) return;
        var inst = RuntimeManager.CreateInstance(group.fmodEvent);
        if (!inst.isValid()) return;
        inst.start();
        // Stop after ONE playthrough. The event is authored looping, so if we
        // waited a fixed 6s it repeated ~6×. Use the event's authored length so
        // exactly one pass plays, capped by maxSeconds.
        float stopAfter = maxSeconds;
        if (desc.getLength(out int lenMs) == FMOD.RESULT.OK && lenMs > 0)
            stopAfter = Mathf.Min(maxSeconds, lenMs / 1000f + 0.05f);
        StartCoroutine(StopInstanceAfter(inst, Mathf.Max(0.1f, stopAfter)));
    }

    private IEnumerator StopInstanceAfter(EventInstance inst, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (inst.isValid())
        {
            inst.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            inst.release();
        }
    }

    // Warn once per missing sound so a broken bank / unwired SoundGroup
    // stops being invisible. Previously PlaySFX silently returned when
    // the FMOD event was null — that made "no enemy sounds" impossible
    // to diagnose from console alone.
    private HashSet<string> warnedMissingSounds;

    public void PlaySFX(string soundName)
    {
        PlaySFX(soundName, allowFallback: true);
    }

    private void PlaySFX(string soundName, bool allowFallback)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group))
        {
            // Unwired event → try the nearest wired fallback once, so the
            // action isn't silent while its dedicated event is unauthored.
            if ((group == null || group.fmodEvent.IsNull) && allowFallback
                && s_sfxFallback.TryGetValue(soundName, out string fb))
            {
                PlaySFX(fb, allowFallback: false);
                return;
            }
            if (group != null && !group.fmodEvent.IsNull)
            {
                FMOD.RESULT r = RuntimeManager.StudioSystem.getEventByID(group.fmodEvent.Guid, out FMOD.Studio.EventDescription desc);
                if (r != FMOD.RESULT.OK || !desc.isValid())
                {
                    WarnMissing(soundName, $"FMOD event GUID {group.fmodEvent.Guid} resolves with error {r} — bank not loaded, or GUID stale. Reimport banks from FMOD → Import Banks menu.");
                    return;
                }
                // Guard: a LOOPING event (e.g. AMB_Wind is authored looping) fired
                // via PlayOneShot spawns an immortal, untracked instance that stacks
                // and bleeds into the next scene (that was "rain still in the camp").
                // Play ONE tracked instance instead so it can be stopped on scene
                // change, and never stack duplicates.
                if (desc.isOneshot(out bool oneshot) == FMOD.RESULT.OK && !oneshot)
                {
                    if (_loopingBeds.TryGetValue(soundName, out var existing) && existing.isValid())
                    {
                        existing.getPlaybackState(out var st);
                        if (st != FMOD.Studio.PLAYBACK_STATE.STOPPED) return; // already playing → no stacking
                    }
                    var inst = RuntimeManager.CreateInstance(group.fmodEvent);
                    inst.start();
                    _loopingBeds[soundName] = inst;
                    return;
                }
                RuntimeManager.PlayOneShot(group.fmodEvent);
                return;
            }
            WarnMissing(soundName, "SoundGroup fmodEvent is null — assign it in the AudioManager inspector or check the FMOD bank");
        }
        else
        {
            WarnMissing(soundName, "no dictionary entry — key not registered in InitializeDictionaries");
        }
    }

    private void WarnMissing(string soundName, string reason)
    {
        if (warnedMissingSounds == null) warnedMissingSounds = new HashSet<string>();
        if (warnedMissingSounds.Add(soundName))
            Debug.LogWarning($"[AudioManager] PlaySFX('{soundName}') failed: {reason}. Further warnings for this key are suppressed.");
    }

    // Low-health heartbeat: a single looping warning instance the player HUD
    // toggles on/off as HP crosses the danger threshold. Kept separate from the
    // pooled looped SFX so it can be driven by a simple bool without handles.
    private EventInstance _lowHealthInst;
    public void SetLowHealthWarning(bool active)
    {
        if (active)
        {
            if (_lowHealthInst.isValid()) return; // already warning
            if (sfxDictionary != null && sfxDictionary.TryGetValue(AudioID.Player_LowHealth, out SoundGroup g)
                && g != null && !g.fmodEvent.IsNull)
            {
                _lowHealthInst = RuntimeManager.CreateInstance(g.fmodEvent);
                _lowHealthInst.start();
            }
        }
        else if (_lowHealthInst.isValid())
        {
            _lowHealthInst.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            _lowHealthInst.release();
            _lowHealthInst = default;
        }
    }

    public void PlaySFX3D(string soundName, Vector3 position)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent, position);
        }
    }

    // Create a tracked EventInstance and start it 3D at `position`. Returns
    // a handle the caller keeps around for StopLoopingSFX. Suitable for
    // build hammers, ambient loops, chopping SFX etc. — anything that
    // needs an explicit end and should NOT keep playing past the moment
    // that triggered it. Returns -1 if the sound is unregistered or the
    // FMOD event is missing.
    public int PlayLoopingSFX3D(string soundName, Vector3 position)
    {
        if (!sfxDictionary.TryGetValue(soundName, out SoundGroup group) || group == null || group.fmodEvent.IsNull)
        {
            WarnMissing(soundName, "PlayLoopingSFX3D: no valid FMOD event");
            return -1;
        }
        EventInstance inst = RuntimeManager.CreateInstance(group.fmodEvent);
        if (!inst.isValid())
        {
            WarnMissing(soundName, "PlayLoopingSFX3D: CreateInstance failed");
            return -1;
        }
        inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(position));
        inst.start();

        int id = nextLoopId++;
        loopedInstances[id] = inst;
        return id;
    }

    // Follow-a-transform variant — updates 3D attributes each frame via
    // FMOD's built-in AttachInstanceToGameObject helper. Use this for
    // moving sources (walking NPC, roaming enemy).
    public int PlayLoopingSFX3D(string soundName, Transform followTarget)
    {
        if (followTarget == null) return PlayLoopingSFX3D(soundName, Vector3.zero);
        int id = PlayLoopingSFX3D(soundName, followTarget.position);
        if (id != -1 && loopedInstances.TryGetValue(id, out EventInstance inst))
        {
            Rigidbody rb = followTarget.GetComponent<Rigidbody>();
            RuntimeManager.AttachInstanceToGameObject(inst, followTarget, rb);
        }
        return id;
    }

    // Stop a looping instance with an optional manual volume fade before
    // release. FMOD's ALLOWFADEOUT only applies the AHDSR release the sound
    // designer authored — if the event has none, the sound would still
    // snap off. The manual fade guarantees a graceful tail.
    // Stop + release EVERY looping SFX instance. Called on scene change so a
    // region loop (rain/storm ambience, enemy vocals, build/campfire loops)
    // can't bleed into the next scene — the AudioManager is DontDestroyOnLoad,
    // so without this its looped instances survive the scene that started them
    // (that was the "rain still playing in the camp" bug).
    public void StopAllLoopedSFX()
    {
        foreach (var kv in loopedInstances)
        {
            EventInstance inst = kv.Value;
            if (!inst.isValid()) continue;
            inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            inst.release();
        }
        loopedInstances.Clear();
        // Also kill any looping "one-shot" beds (AMB_Wind etc.).
        foreach (var kv in _loopingBeds)
        {
            EventInstance inst = kv.Value;
            if (!inst.isValid()) continue;
            inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            inst.release();
        }
        _loopingBeds.Clear();
        // Kill the low-health heartbeat too so it can't bleed into a new scene.
        SetLowHealthWarning(false);
    }

    // Stop a looping "one-shot" bed started via PlaySFX (e.g. AMB_Rain) by its
    // AudioID, with a short fade. No-ops if it isn't currently playing.
    public void StopLoopedBed(string soundName, float fadeSeconds = 0.8f)
    {
        if (_loopingBeds.TryGetValue(soundName, out EventInstance inst) && inst.isValid())
        {
            _loopingBeds.Remove(soundName);
            if (fadeSeconds <= 0f) { inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); inst.release(); }
            else StartCoroutine(FadeAndStopInstanceRoutine(inst, fadeSeconds));
        }
    }

    public void StopLoopingSFX(int handle, float fadeSeconds = 0.4f)
    {
        if (handle < 0) return;
        if (!loopedInstances.TryGetValue(handle, out EventInstance inst)) return;
        loopedInstances.Remove(handle);
        if (!inst.isValid()) return;

        if (fadeSeconds <= 0f)
        {
            inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            inst.release();
        }
        else
        {
            StartCoroutine(FadeAndStopInstanceRoutine(inst, fadeSeconds));
        }
    }

    private IEnumerator FadeAndStopInstanceRoutine(EventInstance inst, float duration)
    {
        if (!inst.isValid()) yield break;
        inst.getVolume(out float startVol, out _);
        float t = 0f;
        while (t < duration && inst.isValid())
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            inst.setVolume(startVol * (k * k));
            yield return null;
        }
        if (inst.isValid())
        {
            inst.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            inst.release();
        }
    }

    // Play `primary` if its FMOD event is wired, otherwise `fallback`. Lets a
    // scene point at a new (not-yet-authored) track without going silent.
    public void PlayMusicOrFallback(string primary, string fallback)
    {
        if (sfxDictionary.TryGetValue(primary, out SoundGroup g) && g != null && !g.fmodEvent.IsNull)
            PlayMusic(primary);
        else
            PlayMusic(fallback);
    }

    // Drives an FMOD parameter named "Intensity" (0..1) on the current music
    // instance — author a matching parameter on a dynamic-music event to layer
    // in extra stems during surges/boss fights. Safe no-op if the event has no
    // such parameter, so it never errors on the simple tracks.
    private float _musicIntensity = 0f;
    private Coroutine _intensityRoutine;

    public void SetMusicIntensity(float value01)
    {
        value01 = Mathf.Clamp01(value01);
        // Ramp toward the target instead of snapping, so combat music swells and
        // eases smoothly rather than switching hard on the phase change.
        if (_intensityRoutine != null) StopCoroutine(_intensityRoutine);
        _intensityRoutine = StartCoroutine(RampMusicIntensity(value01, 2.5f));
    }

    private IEnumerator RampMusicIntensity(float target, float duration)
    {
        float start = _musicIntensity;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _musicIntensity = Mathf.Lerp(start, target, t / duration);
            if (currentMusicInstance.isValid())
                currentMusicInstance.setParameterByName("Intensity", _musicIntensity);
            yield return null;
        }
        _musicIntensity = target;
        if (currentMusicInstance.isValid())
            currentMusicInstance.setParameterByName("Intensity", target);
    }

    public void PlayMusic(string soundName)
    {
        if (currentMusicName == soundName && currentMusicInstance.isValid()) return;

        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
            EventInstance oldInstance = currentMusicInstance;
            currentMusicInstance = RuntimeManager.CreateInstance(group.fmodEvent);
            currentMusicName = soundName;
            musicFadeRoutine = StartCoroutine(MusicCrossfadeRoutine(oldInstance, currentMusicInstance, 1.5f));
        }
    }

    // Ramp the incoming FMOD instance from silence to full over `duration`
    // while releasing the outgoing one with ALLOWFADEOUT so any per-event
    // fade-out AHDSR gets its time. Without this the switch clashed hard
    // (menu → camp → battle all cut in at full volume on frame 0).
    private IEnumerator MusicCrossfadeRoutine(EventInstance outgoing, EventInstance incoming, float duration)
    {
        if (incoming.isValid())
        {
            _musicBaseVol = 0f;
            ApplyMusicInstanceVolume();   // respects any active duck multiplier
            incoming.start();
        }
        if (outgoing.isValid())
        {
            outgoing.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _musicBaseVol = Mathf.Clamp01(t / duration);
            ApplyMusicInstanceVolume();
            yield return null;
        }
        _musicBaseVol = 1f;
        ApplyMusicInstanceVolume();
        if (outgoing.isValid()) outgoing.release();

        musicFadeRoutine = null;
    }

    // Public helper for scripted stops (death cinematic, credits).
    public void StopMusic(float fadeSeconds = 1.5f)
    {
        if (!currentMusicInstance.isValid()) return;
        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        EventInstance target = currentMusicInstance;
        currentMusicInstance = default;
        currentMusicName = null;
        StartCoroutine(FadeAndReleaseRoutine(target, Mathf.Max(0.01f, fadeSeconds)));
    }

    private IEnumerator FadeAndReleaseRoutine(EventInstance inst, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (inst.isValid()) inst.setVolume(1f - Mathf.Clamp01(t / duration));
            yield return null;
        }
        if (inst.isValid()) { inst.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); inst.release(); }
    }

    public void PlayDialogue(int dialogueNumber)
    {
        // Dialogue voice-over is DISABLED per design — the narrator /
        // Elias VO was removed. Subtitles still show (call sites keep
        // passing the text), only the spoken audio is suppressed. To
        // re-enable, delete this early return and the FMOD slots play
        // again.
        return;

#pragma warning disable CS0162 // unreachable — kept for easy re-enable
        string key = "Dialogue/Dialogue" + dialogueNumber;
        if (sfxDictionary.TryGetValue(key, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
#pragma warning restore CS0162
    }
}