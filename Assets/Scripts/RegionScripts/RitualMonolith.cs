using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// THE RITUAL OF CLEANSING — a fight the player starts on purpose.
//
// A cursed monolith with three or four cold braziers around it. Each brazier is
// a two-and-a-half second channel. Lighting one wakes the monolith, and every
// one after that wakes it further. Light them all and the curse breaks: every
// enemy the monolith called dies where it stands, and it pays out.
//
// ==== WHAT MAKES THIS DIFFERENT FROM THE RELIQUARY VIGIL ====
//
// The vigil is ONE timer in ONE place: stand here, survive. It is a test of
// whether the player can hold ground.
//
// This is four short channels in four DIFFERENT places, and the channel breaks
// if the player is hit. So the question is not "can you survive here", it is
// "can you make a gap, cross to the next bowl, and buy yourself three seconds
// in it". The player chooses when to commit and which brazier to go for, and a
// crowd between them and the last cold bowl is a problem they created by
// lighting the other three. That is a different skill and a different shape of
// fight, which is the only good reason to have two siege POIs at all.
//
// ==== THE CHANNEL BREAKS ON DAMAGE, NOT ON BEING NEAR SOMETHING ====
//
// Watching for "an enemy is close" would make the channel a proximity puzzle
// and would fire on an enemy that missed. Watching HEALTH means a parry keeps
// the channel, a dodge keeps the channel, and a block keeps the channel —
// every defensive answer the game already teaches is worth something here, and
// the player who reads the fight gets to keep their progress.
//
// It reads the health rather than subscribing, because PlayerController has no
// damage event and the alternative is editing the busiest file in the project
// to add one.
[DisallowMultipleComponent]
public class RitualMonolith : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("The braziers. Left empty, every RitualBrazier under this object is used.")]
    public RitualBrazier[] braziers;
    [Tooltip("The monolith mesh. Pulsed while the ritual is running and released when it breaks.")]
    public Transform monolith;
    [Tooltip("Corruption effect on the monolith while the curse holds. Switched off when it breaks.")]
    public GameObject curseVFX;
    [Tooltip("Played once, at the monolith, when the last brazier catches.")]
    public GameObject cleanseVFX;

    [Header("Channel")]
    [Tooltip("Seconds of holding the interact key to light one brazier.")]
    public float channelSeconds = 2.5f;
    [Tooltip("Health the player may lose during a channel before it breaks. A hair above zero so a poison tick cannot cancel it, low enough that any real hit does.")]
    public float channelBreakDamage = 1f;

    [Header("Waves")]
    [Tooltip("Enemies called when each brazier catches, in order. The LAST brazier calls nothing — it is the one that ends the fight, and a wave there would be spawning enemies that die on the same frame.")]
    public int[] waveSizes = { 3, 4, 5 };
    [Tooltip("How many of each wave are elites. Same indexing as waveSizes.")]
    public int[] waveElites = { 0, 1, 1 };
    [Tooltip("Ordinary enemies the monolith calls.")]
    public GameObject[] enemyPrefabs;
    [Tooltip("Elites. Left empty, an ordinary enemy is used with its stats raised instead.")]
    public GameObject[] elitePrefabs;
    [Tooltip("Ring the called enemies rise on, in metres from the monolith.")]
    public float spawnRing = 11f;
    [Tooltip("Extra health and damage on the enemies this POI calls, so it stays a fight late in a run.")]
    public float enemyStatMultiplier = 1.15f;

    [Header("Reward")]
    [Tooltip("Diamonds for breaking the curse, before scaling.")]
    public int diamondReward = 55;
    [Tooltip("Supplies for breaking the curse, before scaling.")]
    public int woodReward = 40, stoneReward = 30, foodReward = 20;
    [Tooltip("Extra fraction of the reward per region already conquered, capped at 24 regions. 0.05 = up to 2.2x at the end of the campaign.")]
    public float rewardPerRegion = 0.05f;
    [Tooltip("Dropped at the monolith when the curse breaks — xp crystals, a chest, whatever the region kit uses.")]
    public GameObject rewardDropPrefab;
    [Range(0, 12)] public int rewardDropCount = 6;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;

    // ---- state ----------------------------------------------------------------
    private Transform _player;
    private PlayerController _pc;
    private readonly List<EnemyAI> _called = new List<EnemyAI>(24);
    private RitualBrazier _channelling;
    private float _progress;
    private float _healthAtChannelStart;
    private int _lit;
    private bool _finished;
    private bool _promptShown;
    private float _pulseT;
    private Vector3 _monolithBaseScale;

    private void Awake()
    {
        if (braziers == null || braziers.Length == 0)
            braziers = GetComponentsInChildren<RitualBrazier>(true);

        if (monolith != null) _monolithBaseScale = monolith.localScale;
        if (cleanseVFX != null) cleanseVFX.SetActive(false);
    }

    private void Start()
    {
        // The marker is what makes this a destination rather than something you
        // trip over. Altar is the closest existing kind — a shrine-like thing
        // worth walking to — and reusing it means no new icon has to be drawn.
        var mk = GetComponent<MapEventMarker>();
        if (mk == null) mk = gameObject.AddComponent<MapEventMarker>();
        mk.kind = MapEventIcons.Kind.Altar;

        if (braziers == null || braziers.Length < 2)
            Debug.LogWarning($"[Ritual] '{name}' has fewer than two braziers, so there is no ritual to perform.", this);
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            Debug.LogWarning($"[Ritual] '{name}' has no enemy prefabs — the braziers will light unopposed, which is a reward for nothing.", this);
    }

    private void Update()
    {
        if (_finished) return;

        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _player = p.transform;
            _pc = p.GetComponent<PlayerController>();
        }

        PulseMonolith();

        RitualBrazier target = NearestColdBrazier(out float dist);
        if (target == null || dist > target.interactRange)
        {
            CancelChannel(showBar: false);
            return;
        }

        if (Input.GetKey(interactKey))
        {
            if (_channelling != target) BeginChannel(target);

            // ==== A HIT BREAKS IT ====
            //
            // Compared against the health at the START of this channel rather
            // than frame to frame, so a lifesteal tick or a level-up heal in the
            // middle cannot mask real damage taken a moment earlier.
            if (_pc != null && _pc.currentHealth <= _healthAtChannelStart - channelBreakDamage)
            {
                BreakChannel();
                return;
            }

            _progress += Time.deltaTime / Mathf.Max(0.1f, channelSeconds);
            if (_progress >= 1f) { LightBrazier(target); return; }

            ShowBar(LocalizationManager.Tr("RITUAL_CHANNEL"), _progress);
        }
        else
        {
            CancelChannel(showBar: true);
            HidePromptIfShown();
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("RITUAL_PROMPT", braziers.Length - _lit));
                _promptShown = true;
            }
        }
    }

    private void LateUpdate()
    {
        // Same self-healing teardown the reliquary uses: every way out of a
        // channel drops the bar a moment later, so no exit path needs its own.
        if (_barShownAt < 0f) return;
        if (Time.unscaledTime - _barShownAt < 0.25f) return;
        _barShownAt = -1f;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HideObjectiveBar();
    }

    private float _barShownAt = -1f;

    private void ShowBar(string label, float p)
    {
        HidePromptIfShown();
        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.ShowObjectiveBar(label, p, new Color(0.72f, 0.45f, 1f));
        _barShownAt = Time.unscaledTime;
    }

    private void HidePromptIfShown()
    {
        if (!_promptShown) return;
        _promptShown = false;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
    }

    private RitualBrazier NearestColdBrazier(out float dist)
    {
        dist = float.MaxValue;
        RitualBrazier best = null;
        if (braziers == null || _player == null) return null;
        for (int i = 0; i < braziers.Length; i++)
        {
            var b = braziers[i];
            if (b == null || b.IsLit) continue;
            float d = Vector3.Distance(_player.position, b.transform.position);
            if (d < dist) { dist = d; best = b; }
        }
        return best;
    }

    private void BeginChannel(RitualBrazier b)
    {
        _channelling = b;
        _progress = 0f;
        _healthAtChannelStart = _pc != null ? _pc.currentHealth : float.MaxValue;
    }

    private void CancelChannel(bool showBar)
    {
        if (_channelling == null) return;
        _channelling = null;
        _progress = 0f;
    }

    private void BreakChannel()
    {
        _channelling = null;
        _progress = 0f;
        ShowBar(LocalizationManager.Tr("RITUAL_BROKEN"), 0f);
        if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.UI_Error))
            AudioManager.Instance.PlaySFX(AudioID.UI_Error);
        CameraShakeUtil.TryShake(0.25f, 0.14f);
        InputCompat.Rumble(0.5f, 0.2f, 0.12f);
    }

    private void LightBrazier(RitualBrazier b)
    {
        _channelling = null;
        _progress = 0f;
        b.Light();
        _lit++;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Totem_Activate, b.transform.position);
        CameraShakeUtil.TryShake(0.3f, 0.16f);
        InputCompat.Rumble(0.35f, 0.5f, 0.14f);

        int remaining = braziers.Length - _lit;
        if (remaining <= 0) { StartCoroutine(BreakCurse()); return; }

        // The LAST brazier calls nothing — see waveSizes. Everything before it
        // escalates, so the walk to the next bowl is the hard part.
        int waveIndex = _lit - 1;
        if (waveSizes != null && waveIndex < waveSizes.Length) SendWave(waveIndex);

        ShowBar(LocalizationManager.Tr("RITUAL_LIT", _lit, braziers.Length), 1f);
    }

    private void SendWave(int index)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        int count = Mathf.Max(0, waveSizes[index]);
        int elites = (waveElites != null && index < waveElites.Length) ? Mathf.Clamp(waveElites[index], 0, count) : 0;

        for (int i = 0; i < count; i++)
        {
            bool asElite = i < elites;
            GameObject prefab = asElite && elitePrefabs != null && elitePrefabs.Length > 0
                              ? elitePrefabs[Random.Range(0, elitePrefabs.Length)]
                              : enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
            if (prefab == null) continue;

            float a = (i / (float)Mathf.Max(1, count)) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnRing;
            pos.y = GroundAt(pos);

            var go = Instantiate(prefab, pos, Quaternion.LookRotation(transform.position - pos));
            var ai = go.GetComponent<EnemyAI>();
            if (ai == null) continue;

            // Called by the monolith, bound to the monolith: they come straight
            // in and they never wander off, because an enemy that lost interest
            // would leave the ritual permanently unfinishable.
            ai.startPassive = false;
            ai.canDeAggro = false;
            ai.aggroRange = spawnRing + 20f;
            ai.leashRange = 200f;
            ai.maxHealth *= enemyStatMultiplier;
            ai.damage *= enemyStatMultiplier;
            if (asElite && (elitePrefabs == null || elitePrefabs.Length == 0))
            {
                // No elite prefab assigned: make one out of an ordinary enemy
                // rather than quietly spawning a third minion.
                ai.isElite = true;
                ai.maxHealth *= 1.8f;
                ai.damage *= 1.35f;
                go.transform.localScale *= 1.12f;
            }
            _called.Add(ai);
        }

        if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.Boss_Enrage))
            AudioManager.Instance.PlaySFX3D(AudioID.Boss_Enrage, transform.position);
    }

    private IEnumerator BreakCurse()
    {
        _finished = true;
        HidePromptIfShown();

        if (curseVFX != null) curseVFX.SetActive(false);
        if (cleanseVFX != null) cleanseVFX.SetActive(true);
        if (monolith != null) monolith.localScale = _monolithBaseScale;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, transform.position);
            if (AudioManager.Instance.HasEvent(AudioID.Region_VictoryStinger))
                AudioManager.Instance.PlaySFX(AudioID.Region_VictoryStinger);
        }
        CameraShakeUtil.TryShake(0.85f, 0.45f);
        InputCompat.Rumble(0.9f, 0.6f, 0.4f);

        // Everything the monolith called dies. Through TakeDamage rather than a
        // silent Destroy, so the player still gets the kills, the xp and the
        // drops they were fighting for — the curse breaking is a reward, not a
        // way of confiscating the fight.
        for (int i = 0; i < _called.Count; i++)
        {
            var e = _called[i];
            if (e == null || e.IsDead) continue;
            e.TakeDamage(new DamageInfo
            {
                Amount = 99999f,
                IsCritical = true,
                HitPoint = e.transform.position + Vector3.up,
                SourceName = "Ritual"
            });
            if ((i & 3) == 3) yield return null;   // spread the deaths over a few frames
        }
        _called.Clear();

        yield return new WaitForSeconds(0.35f);
        PayOut();
    }

    private void PayOut()
    {
        float scale = 1f + Mathf.Clamp(PlayerPrefs.GetInt("TotalConqueredRegions", 0), 0, 24) * rewardPerRegion;

        int gems = Mathf.RoundToInt(diamondReward * scale);
        int wood = Mathf.RoundToInt(woodReward * scale);
        int stone = Mathf.RoundToInt(stoneReward * scale);
        int food = Mathf.RoundToInt(foodReward * scale);

        var rm = ResourceManager.Instance;
        if (rm != null)
        {
            rm.AddRunResources(wood, stone, food);
            rm.AddDiamonds(gems);
            rm.UpdateUI();
        }

        if (rewardDropPrefab != null)
        {
            for (int i = 0; i < rewardDropCount; i++)
            {
                Vector2 o = Random.insideUnitCircle * 2.2f;
                Vector3 p = transform.position + new Vector3(o.x, 1.2f, o.y);
                Instantiate(rewardDropPrefab, p, Quaternion.identity);
            }
        }

        RewardReveal.Show(null,
            LocalizationManager.Tr("RITUAL_CLEANSED_TITLE"),
            LocalizationManager.Tr("RITUAL_CLEANSED_BODY", gems),
            new Color(1f, 0.87f, 0.45f), 3.0f);

        var marker = GetComponent<MapEventMarker>();
        if (marker != null) marker.MarkDone();
    }

    // A slow swell while the curse holds, growing with every brazier lit, so the
    // monolith reads as fighting back rather than as scenery with a timer.
    private void PulseMonolith()
    {
        if (monolith == null || _monolithBaseScale == Vector3.zero) return;
        float intensity = 0.012f + 0.012f * _lit;
        float rate = 2.2f + 0.9f * _lit;
        _pulseT += Time.deltaTime * rate;
        monolith.localScale = _monolithBaseScale * (1f + Mathf.Sin(_pulseT) * intensity);
    }

    private static float GroundAt(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f,
                            ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        Terrain t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
    }

    private void OnDisable()
    {
        HidePromptIfShown();
        if (_barShownAt >= 0f && GlobalHUD.Instance != null) GlobalHUD.Instance.HideObjectiveBar();
        _barShownAt = -1f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 0.35f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, spawnRing);
    }
}
