using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FMODUnity;
using FMOD.Studio;

public static class AudioID
{
    public const string UI_Click = "UI_Click";
    public const string UI_Hover = "UI_Hover";
    public const string UI_QuestAccept = "UI_QuestAccept";
    public const string UI_QuestComplete = "UI_QuestComplete";
    public const string UI_Error = "UI_Error";
    public const string UI_LevelUp = "UI_LevelUp";
    public const string UI_Purchase = "UI_Purchase";

    public const string Player_Dash = "Player_Dash";
    public const string Player_Swing = "Player_Swing";
    public const string Player_HitEnemy = "Player_HitEnemy";
    public const string Player_HitResource = "Player_HitRes";
    public const string Player_Hurt = "Player_Hurt";
    public const string Player_Throw = "Player_Throw";
    public const string Player_Heal = "Player_Heal";
    public const string Player_Footstep = "Player_Footstep";
    public const string Explosion = "Explosion";

    public const string Enemy_Agro = "Enemy_Agro";
    public const string Enemy_Telegraph = "Enemy_Telegraph";
    public const string Enemy_Attack = "Enemy_Attack";
    public const string Enemy_Hurt = "Enemy_Hurt";
    public const string Enemy_Die = "Enemy_Die";
    public const string Enemy_Footstep = "Enemy_Footstep";

    public const string Camp_CollectItem = "Camp_CollectItem";
    public const string Camp_CollectGem = "Camp_CollectGem";
    public const string Camp_BuildStart = "Camp_BuildStart";
    public const string Camp_BuildDone = "Camp_BuildDone";
    public const string NPC_Work = "NPC_Work";
    public const string Env_Thunder = "Env_Thunder";
    public const string Env_ChestOpen = "Env_ChestOpen";

    public const string Animal_CatMeow = "Animal_CatMeow";
    public const string Animal_Chicken = "Animal_Chicken";

    // === Region mission + boss cinematic ===
    public const string Boss_Roar = "Boss_Roar";
    public const string Boss_Stagger = "Boss_Stagger";
    public const string Boss_Execute = "Boss_Execute";
    public const string Region_VictoryStinger = "Region_VictoryStinger";
    public const string Region_Shockwave = "Region_Shockwave";
    public const string Cinematic_Whoosh = "Cinematic_Whoosh";

    // === Gameplay feel ===
    public const string Encounter_Cleared = "Encounter_Cleared";
    public const string Totem_Activate = "Totem_Activate";
    public const string Player_PerfectDodge = "Player_PerfectDodge";

    public const string Music_Camp = "Music_Camp";
    public const string Music_Battle = "Music_Battle";

    // === Ambient soundscape (occasional one-shots layered over music) ===
    public const string Ambient_Wind = "Ambient_Wind";
    public const string Ambient_Howl = "Ambient_Howl";
    public const string Ambient_Crow = "Ambient_Crow";
    public const string Ambient_DistantThunder = "Ambient_DistantThunder";
    public const string Ambient_LeafRustle = "Ambient_LeafRustle";
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
    public SoundGroup uiClick, uiHover, uiQuestAccept, uiQuestComplete, uiError, uiLevelUp, uiPurchase;

    [Header("=== PLAYER SOUNDS ===")]
    public SoundGroup playerDash, playerSwing, playerHitEnemy, playerHitResource, playerHurt, playerThrow, playerHeal, playerFootstep, explosion;

    [Header("=== ENEMY SOUNDS ===")]
    public SoundGroup enemyAgro, enemyTelegraph, enemyAttack, enemyHurt, enemyDie, enemyFootstep;

    [Header("=== ENVIRONMENT & CAMP ===")]
    public SoundGroup campCollectItem, campCollectGem, campBuildStart, campBuildDone, npcWork, envThunder, envChestOpen;

    [Header("=== ANIMALS ===")]
    public SoundGroup animalCatMeow, animalChicken;

    [Header("=== REGION + BOSS CINEMATIC ===")]
    public SoundGroup bossRoar, bossStagger, bossExecute, regionVictoryStinger, regionShockwave, cinematicWhoosh;

    [Header("=== GAMEPLAY FEEL ===")]
    public SoundGroup encounterCleared, totemActivate, playerPerfectDodge;

    [Header("=== AMBIENT SOUNDSCAPE ===")]
    public SoundGroup ambientWind, ambientHowl, ambientCrow, ambientDistantThunder, ambientLeafRustle;

    [Header("=== MUSIC ===")]
    public SoundGroup musicCamp, musicBattle;

    private Dictionary<string, SoundGroup> sfxDictionary;
    private EventInstance currentMusicInstance;
    private string currentMusicName;

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

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "CampScene") PlayMusic(AudioID.Music_Camp);
        else if (scene.name == "WorldScene") PlayMusic(AudioID.Music_Battle);
    }

    private void LoadAudioSettings()
    {
        // �������� ������: Mathf.Clamp �� ����� ������� ����� ������ �� 1 (100%)
        globalMusicVolume = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_MusicVol", 1f), 0f, 1f);
        globalSFXVolume = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_SFXVol", 1f), 0f, 1f);

        float masterVol = Mathf.Clamp(PlayerPrefs.GetFloat("Settings_MasterVol", 1f), 0f, 1f);
        AudioListener.volume = masterVol;
    }

    public void SetMasterVolume(float vol) { AudioListener.volume = vol; PlayerPrefs.SetFloat("Settings_MasterVol", vol); }
    public void SetMusicVolume(float vol) { globalMusicVolume = vol; PlayerPrefs.SetFloat("Settings_MusicVol", vol); UpdateMusicVolume(); }
    public void SetSFXVolume(float vol) { globalSFXVolume = vol; PlayerPrefs.SetFloat("Settings_SFXVol", vol); }

    // Extended channels for AAA settings — UI / ambient / voice all
    // multiply on top of master via AudioListener so we only need to
    // surface them as multipliers consumed at PlayOneShot time.
    public float globalUIVolume = 1f;
    public float globalAmbientVolume = 1f;
    public float globalVoiceVolume = 1f;
    public void SetUIVolume(float vol) { globalUIVolume = vol; PlayerPrefs.SetFloat("Settings_UIVol", vol); }
    public void SetAmbientVolume(float vol) { globalAmbientVolume = vol; PlayerPrefs.SetFloat("Settings_AmbientVol", vol); }
    public void SetVoiceVolume(float vol) { globalVoiceVolume = vol; PlayerPrefs.SetFloat("Settings_VoiceVol", vol); }

    private void UpdateMusicVolume()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            foreach (var kvp in musicDictionary)
            {
                if (kvp.Value.clips != null && kvp.Value.clips.Length > 0 && kvp.Value.clips[0] == musicSource.clip)
                {
                    musicSource.volume = kvp.Value.volume * globalMusicVolume;
                    break;
                }
            }
        }
    }

    private void InitializeDictionaries()
    {
        sfxDictionary = new Dictionary<string, SoundGroup>();

        // UI
        sfxDictionary.Add(AudioID.UI_Click, uiClick); sfxDictionary.Add(AudioID.UI_Hover, uiHover); sfxDictionary.Add(AudioID.UI_QuestAccept, uiQuestAccept);
        sfxDictionary.Add(AudioID.UI_QuestComplete, uiQuestComplete); sfxDictionary.Add(AudioID.UI_Error, uiError); sfxDictionary.Add(AudioID.UI_LevelUp, uiLevelUp);
        sfxDictionary.Add(AudioID.UI_Purchase, uiPurchase);

        // Player
        sfxDictionary.Add(AudioID.Player_Dash, playerDash); sfxDictionary.Add(AudioID.Player_Swing, playerSwing); sfxDictionary.Add(AudioID.Player_HitEnemy, playerHitEnemy);
        sfxDictionary.Add(AudioID.Player_HitResource, playerHitResource); sfxDictionary.Add(AudioID.Player_Hurt, playerHurt); sfxDictionary.Add(AudioID.Player_Throw, playerThrow);
        sfxDictionary.Add(AudioID.Player_Heal, playerHeal); sfxDictionary.Add(AudioID.Player_Footstep, playerFootstep); sfxDictionary.Add(AudioID.Explosion, explosion);

        // Enemy
        sfxDictionary.Add(AudioID.Enemy_Agro, enemyAgro); sfxDictionary.Add(AudioID.Enemy_Telegraph, enemyTelegraph); sfxDictionary.Add(AudioID.Enemy_Attack, enemyAttack);
        sfxDictionary.Add(AudioID.Enemy_Hurt, enemyHurt); sfxDictionary.Add(AudioID.Enemy_Die, enemyDie); sfxDictionary.Add(AudioID.Enemy_Footstep, enemyFootstep);

        // Env & Camp
        sfxDictionary.Add(AudioID.Camp_CollectItem, campCollectItem); sfxDictionary.Add(AudioID.Camp_CollectGem, campCollectGem);
        sfxDictionary.Add(AudioID.Camp_BuildStart, campBuildStart); sfxDictionary.Add(AudioID.Camp_BuildDone, campBuildDone);
        sfxDictionary.Add(AudioID.NPC_Work, npcWork); sfxDictionary.Add(AudioID.Env_Thunder, envThunder); sfxDictionary.Add(AudioID.Env_ChestOpen, envChestOpen);

        // Animals
        sfxDictionary.Add(AudioID.Animal_CatMeow, animalCatMeow); sfxDictionary.Add(AudioID.Animal_Chicken, animalChicken);

        // Region + Boss cinematic
        sfxDictionary.Add(AudioID.Boss_Roar, bossRoar);
        sfxDictionary.Add(AudioID.Boss_Stagger, bossStagger);
        sfxDictionary.Add(AudioID.Boss_Execute, bossExecute);
        sfxDictionary.Add(AudioID.Region_VictoryStinger, regionVictoryStinger);
        sfxDictionary.Add(AudioID.Region_Shockwave, regionShockwave);
        sfxDictionary.Add(AudioID.Cinematic_Whoosh, cinematicWhoosh);

        // Gameplay feel
        sfxDictionary.Add(AudioID.Encounter_Cleared, encounterCleared);
        sfxDictionary.Add(AudioID.Totem_Activate, totemActivate);
        sfxDictionary.Add(AudioID.Player_PerfectDodge, playerPerfectDodge);

        sfxDictionary.Add(AudioID.Ambient_Wind, ambientWind);
        sfxDictionary.Add(AudioID.Ambient_Howl, ambientHowl);
        sfxDictionary.Add(AudioID.Ambient_Crow, ambientCrow);
        sfxDictionary.Add(AudioID.Ambient_DistantThunder, ambientDistantThunder);
        sfxDictionary.Add(AudioID.Ambient_LeafRustle, ambientLeafRustle);

        // Music
        sfxDictionary.Add(AudioID.Music_Camp, musicCamp); sfxDictionary.Add(AudioID.Music_Battle, musicBattle);
    }

    // Odpalanie efekt�w d�wi�kowych 2D (interfejs)
    public void PlayUI(string soundName)
    {
        PlaySFX(soundName);
    }

    // Odpalanie efekt�w d�wi�kowych w �wiecie gry
    public void PlaySFX(string soundName)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            // FMOD odpala d�wi�k jednorazowo w locie! 
            // Losowo�� pitchu/g�o�no�ci ustawiasz bezpo�rednio w programie FMOD Studio!
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
    }

    // Zarz�dzanie muzyk� w tle
    public void PlayMusic(string soundName)
    {
        if (currentMusicName == soundName) return;

        // Je�li leci jaka� muzyka, zatrzymujemy j� z uwzgl�dnieniem wygaszania (Fade Out zdefiniowanego w FMOD Studio)
        if (currentMusicInstance.isValid())
        {
            currentMusicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            currentMusicInstance.release();
        }

        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            currentMusicInstance = RuntimeManager.CreateInstance(group.fmodEvent);
            currentMusicInstance.start();
            currentMusicName = soundName;
        }
    }

    // Puste funkcje zachowane dla kompatybilno�ci kodu programisty (�eby gra si� kompilowa�a)
    public void SetMasterVolume(float vol) { }
    public void SetMusicVolume(float vol) { }
    public void SetSFXVolume(float vol) { }
}