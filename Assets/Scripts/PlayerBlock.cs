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
    [Tooltip("Floor for the parry window, in seconds. Used when the attacker's wind-up is unknown; otherwise the window is a fraction of that wind-up — see parryWindowOfTelegraph.")]
    public float parryWindow = 0.18f;

    // ==== THE WINDOW BELONGS TO THE ATTACK, NOT TO THE CLOCK ====
    //
    // A flat 0.18s means the difficulty of parrying is decided entirely by how
    // long the attacker's wind-up is, and those differ by nearly threefold
    // across the archetypes. Against a slow boss the player had almost a second
    // of warning for the same 0.18s window; against a quick enemy the window
    // opened before the tell had finished registering.
    //
    // So the fast enemies were unparryable and the slow ones trivial — the exact
    // inverse of what the fight wants, since a boss parry is the one worth
    // building a mechanic around.
    //
    // Taking the window as a FRACTION of this attacker's wind-up makes every
    // enemy the same read: watch the tell, answer in its last four-tenths. A
    // Rogue and a Boss now ask for the same skill on different clocks, which is
    // what "learnable" actually means.
    [Tooltip("Parry window as a fraction of the attacker's wind-up. 0.4 means the last four-tenths of any telegraph is parryable, whether that telegraph is half a second or two.")]
    [Range(0.15f, 0.7f)] public float parryWindowOfTelegraph = 0.4f;
    [Tooltip("Degrees of cover, centred on where the player faces. A hit from outside this arc is not blocked at all.")]
    public float guardAngle = 130f;
    // ==== HOLDING IS FREE. BEING HIT IS NOT. ====
    //
    // This was 6/s, and the damage it did was far worse than the number looks:
    // DrainStamina stamps lastStaminaSpend, and regeneration requires 0.8s
    // without a spend. So while the shield was up the pool drained AND
    // regenerated at exactly zero — not slower, nothing. Three seconds of
    // correct defensive play cost about seventy of a hundred points, and the
    // dash comes out of the same pool, so every block also spent the escape.
    //
    // The whole defensive half of the fight was therefore a slow loss, with only
    // a parry paying anything back. That is the "сиро" in the report: the
    // mechanic punished the player for using it properly.
    //
    // Turtling does not need a bleed to discourage it. A raised shield deals no
    // damage, the ring keeps circling, and nothing about the fight advances —
    // that is already the cost. And it still cannot be held forever under
    // pressure, because every ABSORBED hit restarts the regeneration delay: with
    // attackers landing about one blow a second there is never a gap long enough
    // to recover in, so the pool still runs down. The player recovers in the
    // openings they earn, which is exactly where recovery belongs.
    [Tooltip("Stamina per second just to keep the shield up. Zero on purpose — see the note above. Raise it only if holding the guard turns out to need a cost of its own.")]
    public float holdDrain = 0f;
    [Tooltip("Flat stamina per absorbed hit, before the damage-scaled part.")]
    public float blockCostBase = 6f;
    [Tooltip("Extra stamina per point of damage absorbed. This is what makes a boss swing expensive and a minion's cheap, with nothing to author per enemy.")]
    public float blockCostPerDamage = 0.55f;
    [Tooltip("Stamina handed back for a perfect block. Generous on purpose: parrying well should let a good player fight indefinitely.")]
    public float parryRefund = 18f;
    // ==== A BREAK IS A PUNISHMENT, NOT A SENTENCE ====
    //
    // It used to be the harshest outcome the game can produce, applied at the
    // worst possible moment: full damage, plus LockStamina for two seconds — and
    // LockStamina refuses ALL spending, so the dash went with it. The player ran
    // out of stamina because they were under pressure, and the punishment for
    // that was two seconds of standing in the middle of a crowd with no guard,
    // no dash and no way to move the situation.
    //
    // Nothing is learned from it and there is no play to make. The break still
    // hurts and still takes the shield away, but it now ENDS with the player
    // somewhere else: the blow throws them clear, the guard alone is locked, and
    // stamina starts climbing again on the ordinary delay so a dash is
    // affordable within about a second.
    [Tooltip("Seconds the guard cannot be raised after it is broken. Only the GUARD — dashing and stamina regeneration are deliberately left alone, so a break is recoverable.")]
    public float guardBreakLock = 1.2f;
    [Tooltip("Fraction of the blow that lands through a broken guard. Not the full hit: the shield was there, it just was not enough.")]
    [Range(0.2f, 1f)] public float guardBreakDamageFraction = 0.6f;
    [Tooltip("How hard the break throws the player clear of the attacker. This is the exit — without it a break just parks the player, defenceless, exactly where they were standing.")]
    public float guardBreakKnockback = 9f;
    [Tooltip("Seconds the rest of the crowd holds off after a break. Short: enough that the second attacker cannot free-hit a player who has just lost their guard, nowhere near enough to make breaking one harmless.")]
    public float guardBreakHesitation = 0.5f;
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

    // What the on-screen cue has to draw. It must be the SAME number the hit
    // will be judged against — a countdown that disagrees with the rule it is
    // counting down to is worse than no countdown, because the player learns a
    // timing that then fails.
    public static float ParryWindowSecondsFor(EnemyAI attacker)
    {
        if (Instance != null) return Instance.ParryWindowFor(attacker);
        // No guard installed yet: fall back to the same shape with default
        // numbers so the cue is still honest about roughly when to press.
        float t = attacker != null ? attacker.EffectiveTelegraph : 0.6f;
        return Mathf.Max(0.18f, t * 0.4f);
    }

    // The window for THIS attacker: the flat floor, or a slice of its wind-up,
    // whichever is more generous. The shield's own bonus rides on top either
    // way, so a parry-focused shield still helps against everything.
    private float ParryWindowFor(EnemyAI attacker)
    {
        float w = parryWindow;
        if (attacker != null) w = Mathf.Max(w, attacker.EffectiveTelegraph * parryWindowOfTelegraph);
        return Mathf.Max(0.05f, w + _shield.parryWindowBonus);
    }
    private float EffectiveAngle => Mathf.Clamp(guardAngle + _shield.angleBonus, 40f, 220f);

    private void Update()
    {
        if (_pc == null) return;

        // Not while swimming. Attacking and throwing a grenade are already
        // refused in the water — both hands are busy staying afloat — and a
        // shield is the heaviest thing the player carries. Letting it come up
        // out there also made the water a safe box to guard from, with no
        // stamina economy attached, because nothing in it can be parried.
        bool wants = Input.GetKey(blockKey)
                     && !_pc.IsDead
                     && !_pc.isSwimming
                     && !_pc.isControlBlocked
                     && Time.time >= _lockedUntil
                     && _pc.HasStamina(1f);

        if (wants && !IsBlocking)
        {
            IsBlocking = true;
            _raisedAt = Time.time;
            GuardBroken = false;

            // Raising the guard cancels a swing already in flight — and it has
            // to be said HERE, on the rising edge, not inferred later from
            // whether the guard is still up. The damage lands at the swing's
            // contact frame, which can be after the player has already let go,
            // so checking the live state at that moment lets a quick tap through
            // and the hit connects anyway.
            _pc.CancelSwing();

            if (_anim != null) SetBlockPose(true);
            if (AudioManager.Instance != null && AudioManager.Instance.HasEvent(AudioID.Block_Raise))
                AudioManager.Instance.PlaySFX(AudioID.Block_Raise);
        }
        else if (!wants && IsBlocking)
        {
            LowerGuard();
        }

        // Only spend if somebody has actually configured a hold cost. Calling
        // DrainStamina with zero would still stamp lastStaminaSpend and suppress
        // regeneration for the whole time the guard is up, which is the exact
        // bug this is fixing — a free hold has to mean NO call, not a call for
        // nothing.
        if (IsBlocking && holdDrain > 0.001f)
        {
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

    // ==== WHAT THE GUARD DID, FOR ANYTHING THAT NEEDS TO REACT TO IT ====
    //
    // Resolve returns its answer to PlayerController and nowhere else, so
    // anything further out — an arrow deciding where to bury itself, a VFX
    // deciding where to spark — had no way to ask. Recorded with the frame it
    // happened on, because a caller that reads this WITHOUT having just caused a
    // hit would otherwise get whatever the last one was.
    public Result LastResult { get; private set; }
    public int LastResultFrame { get; private set; } = -1;

    // The shield model in the player's hand, if one is equipped. Cached lazily
    // because ShieldLoadout rebuilds it whenever the loadout changes.
    public Transform ShieldTransform
    {
        get
        {
            var loadout = GetComponent<ShieldLoadout>();
            return loadout != null ? loadout.ShieldModel : null;
        }
    }

    private Result Record(Result r)
    {
        LastResult = r;
        LastResultFrame = Time.frameCount;
        return r;
    }

    // Called by PlayerController.TakeDamage BEFORE any health is touched.
    public Result Resolve(ref DamageInfo info, Vector3 attackerPos, EnemyAI attacker)
    {
        if (!IsBlocking) return Record(Result.NotBlocked);
        if (info.Unblockable) return Record(Result.NotBlocked);

        // FROM THE FRONT ONLY. A shield is a direction, not a status effect.
        Vector3 toAttacker = attackerPos - transform.position; toAttacker.y = 0f;
        if (toAttacker.sqrMagnitude > 0.001f)
        {
            float angle = Vector3.Angle(transform.forward, toAttacker.normalized);
            if (angle > EffectiveAngle * 0.5f) return Record(Result.NotBlocked);
        }

        bool perfect = Time.time - _raisedAt <= ParryWindowFor(attacker);

        if (perfect)
        {
            info.Amount = 0f;
            info.KnockbackForce = 0f;
            info.StunDuration = 0f;
            _pc.RefundStamina(parryRefund);
            PunishAttacker(attacker, parriedStagger, vulnerable: true);
            PlayParryFeedback(info.HitPoint, attackerPos);
            return Record(Result.Parried);
        }

        float cost = (blockCostBase + info.Amount * blockCostPerDamage) * Mathf.Max(0.1f, _shield.staminaMultiplier);

        // NOT ENOUGH IN THE POOL = GUARD BREAK. Checked before spending, so the
        // player is never charged for a block that did not happen.
        if (!_pc.HasStamina(cost))
        {
            _pc.DrainStamina(cost);
            // NO LockStamina. It refuses every spend, dash included, which is
            // what turned a break into two seconds of being unable to act. The
            // guard alone is locked, below; stamina climbs again on the ordinary
            // delay, so an escape becomes affordable shortly after the blow.
            _lockedUntil = Time.time + guardBreakLock;
            GuardBroken = true;
            LowerGuard();
            PlayGuardBreakFeedback();

            // The shield was there. It was not enough — but it was there.
            info.Amount *= guardBreakDamageFraction;
            // Thrown clear, and that is the point: the player ends the exchange
            // somewhere other than the middle of the crowd that just broke them.
            Vector3 away = transform.position - attackerPos; away.y = 0f;
            if (away.sqrMagnitude > 0.001f) info.PushDirection = away.normalized;
            info.KnockbackForce = Mathf.Max(info.KnockbackForce, guardBreakKnockback);

            // And the others hold for a beat, so a break is not an execution by
            // whoever happens to be swinging next.
            if (CombatRing.Instance != null) CombatRing.Instance.Hesitate(guardBreakHesitation);

            return Record(Result.GuardBreak);
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
        return Record(Result.Blocked);
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
        if (AudioManager.Instance != null)
        {
            // Its own event now. Blocking is a timing read and timing reads are
            // learned by ear as much as by eye — reusing the enemy's swing here
            // meant a successful block sounded exactly like being hit.
            if (AudioManager.Instance.HasEvent(AudioID.Block_Impact))
                AudioManager.Instance.PlaySFX3D(AudioID.Block_Impact, at);
            else
                AudioManager.Instance.PlaySFX3D(AudioID.Enemy_Attack, at);
        }
        CameraShakeUtil.TryShake(0.25f, 0.09f);
        // Dull and low: a blow absorbed by the shield, felt through the arm.
        InputCompat.Rumble(0.45f, 0.1f, 0.09f);
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
            // A parry must not sound like a critical hit — that is the exact
            // distinction the player is trying to learn.
            if (AudioManager.Instance.HasEvent(AudioID.Block_Parry))
            {
                AudioManager.Instance.PlaySFX(AudioID.Block_Parry);
            }
            else
            {
                AudioManager.Instance.PlaySFX(AudioID.Player_Crit);
                AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, at);
            }
        }
        if (_anim != null) TriggerBlockHit();
        Vector3 dir = (attackerPos - transform.position); dir.y = 0f;
        CameraShakeUtil.TryDirectionalShake(dir.normalized, 1.1f, 0.22f, 0.25f);
        // A fifth channel, and the one a player feels before they see anything.
        // Sharp and high, deliberately unlike the dull thud of an ordinary
        // block — that is the distinction the whole mechanic is trying to teach.
        InputCompat.Rumble(0.25f, 0.9f, 0.13f);
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
        if (AudioManager.Instance != null)
        {
            // A guard break is a physical event, not a menu rejection. UI_Error
            // was a placeholder and it makes the worst moment in the fight sound
            // like clicking a disabled button.
            if (AudioManager.Instance.HasEvent(AudioID.Block_GuardBreak))
                AudioManager.Instance.PlaySFX(AudioID.Block_GuardBreak);
            else
                AudioManager.Instance.PlaySFX(AudioID.UI_Error);
        }
        CameraShakeUtil.TryShake(0.9f, 0.4f);
        // The harshest thing that happens to the player, so both motors, hard.
        InputCompat.Rumble(0.9f, 0.7f, 0.3f);
        if (_anim != null) SetBlockPose(false);
    }
}
