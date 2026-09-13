using System.Collections;
using UnityEngine;

// Holding a shield up, and what it costs.
//
// ==== THE THREE OUTCOMES, AND WHY THERE IS NO CHIP DAMAGE ====
//
// A raised guard resolves an incoming hit one of three ways:
//
//   PERFECT BLOCK. The hit arrives within a fraction of a second of the guard
//   going up. Free — it costs no stamina and refunds some — and the attacker
//   is thrown into a long stagger and marked VULNERABLE. This is the riposte
//   window, and it is the skill ceiling of the whole system.
//
//   BLOCK. The hit is absorbed for zero damage and a bite of stamina scaled to
//   how hard it was. Deliberately NOT chip damage: taking health through a
//   successful block teaches the player that the correct action still hurts
//   them, which teaches them not to block. The pressure comes from the stamina
//   instead, which is honest — the guard works, you just cannot hold it forever
//   against something heavy.
//
//   GUARD BREAK. The pool is empty. The shield drops, that hit lands in full,
//   the player is staggered, and the guard cannot be raised again for a moment
//   — otherwise breaking it achieves nothing, since the player would simply
//   re-block on the next frame.
//
// Some attacks are UNBLOCKABLE, and those exist so that a raised shield is
// never the answer to everything. They must be telegraphed differently or they
// are not a mechanic, they are a gotcha; see DamageInfo.Unblockable.
//
// ==== WHAT THE ANGLE IS FOR ====
//
// A shield covers the front. Being hit from behind while blocking takes the hit
// in full — that is what keeps a crowd dangerous even to a defensive player,
// and it is why the attack-token ring matters: with everyone swinging at once
// there would always be someone behind you and the guard would be pointless.
[DisallowMultipleComponent]
public class PlayerBlock : MonoBehaviour
{
    public static PlayerBlock Instance { get; private set; }

    [Header("Input")]
    public KeyCode blockKey = KeyCode.Q;

    [Header("Guard")]
    [Tooltip("Seconds after raising the guard during which a hit is a PERFECT block. Short enough to be a read, long enough to be learnable — this is the number to tune first if parrying feels unfair.")]
    public float parryWindow = 0.18f;
    [Tooltip("Degrees of cover, centred on where the player faces. A hit from outside this arc is not blocked at all.")]
    public float guardAngle = 130f;
    [Tooltip("Stamina per second just to keep the shield up. Small: the cost of blocking should come from being HIT, not from holding.")]
    public float holdDrain = 6f;
    [Tooltip("Flat stamina per absorbed hit, before the damage-scaled part.")]
    public float blockCostBase = 6f;
    [Tooltip("Extra stamina per point of damage absorbed. This is what makes a boss swing expensive and a minion's cheap, with nothing to author per enemy.")]
    public float blockCostPerDamage = 0.55f;
    [Tooltip("Stamina handed back for a perfect block. Generous on purpose: parrying well should let a good player fight indefinitely.")]
    public float parryRefund = 18f;
    [Tooltip("Seconds the guard cannot be raised after it is broken.")]
    public float guardBreakLock = 2f;
    [Tooltip("Movement speed multiplier while blocking.")]
    [Range(0.2f, 1f)] public float blockMoveMultiplier = 0.55f;

    [Header("Punishing the attacker")]
    [Tooltip("Seconds an ordinary blocked enemy is staggered for. Enough to read as recoil off the shield.")]
    public float blockedStagger = 0.6f;
    [Tooltip("Seconds a PARRIED enemy is staggered for. This is the counter-attack window, so it has to be long enough to actually land a combo in.")]
    public float parriedStagger = 1.6f;
    [Tooltip("Extra damage a parried enemy takes while staggered.")]
    public float vulnerableMultiplier = 1.75f;
    [Tooltip("Seconds the WHOLE crowd hesitates after an ordinary block. Long enough to lower the guard and land a swing — without it the other attackers simply take over and blocking buys nothing.")]
    public float blockHesitation = 0.7f;
    [Tooltip("Seconds the whole crowd hesitates after a parry. This is the counter-attack window the system is built around, so it is generous.")]
    public float parryHesitation = 1.5f;

    public bool IsBlocking { get; private set; }
    public bool GuardBroken { get; private set; }

    private PlayerController _pc;
    private Animator _anim;
    private float _raisedAt = -100f;
    private float _lockedUntil = -100f;
    private ShieldStats _shield = ShieldStats.Bare;

    // What the equipped shield changes. A struct rather than a reference so
    // "no shield" is a value and never a null check at the point of use.
    public struct ShieldStats
    {
        public float parryWindowBonus;
        public float staminaMultiplier;
        public float angleBonus;
        public float reflectFraction;
        public float bonusStamina;

        // Bare-handed. Blocking without a shield IS allowed — it is worse in
        // every way, which teaches what a shield is for far better than simply
        // disabling the button and saying nothing.
        public static ShieldStats Bare => new ShieldStats
        {
            parryWindowBonus = -0.06f,
            staminaMultiplier = 1.8f,
            angleBonus = -40f,
            reflectFraction = 0f,
            bonusStamina = 0f,
        };
    }

    public void EquipShield(ShieldStats stats)
    {
        _shield = stats;
        if (_pc != null) _pc.bonusStamina = stats.bonusStamina;
    }

    // SELF-INSTALLING, so no player prefab has to be re-authored.
    //
    // There are two player prefabs and several scenes, and a mechanic that only
    // works in whichever one somebody remembered to add a component to is a
    // mechanic that is broken half the time. Adding it from the controller's
    // own Awake means every player that exists has a guard.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
        Attach();
    }

    private static void OnScene(UnityEngine.SceneManagement.Scene s,
                                UnityEngine.SceneManagement.LoadSceneMode m) => Attach();

    private static void Attach()
    {
        var pc = FindFirstObjectByType<PlayerController>();
        if (pc == null) return;
        if (pc.GetComponent<PlayerBlock>() == null) pc.gameObject.AddComponent<PlayerBlock>();
        if (pc.GetComponent<ShieldLoadout>() == null) pc.gameObject.AddComponent<ShieldLoadout>();
    }

    private void Awake()
    {
        Instance = this;
        _pc = GetComponent<PlayerController>();
        _anim = GetComponentInChildren<Animator>();
        CacheAnimatorParams();
    }

    // THE ANIMATOR MAY NOT KNOW THESE YET.
    //
    // Setting a parameter an AnimatorController does not declare logs an error
    // EVERY TIME — several a second while the guard is up — which buries the
    // console and makes every other warning in the game useless. The block has
    // to work before the controller is authored, so the calls are gated on the
    // parameter actually existing.
    private bool _hasBlockBool, _hasBlockHit;

    private void CacheAnimatorParams()
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;
        foreach (var p in _anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == "isBlocking") _hasBlockBool = true;
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == "BlockHit") _hasBlockHit = true;
        }
        if (!_hasBlockBool)
            Debug.Log("[Block] The player animator has no 'isBlocking' bool, so the guard has no pose yet. " +
                      "Run Tools > Combat > Add Block To Player Animator. Everything else works regardless.");
    }

    private void SetBlockPose(bool on) { if (_hasBlockBool) _anim.SetBool("isBlocking", on); }
    private void TriggerBlockHit() { if (_hasBlockHit) { _anim.ResetTrigger("BlockHit"); _anim.SetTrigger("BlockHit"); } }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private float EffectiveParryWindow => Mathf.Max(0.05f, parryWindow + _shield.parryWindowBonus);
    private float EffectiveAngle => Mathf.Clamp(guardAngle + _shield.angleBonus, 40f, 220f);

    private void Update()
    {
        if (_pc == null) return;

        bool wants = Input.GetKey(blockKey)
                     && !_pc.IsDead
                     && !_pc.isControlBlocked
                     && Time.time >= _lockedUntil
                     && _pc.HasStamina(1f);

        if (wants && !IsBlocking)
        {
            IsBlocking = true;
            _raisedAt = Time.time;
            GuardBroken = false;
            if (_anim != null) SetBlockPose(true);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.UI_Click);
        }
        else if (!wants && IsBlocking)
        {
            LowerGuard();
        }

        if (IsBlocking)
        {
            // Holding costs a trickle. If it runs the pool dry on its own the
            // guard simply drops — no break, no stagger. Being punished for
            // holding a shield in a lull would teach the player to never raise
            // it early, which is the opposite of what parrying needs.
            if (_pc.DrainStamina(holdDrain * Time.deltaTime)) LowerGuard();
        }
    }

    private void LowerGuard()
    {
        IsBlocking = false;
        if (_anim != null) SetBlockPose(false);
    }

    // ---- the resolution ------------------------------------------------------

    public enum Result { NotBlocked, Blocked, Parried, GuardBreak }

    // Called by PlayerController.TakeDamage BEFORE any health is touched.
    public Result Resolve(ref DamageInfo info, Vector3 attackerPos, EnemyAI attacker)
    {
        if (!IsBlocking) return Result.NotBlocked;
        if (info.Unblockable) return Result.NotBlocked;

        // FROM THE FRONT ONLY. A shield is a direction, not a status effect.
        Vector3 toAttacker = attackerPos - transform.position; toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude > 0.001f)
        {
            float angle = Vector3.Angle(transform.forward, toAttacker.normalized);
            if (angle > EffectiveAngle * 0.5f) return Result.NotBlocked;
        }

        bool perfect = Time.time - _raisedAt <= EffectiveParryWindow;

        if (perfect)
        {
            info.Amount = 0f;
            info.KnockbackForce = 0f;
            info.StunDuration = 0f;
            _pc.RefundStamina(parryRefund);
            PunishAttacker(attacker, parriedStagger, vulnerable: true);
            PlayParryFeedback(info.HitPoint, attackerPos);
            return Result.Parried;
        }

        float cost = (blockCostBase + info.Amount * blockCostPerDamage) * Mathf.Max(0.1f, _shield.staminaMultiplier);

        // NOT ENOUGH IN THE POOL = GUARD BREAK. Checked before spending, so the
        // player is never charged for a block that did not happen.
        if (!_pc.HasStamina(cost))
        {
            _pc.DrainStamina(cost);
            _pc.LockStamina(guardBreakLock);
            _lockedUntil = Time.time + guardBreakLock;
            GuardBroken = true;
            LowerGuard();
            PlayGuardBreakFeedback();
            // The hit lands in full and staggers — info is left untouched.
            return Result.GuardBreak;
        }

        _pc.DrainStamina(cost);

        // Spikes bite back. The one shield stat that changes how you fight
        // rather than how long you last.
        if (_shield.reflectFraction > 0f && attacker != null && !attacker.IsDead)
        {
            attacker.TakeDamage(new DamageInfo
            {
                Amount = info.Amount * _shield.reflectFraction,
                PushDirection = (attackerPos - transform.position).normalized,
                KnockbackForce = 2f,
                HitPoint = attackerPos,
            });
        }

        info.Amount = 0f;
        info.StunDuration = 0f;
        // A little push stays: absorbing a blow should move you, or the shield
        // reads as a wall rather than as something being held by a person.
        info.KnockbackForce = Mathf.Min(info.KnockbackForce, 3f);

        PunishAttacker(attacker, blockedStagger, vulnerable: false);
        PlayBlockFeedback(info.HitPoint);
        return Result.Blocked;
    }

    // The attacker pays for being blocked, and pays far more for being parried.
    //
    // Without this the block is purely defensive and the fight never opens up:
    // the player absorbs hits forever and never gets a turn. Losing the attack
    // slot is the important half — it hands the enemy's place in the rotation to
    // somebody in the ring and forces this one to circle.
    private void PunishAttacker(EnemyAI attacker, float stagger, bool vulnerable)
    {
        if (attacker == null || attacker.IsDead) return;

        attacker.ApplyBlockRecoil(stagger, vulnerable ? vulnerableMultiplier : 1f,
                                  vulnerable ? stagger : 0f);

        if (CombatRing.Instance != null)
        {
            CombatRing.Instance.Release(attacker, penalise: true, extraWait: vulnerable ? 1.2f : 0.3f);
            // And everyone else holds off for a beat, so the opening is real.
            // Without this the other two in the rotation swing while this one
            // reels, and defending well is punished instead of rewarded.
            CombatRing.Instance.Hesitate(vulnerable ? parryHesitation : blockHesitation);
        }
    }

    // ---- feedback ------------------------------------------------------------

    private void PlayBlockFeedback(Vector3 at)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, at);
        CameraShakeUtil.TryShake(0.25f, 0.09f);
        if (_anim != null) TriggerBlockHit();
    }

    // ==== THE MOMENT THE WHOLE SYSTEM EXISTS FOR ====
    //
    // A parry that is only a hitstop and a camera kick is indistinguishable
    // from an ordinary block that happened to land well — and if the player
    // cannot tell WHEN they parried, they cannot learn to do it on purpose,
    // which makes the deepest mechanic in the fight invisible.
    //
    // So it announces itself in four ways at once, on four different channels,
    // because in a crowded fight any single one can be missed: a flash of light
    // at the point of contact, a word on screen, the world stopping, and the
    // enemy itself flashing. Overkill is correct here. This is the one beat the
    // player is meant to chase — but see ParryFlashRoutine for why none of these
    // channels is allowed to sit between the camera and the enemy.
    private void PlayParryFeedback(Vector3 at, Vector3 attackerPos)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(AudioID.Player_Crit);
            AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, at);
        }
        if (_anim != null) TriggerBlockHit();
        Vector3 dir = (attackerPos - transform.position); dir.y = 0f;
        CameraShakeUtil.TryDirectionalShake(dir.normalized, 1.1f, 0.22f, 0.25f);
        StartCoroutine(ParryTimeRoutine());

        Vector3 spark = at != Vector3.zero ? at : transform.position + Vector3.up * 1.2f;
        StartCoroutine(ParryFlashRoutine(spark));
        ParryBanner.Show(LocalizationManager.Tr("PARRY"), new Color(1f, 0.93f, 0.55f));
    }

    // A flash of light at the point of contact — light only, no geometry.
    //
    // This started as an expanding emissive sphere. It read clearly and it was
    // also a three-metre white ball detonating in the middle of the screen at
    // exactly the moment the player needs to see the staggered enemy in order to
    // punish it. The effect was hiding the opening it was announcing, which is
    // the worst thing a piece of feedback can do.
    //
    // A point light says the same thing without occluding anything: the player,
    // the attacker and the ground between them all flare for a fifth of a
    // second. The legibility the sphere was carrying now lives in the on-screen
    // word, the hitstop and the gold pulse on the parried enemy — three channels,
    // none of which sit between the camera and the fight.
    private IEnumerator ParryFlashRoutine(Vector3 at)
    {
        Color glow = new Color(1f, 0.95f, 0.65f);

        var lightGo = new GameObject("ParryLight");
        lightGo.transform.position = at;
        var lg = lightGo.AddComponent<Light>();
        lg.type = LightType.Point;
        lg.color = glow;
        lg.range = 9f;
        lg.shadows = LightShadows.None;

        // UNSCALED, because the hitstop this plays over sets timeScale to 0.05.
        // On scaled time the flash would crawl through the freeze and land after
        // the moment it is describing.
        float t = 0f;
        const float dur = 0.22f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = t / dur;
            // Hard in, eased out: a light that ramps up reads as a lamp, one
            // that is already at full brightness on frame one reads as a strike.
            lg.intensity = 7f * (1f - k) * (1f - k);
            yield return null;
        }

        Destroy(lightGo);
    }

    // A hitstop and a dip, on UNSCALED time. A parry frequently happens while
    // something else is already messing with the timescale, and a routine that
    // waits on the game clock to restore it would leave the world in slow motion.
    private IEnumerator ParryTimeRoutine()
    {
        if (_parrying) yield break;
        _parrying = true;

        float restore = Time.timeScale;
        Time.timeScale = 0.05f;
        float t = 0f;
        while (t < 0.07f) { t += Time.unscaledDeltaTime; yield return null; }

        Time.timeScale = 0.35f;
        t = 0f;
        while (t < 0.12f) { t += Time.unscaledDeltaTime; yield return null; }

        Time.timeScale = restore <= 0.01f ? 1f : restore;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        _parrying = false;
    }

    private bool _parrying;

    private void PlayGuardBreakFeedback()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.UI_Error);
        CameraShakeUtil.TryShake(0.9f, 0.4f);
        if (_anim != null) SetBlockPose(false);
    }
}
