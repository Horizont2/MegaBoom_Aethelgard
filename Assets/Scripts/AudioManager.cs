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

    public const string Ambient_Wind = "AMB/AMB_Wind";
    // Fire crackle for the camp bonfire — needs an FMOD event at this
    // path; CampfireAudio will silently no-op if the event isn't wired.
    public const string Ambient_CampFire = "AMB/AMB_CampFire";
    public const string Ambient_Howl = "AMB/AMB_Howl";
    public const string Ambient_Crow = "AMB/AMB_Crow";
    public const string Ambient_DistantThunder = "AMB/AMB_Distant_Thunder";
    public const string Ambient_LeafRustle = "AMB/AMB_Leaf_Rustle";
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
    public SoundGroup ambientHowl;
    public SoundGroup ambientCrow;
    public SoundGroup ambientDistantThunder;
    public SoundGroup ambientLeafRustle;

    [Header("=== MUSIC ===")]
    public SoundGroup musicCamp;
    public SoundGroup musicBattle;

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
            Destroy(gameObject);
            return;
        }

        InitializeDictionaries();
    }

    private void Start()
    {
        // Diagnostic — if the FMOD banks aren't loaded, nothing below
        // will make sound and every PlaySFX call will emit a warning.
        // Print the loaded bank count once at startup so it's obvious
        // when the FMOD → Import Banks step was skipped.
        FMOD.RESULT bcRes = RuntimeManager.StudioSystem.getBankCount(out int bankCount);
        if (bcRes != FMOD.RESULT.OK || bankCount <= 1)
        {
            Debug.LogWarning($"[AudioManager] FMOD reports {bankCount} bank(s) loaded (result={bcRes}). Enemy / hit / build SFX will be silent until you run FMOD → Import Banks (or copy FMOD/Build/Desktop/*.bank into Assets/StreamingAssets/).");
        }

        masterBus = RuntimeManager.GetBus("bus:/");
        PreloadAllSampleData();
        musicBus = RuntimeManager.GetBus("bus:/Music");
        sfxBus = RuntimeManager.GetBus("bus:/Sound FX");
        uiBus = RuntimeManager.GetBus("bus:/Ui");
        ambientBus = RuntimeManager.GetBus("bus:/Ambient");
        voiceBus = RuntimeManager.GetBus("bus:/Voice");

        LoadAudioSettings();
    }

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
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
            case "GameScene":
            case "Lvl_1":
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
    public void SetMusicVolume(float vol)  { musicUserVol = vol;  musicBus.setVolume(vol * musicDuckMultiplier); }
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
        sfxDictionary.Add(AudioID.Ambient_Howl, ambientHowl);
        sfxDictionary.Add(AudioID.Ambient_Crow, ambientCrow);
        sfxDictionary.Add(AudioID.Ambient_DistantThunder, ambientDistantThunder);
        sfxDictionary.Add(AudioID.Ambient_LeafRustle, ambientLeafRustle);

        sfxDictionary.Add(AudioID.Music_Camp, musicCamp);
        sfxDictionary.Add(AudioID.Music_Battle, musicBattle);

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

    // Warn once per missing sound so a broken bank / unwired SoundGroup
    // stops being invisible. Previously PlaySFX silently returned when
    // the FMOD event was null — that made "no enemy sounds" impossible
    // to diagnose from console alone.
    private HashSet<string> warnedMissingSounds;

    public void PlaySFX(string soundName)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group))
        {
            if (group != null && !group.fmodEvent.IsNull)
            {
                FMOD.RESULT r = RuntimeManager.StudioSystem.getEventByID(group.fmodEvent.Guid, out FMOD.Studio.EventDescription desc);
                if (r != FMOD.RESULT.OK || !desc.isValid())
                {
                    WarnMissing(soundName, $"FMOD event GUID {group.fmodEvent.Guid} resolves with error {r} — bank not loaded, or GUID stale. Reimport banks from FMOD → Import Banks menu.");
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
            incoming.setVolume(0f);
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
            float k = Mathf.Clamp01(t / duration);
            if (incoming.isValid()) incoming.setVolume(k);
            yield return null;
        }
        if (incoming.isValid()) incoming.setVolume(1f);
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
        string key = "Dialogue/Dialogue" + dialogueNumber;
        if (sfxDictionary.TryGetValue(key, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
    }
}