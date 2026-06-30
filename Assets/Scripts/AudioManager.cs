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
        masterBus = RuntimeManager.GetBus("bus:/");
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
        if (scene.name == "CampScene") PlayMusic(AudioID.Music_Camp);
        else if (scene.name == "WorldScene") PlayMusic(AudioID.Music_Battle);
    }

    private void LoadAudioSettings()
    {
        float masterVol = PlayerPrefs.GetFloat("Settings_MasterVol", 1f);
        float musicVol = PlayerPrefs.GetFloat("Settings_MusicVol", 1f);
        float sfxVol = PlayerPrefs.GetFloat("Settings_SFXVol", 1f);
        float uiVol = PlayerPrefs.GetFloat("Settings_UIVol", 1f);
        float ambientVol = PlayerPrefs.GetFloat("Settings_AmbientVol", 1f);
        float voiceVol = PlayerPrefs.GetFloat("Settings_VoiceVol", 1f);

        masterBus.setVolume(masterVol);
        musicBus.setVolume(musicVol);
        sfxBus.setVolume(sfxVol);
        uiBus.setVolume(uiVol);
        ambientBus.setVolume(ambientVol);
        voiceBus.setVolume(voiceVol);
    }

    public void SetMasterVolume(float vol)
    {
        masterBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_MasterVol", vol);
    }

    public void SetMusicVolume(float vol)
    {
        musicBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_MusicVol", vol);
    }

    public void SetSFXVolume(float vol)
    {
        sfxBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_SFXVol", vol);
    }

    public void SetUIVolume(float vol)
    {
        uiBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_UIVol", vol);
    }

    public void SetAmbientVolume(float vol)
    {
        ambientBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_AmbientVol", vol);
    }

    public void SetVoiceVolume(float vol)
    {
        voiceBus.setVolume(vol);
        PlayerPrefs.SetFloat("Settings_VoiceVol", vol);
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

    public void PlaySFX(string soundName)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
    }

    public void PlaySFX3D(string soundName, Vector3 position)
    {
        if (sfxDictionary.TryGetValue(soundName, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent, position);
        }
    }

    public void PlayMusic(string soundName)
    {
        if (currentMusicName == soundName) return;

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

    public void PlayDialogue(int dialogueNumber)
    {
        string key = "Dialogue/Dialogue" + dialogueNumber;
        if (sfxDictionary.TryGetValue(key, out SoundGroup group) && !group.fmodEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(group.fmodEvent);
        }
    }
}