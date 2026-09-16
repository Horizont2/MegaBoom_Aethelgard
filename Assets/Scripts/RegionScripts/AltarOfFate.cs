using System.Collections;
using UnityEngine;

// THE ALTAR OF FATE — pay diamonds, pull the lever, find out.
//
// ==== WHY A GAMBLE BELONGS IN THIS GAME ====
//
// Every other payout in the world is earned by doing something: clear the
// guards, hold the vigil, light the braziers. They are all the same TRANSACTION
// with different prices — effort in, loot out — so a player who is good at the
// game just gets more of everything, steadily.
//
// This one takes the currency instead, and it can say no. That makes it the only
// place on the map where the player makes a decision with nothing to practise,
// and it is the only place a bad run can turn around in one press. Both of those
// are worth having, and neither exists anywhere else.
//
// ==== THE NUMBERS, AND WHY THEY ARE WHAT THEY ARE ====
//
// 40% bad, 20% consolation, 40% jackpot, for a cost near 50 diamonds.
//
// Expected value is deliberately a little POSITIVE — around a tenth of the
// stake. A gamble with negative EV is a tax on optimism, and the players who
// work the numbers out stop touching it, which kills the feature for exactly
// the audience most likely to enjoy it. A little positive means using it is
// correct, and the reason not to is the VARIANCE: forty percent of the time you
// are down fifty diamonds and possibly fighting two elites you did not plan for.
//
// The jackpots are worth more than diamonds on purpose. A free upgrade card or
// twenty seconds of doubled damage can change how a run ENDS, and that is the
// thing worth gambling for. Handing back 150 diamonds would be arithmetic.
//
// ==== ONE USE ====
//
// Per altar, per region, forever. Without that it is a slot machine, and a
// player standing at it feeding diamonds in until the jackpot lands has turned
// a decision into a grind — and made the 40% failure meaningless, since it costs
// nothing but another press.
[DisallowMultipleComponent]
public class AltarOfFate : MonoBehaviour
{
    public enum Outcome { Mockery, Curse, Ambush, Alms, FreeUpgrade, Wrath, Magnet }

    [Header("Price")]
    [Tooltip("Diamonds the altar asks for. Scaled up a little per conquered region so it stays a real decision late in the campaign.")]
    public int baseCost = 50;
    [Tooltip("Extra fraction of the cost per conquered region, capped at 24.")]
    public float costPerRegion = 0.04f;

    [Header("Odds")]
    [Tooltip("Chance the altar takes the offering and gives nothing good.")]
    [Range(0f, 1f)] public float badChance = 0.40f;
    [Tooltip("Chance of the consolation. Read AFTER the bad roll.")]
    [Range(0f, 1f)] public float almsChance = 0.20f;
    // Whatever is left is the jackpot.

    [Header("Punishments")]
    [Tooltip("Seconds the curse slows the player.")]
    public float curseSeconds = 10f;
    [Tooltip("Movement multiplier while cursed.")]
    [Range(0.2f, 1f)] public float curseSlow = 0.5f;
    [Tooltip("Elites the trap spawns.")]
    public Vector2Int ambushCount = new Vector2Int(2, 3);
    public GameObject[] ambushPrefabs;
    [Tooltip("Junk coughed up by the mockery — bones, ash. Purely visual, and that is the joke.")]
    public GameObject[] junkPrefabs;

    [Header("Prizes")]
    [Tooltip("Seconds the Wrath buff lasts.")]
    public float wrathSeconds = 20f;
    [Tooltip("Damage multiplier during Wrath.")]
    public float wrathDamage = 2f;
    [Tooltip("Seconds the Magnet drags the whole map in for.")]
    public float magnetSeconds = 6f;
    [Tooltip("Pickup radius during Magnet, in metres. Large enough to cover a region.")]
    public float magnetRadius = 600f;

    [Header("Look and feel")]
    [Tooltip("Light on the altar. Its colour is what tells the player which way it went, before any text.")]
    public Light altarLight;
    [Tooltip("Effect played while the altar makes up its mind.")]
    public GameObject tensionVFX;
    [Tooltip("Seconds of flickering before the answer. Long enough to be unbearable, short enough not to be a loading screen.")]
    public float tensionSeconds = 1.5f;

    [Header("Interaction")]
    public float interactRange = 3f;
    public KeyCode interactKey = KeyCode.E;

    private Transform _player;
    private PlayerController _pc;
    private bool _spent;
    private bool _rolling;
    private bool _promptShown;

    public int Cost => Mathf.RoundToInt(
        baseCost * (1f + Mathf.Clamp(PlayerPrefs.GetInt("TotalConqueredRegions", 0), 0, 24) * costPerRegion));

    private void Start()
    {
        var mk = GetComponent<MapEventMarker>();
        if (mk == null) mk = gameObject.AddComponent<MapEventMarker>();
        mk.kind = MapEventIcons.Kind.Altar;

        if (tensionVFX != null) tensionVFX.SetActive(false);
        if (altarLight != null) altarLight.intensity = 0.6f;
    }

    private void Update()
    {
        if (_spent || _rolling) return;

        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _player = p.transform;
            _pc = p.GetComponent<PlayerController>();
        }

        if (Vector3.Distance(_player.position, transform.position) > interactRange)
        {
            HidePrompt();
            return;
        }

        var rm = ResourceManager.Instance;
        int cost = Cost;
        bool canPay = rm != null && rm.CanAffordDiamonds(cost);

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.ShowPrompt(canPay
                ? LocalizationManager.Tr("ALTAR_FATE_PROMPT", cost)
                : LocalizationManager.Tr("ALTAR_FATE_TOO_POOR", cost));
            _promptShown = true;
        }

        if (canPay && Input.GetKeyDown(interactKey))
        {
            rm.SpendDiamonds(cost);
            HidePrompt();
            StartCoroutine(RollRoutine());
        }
    }

    private void HidePrompt()
    {
        if (!_promptShown) return;
        _promptShown = false;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
    }

    private IEnumerator RollRoutine()
    {
        _rolling = true;

        // ==== THE ANSWER IS DECIDED FIRST, AND THE TENSION IS HONEST ABOUT IT ====
        //
        // Rolling up front and then playing the flicker means the light can lean
        // toward the answer as it settles, which is what makes the last half
        // second land. Rolling at the END would make the flicker a lie, and
        // players read that instantly even when they cannot say why.
        Outcome result = Roll();

        if (tensionVFX != null) tensionVFX.SetActive(true);
        if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.Chest_VigilStart))
            AudioManager.Instance.PlaySFX3D(AudioID.Chest_VigilStart, transform.position);

        Color end = ColourFor(result);
        float t = 0f;
        while (t < tensionSeconds)
        {
            t += Time.deltaTime;
            float k = t / tensionSeconds;

            if (altarLight != null)
            {
                // Flicker hard at first, then converge on the answer's colour.
                altarLight.color = Color.Lerp(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f), end, k * k);
                altarLight.intensity = Mathf.Lerp(2f, 9f, k) * (0.6f + 0.4f * Mathf.Sin(t * 40f));
            }
            CameraShakeUtil.TryShake(Mathf.Lerp(0.05f, 0.3f, k), 0.08f);
            yield return null;
        }

        if (tensionVFX != null) tensionVFX.SetActive(false);
        if (altarLight != null) { altarLight.color = end; altarLight.intensity = 5f; }

        Apply(result);

        _spent = true;
        _rolling = false;

        var marker = GetComponent<MapEventMarker>();
        if (marker != null) marker.MarkDone();
    }

    private Outcome Roll()
    {
        float r = Random.value;
        if (r < badChance)
        {
            // Split the failure three ways. Mockery is the cheapest to receive
            // and the most common, because "you lost the stake" IS the failure —
            // the other two are the failure plus a bill.
            float b = Random.value;
            if (b < 0.45f) return Outcome.Mockery;
            if (b < 0.78f) return Outcome.Curse;
            return Outcome.Ambush;
        }
        if (r < badChance + almsChance) return Outcome.Alms;

        float g = Random.value;
        if (g < 0.40f) return Outcome.FreeUpgrade;
        if (g < 0.75f) return Outcome.Wrath;
        return Outcome.Magnet;
    }

    private static Color ColourFor(Outcome o) => o switch
    {
        Outcome.Mockery or Outcome.Curse or Outcome.Ambush => new Color(0.85f, 0.12f, 0.12f),
        Outcome.Alms => new Color(0.35f, 0.6f, 1f),
        _ => new Color(1f, 0.82f, 0.3f),
    };

    private void Apply(Outcome o)
    {
        switch (o)
        {
            case Outcome.Mockery: DoMockery(); break;
            case Outcome.Curse: DoCurse(); break;
            case Outcome.Ambush: DoAmbush(); break;
            case Outcome.Alms: DoAlms(); break;
            case Outcome.FreeUpgrade: DoFreeUpgrade(); break;
            case Outcome.Wrath: DoWrath(); break;
            case Outcome.Magnet: DoMagnet(); break;
        }
    }

    // ---- failures -------------------------------------------------------------

    private void DoMockery()
    {
        if (junkPrefabs != null && junkPrefabs.Length > 0)
        {
            int n = Random.Range(4, 8);
            for (int i = 0; i < n; i++)
            {
                var pf = junkPrefabs[Random.Range(0, junkPrefabs.Length)];
                if (pf == null) continue;
                Vector2 o = Random.insideUnitCircle * 1.4f;
                var go = Instantiate(pf, transform.position + new Vector3(o.x, 1.6f, o.y),
                                     Random.rotation);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(new Vector3(o.x, 4f, o.y) * 1.6f, ForceMode.Impulse);
                Destroy(go, 20f);
            }
        }
        Announce("ALTAR_FATE_MOCKERY_TITLE", "ALTAR_FATE_MOCKERY_BODY", ColourFor(Outcome.Mockery));
        if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.UI_Error))
            AudioManager.Instance.PlaySFX(AudioID.UI_Error);
    }

    private void DoCurse()
    {
        if (_pc != null) StartCoroutine(CurseRoutine(_pc));
        Announce("ALTAR_FATE_CURSE_TITLE",
                 LocalizationManager.Tr("ALTAR_FATE_CURSE_BODY", Mathf.RoundToInt(curseSeconds)),
                 ColourFor(Outcome.Curse), preTranslated: true);
        CameraShakeUtil.TryShake(0.7f, 0.35f);
        InputCompat.Rumble(0.85f, 0.4f, 0.35f);
    }

    private IEnumerator CurseRoutine(PlayerController pc)
    {
        // Multiplicative and restored to what it WAS, not to a constant — the
        // player may have upgrades on their speed, and a curse that hands back
        // the prefab default would quietly delete them.
        float before = pc.moveSpeed;
        pc.moveSpeed = before * curseSlow;
        float t = 0f;
        while (t < curseSeconds && pc != null)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (pc != null) pc.moveSpeed = before;
    }

    private void DoAmbush()
    {
        int n = Random.Range(ambushCount.x, ambushCount.y + 1);
        var pool = (ambushPrefabs != null && ambushPrefabs.Length > 0) ? ambushPrefabs : null;
        if (pool != null)
        {
            for (int i = 0; i < n; i++)
            {
                var pf = pool[Random.Range(0, pool.Length)];
                if (pf == null) continue;
                float a = (i / (float)n) * Mathf.PI * 2f;
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5f;
                pos.y = GroundAt(pos);
                var go = Instantiate(pf, pos, Quaternion.LookRotation(transform.position - pos));
                var ai = go.GetComponent<EnemyAI>();
                if (ai == null) continue;
                ai.startPassive = false;
                ai.canDeAggro = false;
                ai.isElite = true;
                ai.maxHealth *= 1.6f;
                ai.damage *= 1.25f;
                ai.aggroRange = 40f;
            }
        }
        Announce("ALTAR_FATE_AMBUSH_TITLE", "ALTAR_FATE_AMBUSH_BODY", ColourFor(Outcome.Ambush));
        CameraShakeUtil.TryShake(0.9f, 0.4f);
        InputCompat.Rumble(0.9f, 0.7f, 0.4f);
    }

    // ---- consolation ----------------------------------------------------------

    private void DoAlms()
    {
        int back = Mathf.Max(1, Cost / 2);
        if (ResourceManager.Instance != null) ResourceManager.Instance.AddDiamonds(back);
        Announce("ALTAR_FATE_ALMS_TITLE",
                 LocalizationManager.Tr("ALTAR_FATE_ALMS_BODY", back),
                 ColourFor(Outcome.Alms), preTranslated: true);
    }

    // ---- prizes ---------------------------------------------------------------

    private void DoFreeUpgrade()
    {
        Announce("ALTAR_FATE_GIFT_TITLE", "ALTAR_FATE_GIFT_BODY", ColourFor(Outcome.FreeUpgrade));
        // After the reveal, or the upgrade menu opens under it and the player
        // never reads what happened.
        StartCoroutine(OpenUpgradeAfter(1.1f));
    }

    private IEnumerator OpenUpgradeAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        var lum = Object.FindFirstObjectByType<LevelUpManager>();
        if (lum != null) lum.ShowMenu();
        else Debug.LogWarning("[AltarOfFate] No LevelUpManager in the scene — the gift rolled and had nowhere to go.", this);
    }

    private void DoWrath()
    {
        if (_pc != null) StartCoroutine(WrathRoutine(_pc));
        Announce("ALTAR_FATE_WRATH_TITLE",
                 LocalizationManager.Tr("ALTAR_FATE_WRATH_BODY", Mathf.RoundToInt(wrathSeconds)),
                 ColourFor(Outcome.Wrath), preTranslated: true);
    }

    private IEnumerator WrathRoutine(PlayerController pc)
    {
        // Additive on the multiplier, undone by the same amount. Assigning and
        // restoring a snapshot would wipe anything the player picks up during
        // the twenty seconds — a Forge bonus, a level-up card.
        float bonus = Mathf.Max(0f, wrathDamage - 1f);
        pc.globalDamageMultiplier += bonus;
        float t = 0f;
        while (t < wrathSeconds && pc != null)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (pc != null) pc.globalDamageMultiplier -= bonus;
    }

    private void DoMagnet()
    {
        if (_pc != null) StartCoroutine(MagnetRoutine(_pc));
        Announce("ALTAR_FATE_MAGNET_TITLE", "ALTAR_FATE_MAGNET_BODY", ColourFor(Outcome.Magnet));
    }

    private IEnumerator MagnetRoutine(PlayerController pc)
    {
        // Every pickup in the game already flies to the player once it is inside
        // pickupRadius — ResourceDrop, DiamondPickup and XpCrystal all test
        // against that one field. So the whole effect is that number, briefly.
        // No new movement code, and anything that spawns DURING the window is
        // swept up too, which is what makes it feel like an anomaly rather than
        // a scripted collection.
        float before = pc.pickupRadius;
        pc.pickupRadius = magnetRadius;
        float t = 0f;
        while (t < magnetSeconds && pc != null)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (pc != null) pc.pickupRadius = before;
    }

    // ---- shared ---------------------------------------------------------------

    private void Announce(string titleKey, string body, Color accent, bool preTranslated)
    {
        RewardReveal.Show(null, LocalizationManager.Tr(titleKey),
                          preTranslated ? body : LocalizationManager.Tr(body), accent, 2.8f);
    }

    private void Announce(string titleKey, string bodyKey, Color accent)
        => Announce(titleKey, bodyKey, accent, preTranslated: false);

    private static float GroundAt(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f,
                            ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        Terrain t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
    }

    private void OnDisable() => HidePrompt();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
