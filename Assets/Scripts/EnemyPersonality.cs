using System.Collections.Generic;
using UnityEngine;

// Makes one skeleton different from the skeleton standing next to it.
//
// ==== THE PROBLEM ====
//
// All nine enemy prefabs — minion, warrior, rogue, mage, necromancer, archer and
// all three bosses — share a single AnimatorController with nine states. So every
// enemy in the game idles the same, runs the same, is hit the same and dies the
// same, and a camp of four skeletons is one skeleton drawn four times. Worse,
// they are usually spawned in the same frame, so their animations are in perfect
// lockstep: four figures breathing on the same beat, which reads as a rendering
// glitch rather than as four people.
//
// ==== THE APPROACH: OVERRIDE, DON'T REBUILD ====
//
// An AnimatorOverrideController keeps the state machine exactly as it is and only
// swaps which CLIP each state plays. That is the whole trick. No states are added,
// no transitions are rewired, no controller asset is edited — so none of the ways
// a hand-edited state machine can silently break are available. Each enemy gets
// its own override instance, picks its own clips, and the graph it runs on is
// still the one that has always worked.
//
// It also means gait is a clip swap rather than a new state: a patrol that walks
// and then chases is the same Running state with a different clip in it.
//
// ==== THREE LAYERS OF DIFFERENCE ====
//
//   ARCHETYPE. A warrior should not move like a rogue. Each kind gets its own
//   pools — heavy two-handed swings and a slow deliberate walk, or dual-wield
//   slashes and a quick light one — plus its own speed and build.
//
//   INDIVIDUAL. Within an archetype, each one still rolls its own idle, its own
//   death, its own gait speed and a fractional difference in size.
//
//   PHASE. And then the animators are nudged out of step with each other, which
//   is the single cheapest and most effective of the three. Identical figures
//   moving on different beats stop reading as copies almost entirely.
[DisallowMultipleComponent]
public class EnemyPersonality : MonoBehaviour
{
    public enum Archetype { Minion, Warrior, Rogue, Mage, Necromancer, Archer, Boss }

    // Master switch, read from PlayerPrefs so it can be flipped without a
    // rebuild. This layer rewrites which clip every enemy state plays, so when
    // something looks wrong with enemy animation it is the first thing worth
    // ruling out — and being stuck waiting for a code change to do that is not
    // acceptable. Set "EnemyPersonality" to 0 to switch it off entirely and get
    // the stock shared animator back.
    public static bool Enabled => PlayerPrefs.GetInt("EnemyPersonality", 1) == 1;

    [Tooltip("Log the clip-to-role mapping for each enemy on spawn. The mapping is inferred from clip names, so this is the fastest way to see WHY a state is playing the wrong animation.")]
    public bool logMapping = false;

    [Tooltip("Leave at Auto-detect unless a prefab is named in a way the detector cannot read.")]
    public bool autoDetectArchetype = true;
    public Archetype archetype = Archetype.Minion;

    [Header("Gait matching")]
    [Tooltip("Ground speed the WALK clips were authored for, in m/s. The clip is played at travelled-speed divided by this, which is what stops the feet sliding.")]
    public float walkClipSpeed = 1.4f;
    [Tooltip("Ground speed the RUN clips were authored for, in m/s.")]
    public float runClipSpeed = 4.2f;

    [Header("Individual variation")]
    [Tooltip("A small per-enemy tempo difference riding on top of the gait match. Keep it narrow — past about 10% it stops reading as a different person and starts reading as sliding again.")]
    public Vector2 speedJitter = new Vector2(0.95f, 1.06f);
    [Tooltip("Build. Deliberately tiny — this scales the collider and the hitbox with the model, so anything larger changes how the fight plays, not just how it looks.")]
    public Vector2 scaleJitter = new Vector2(0.97f, 1.03f);
    public bool varyScale = true;

    private Animator _animator;
    private AnimatorOverrideController _override;
    private EnemyAnimationSet _set;
    private System.Random _rng;

    // The clip names the base controller uses, which are the KEYS an override is
    // addressed by. An override maps original-clip to replacement, so getting a
    // name wrong here is a silent no-op rather than an error — hence reading them
    // off the controller at startup instead of assuming.
    private string _idleKey, _runKey, _hitKey, _deathKey, _attackKey;

    private AnimationClip _walkClip, _runClip;
    // What this one swings by default, remembered so a named move can hand the
    // attack state back afterwards.
    private AnimationClip _basicAttack;
    private bool _walking = true;
    // Per-enemy tempo, applied on top of the gait match rather than instead of it.
    private float _tempo = 1f;

    private void Awake()
    {
        if (!Enabled) { enabled = false; return; }

        _animator = GetComponentInChildren<Animator>();
        if (_animator == null || _animator.runtimeAnimatorController == null) { enabled = false; return; }

        _set = EnemyAnimationSet.Load();
        if (_set == null) { enabled = false; return; }

        // Seeded per instance so a given enemy is consistent with itself across a
        // frame, while two spawned in the same frame still differ.
        _rng = new System.Random(GetInstanceID());

        if (autoDetectArchetype) archetype = Detect(gameObject.name);
        if (PlayerPrefs.GetInt("EnemyPersonalityLog", 0) == 1) logMapping = true;

        BuildOverride();
        ApplyBody();
        Desync();
    }

    private static Archetype Detect(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("boss")) return Archetype.Boss;
        if (n.Contains("necro")) return Archetype.Necromancer;
        if (n.Contains("mage")) return Archetype.Mage;
        if (n.Contains("archer") || n.Contains("bow")) return Archetype.Archer;
        if (n.Contains("rogue")) return Archetype.Rogue;
        if (n.Contains("warrior")) return Archetype.Warrior;
        return Archetype.Minion;
    }

    private void BuildOverride()
    {
        _override = new AnimatorOverrideController(_animator.runtimeAnimatorController);

        // Work out which clip in the controller belongs to which role.
        //
        // The previous version walked the clips ONCE through an if/else-if chain,
        // and that was the animation mess. Each clip could only ever be tested
        // against the first role it happened to match, and roles were claimed in
        // whatever order GetOverrides returned — so the bow attack, which is
        // simply the alphabetically earlier clip containing "attack", claimed the
        // attack role and the real melee swing was never mapped at all. Any
        // reordering shuffled which state got which clip.
        //
        // Now each role is resolved independently, by its own scored predicate,
        // over the whole list — and a clip already taken by one role cannot be
        // claimed by another. Deterministic regardless of order.
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        _override.GetOverrides(pairs);

        var names = new List<string>(pairs.Count);
        foreach (var p in pairs) if (p.Key != null) names.Add(p.Key.name);

        var claimed = new HashSet<string>();
        _attackKey = Claim(names, claimed, n =>
            (n.Contains("attack") || n.Contains("stab") || n.Contains("slice") || n.Contains("chop"))
            && !n.Contains("bow") && !n.Contains("ranged") && !n.Contains("hit"));
        _hitKey = Claim(names, claimed, n => n.Contains("hit") && !n.Contains("attack"));
        _deathKey = Claim(names, claimed, n => n.Contains("death") || n.Contains("die"));
        _idleKey = Claim(names, claimed, n =>
            n.Contains("idle") && !n.Contains("dizzy") && !n.Contains("aim") && !n.Contains("bow"));
        _runKey = Claim(names, claimed, n => n.Contains("running") || n.Contains("walking") || n.Contains("run"));

        if (logMapping)
            Debug.Log($"[Personality] {name} ({archetype}) mapped — attack:{_attackKey ?? "-"} hit:{_hitKey ?? "-"} " +
                      $"death:{_deathKey ?? "-"} idle:{_idleKey ?? "-"} run:{_runKey ?? "-"}  from [{string.Join(", ", names)}]");

        AnimationClip idle = PickIdle();
        AnimationClip death = EnemyAnimationSet.Pick(_set.deaths, _rng);
        _basicAttack = PickAttack();
        _walkClip = PickWalk();
        _runClip = PickRun();

        Set(_idleKey, idle);
        Set(_deathKey, death);
        Set(_attackKey, _basicAttack);
        Set(_hitKey, EnemyAnimationSet.Pick(_set.hits, _rng));
        Set(_runKey, _walkClip ?? _runClip);   // starts calm; SetGait corrects it

        _animator.runtimeAnimatorController = _override;
    }

    // First clip matching `want` that no other role has taken.
    private static string Claim(List<string> names, HashSet<string> claimed, System.Func<string, bool> want)
    {
        foreach (var n in names)
        {
            if (claimed.Contains(n)) continue;
            if (!want(n.ToLowerInvariant())) continue;
            claimed.Add(n);
            return n;
        }
        return null;
    }

    private void Set(string key, AnimationClip clip)
    {
        if (string.IsNullOrEmpty(key) || clip == null) return;
        _override[key] = clip;
    }

    // ---- archetype flavour ---------------------------------------------------

    private AnimationClip PickIdle()
    {
        switch (archetype)
        {
            case Archetype.Boss:
            case Archetype.Warrior:
                return First(_set.idles, "Idle_A") ?? EnemyAnimationSet.Pick(_set.idles, _rng);
            case Archetype.Rogue:
            case Archetype.Mage:
            case Archetype.Necromancer:
                return First(_set.idles, "Idle_B") ?? EnemyAnimationSet.Pick(_set.idles, _rng);
            case Archetype.Archer:
                return First(_set.bow, "Ranged_Bow_Aiming_Idle") ?? EnemyAnimationSet.Pick(_set.idles, _rng);
            default:
                // The rank and file get the shambling skeleton idle where it
                // exists, and roll freely otherwise — they are the ones there are
                // most of, so they are the ones variety matters most for.
                return First(_set.idles, "Skeletons_Idle") ?? EnemyAnimationSet.Pick(_set.idles, _rng);
        }
    }

    private AnimationClip PickWalk()
    {
        switch (archetype)
        {
            case Archetype.Boss:
            case Archetype.Warrior:   return First(_set.walks, "Walking_A") ?? EnemyAnimationSet.Pick(_set.walks, _rng);
            case Archetype.Rogue:     return First(_set.walks, "Walking_B") ?? EnemyAnimationSet.Pick(_set.walks, _rng);
            case Archetype.Minion:    return First(_set.walks, "Skeletons_Walking") ?? First(_set.walks, "Walking_C")
                                             ?? EnemyAnimationSet.Pick(_set.walks, _rng);
            default:                  return EnemyAnimationSet.Pick(_set.walks, _rng);
        }
    }

    private AnimationClip PickRun()
    {
        if (archetype == Archetype.Archer && _set.runHoldingBow != null) return _set.runHoldingBow;
        if (archetype == Archetype.Rogue) return First(_set.runs, "Running_B") ?? EnemyAnimationSet.Pick(_set.runs, _rng);
        return EnemyAnimationSet.Pick(_set.runs, _rng);
    }

    private AnimationClip PickAttack()
    {
        switch (archetype)
        {
            case Archetype.Boss:        return EnemyAnimationSet.Pick(_set.bossAttacks, _rng)
                                            ?? EnemyAnimationSet.Pick(_set.attacks2H, _rng);
            case Archetype.Warrior:     return EnemyAnimationSet.Pick(_set.attacks2H, _rng);
            case Archetype.Rogue:       return EnemyAnimationSet.Pick(_set.attacksDualWield, _rng)
                                            ?? EnemyAnimationSet.Pick(_set.attacks1H, _rng);
            case Archetype.Mage:        return EnemyAnimationSet.Pick(_set.spellcasts, _rng);
            case Archetype.Necromancer: return _set.summon ?? EnemyAnimationSet.Pick(_set.spellcasts, _rng);
            case Archetype.Archer:      return null;   // the bow states already handle it
            default:                    return EnemyAnimationSet.Pick(_set.attacksUnarmed, _rng)
                                            ?? EnemyAnimationSet.Pick(_set.attacks1H, _rng);
        }
    }

    private static AnimationClip First(AnimationClip[] pool, string name)
    {
        if (pool == null) return null;
        foreach (var c in pool) if (c != null && c.name == name) return c;
        return null;
    }

    // ---- body and timing -----------------------------------------------------

    private void ApplyBody()
    {
        // Playback rate is NOT set here any more.
        //
        // Multiplying animator.speed by an archetype figure and a jitter was
        // exactly why the run cycle stopped matching the ground: the enemy still
        // travelled at moveSpeed while its legs ran at 0.86x or 1.10x of whatever
        // rate the clip was authored for, so it skated. Gait rate is now derived
        // from how fast the enemy is ACTUALLY moving — see MatchLocomotion — and
        // the per-enemy character comes from which clips it wears, not from
        // running the same clip at a different speed.
        _tempo = Lerp(speedJitter);

        if (!varyScale || archetype == Archetype.Boss) return;   // a boss is the size it was authored
        float s = Lerp(scaleJitter);
        if (archetype == Archetype.Warrior) s *= 1.03f;
        if (archetype == Archetype.Rogue) s *= 0.97f;
        transform.localScale *= s;
    }

    // Keep the legs in step with the ground.
    //
    // A locomotion clip is authored for one speed. Play it on a character moving
    // at a different one and the feet slide — the classic tell that a character
    // is being dragged rather than walking. The fix is to run the clip at
    // travelled-speed / authored-speed.
    //
    // Only while moving, and never during an attack: animator.speed is global, so
    // scaling it for the legs would speed the swing up too and desynchronise the
    // blow from the moment the damage lands.
    private float _playRate = 1f;

    public void MatchLocomotion(float worldSpeed, bool attacking)
    {
        if (_animator == null) return;

        if (attacking || worldSpeed < 0.15f)
        {
            // Straight to the tempo: an attack must play at its authored rate
            // or the contact frame stops matching the damage.
            _playRate = _tempo;
            _animator.speed = _playRate;
            return;
        }

        float reference = _walking ? walkClipSpeed : runClipSpeed;
        if (reference <= 0.01f) { _playRate = _tempo; _animator.speed = _playRate; return; }

        // The tempo jitter rides on top as a small per-enemy difference, kept
        // narrow enough that it reads as gait and not as sliding.
        float want = Mathf.Clamp(worldSpeed / reference, 0.45f, 2.2f) * _tempo;

        // ==== EASED, BECAUSE THE MEASURED SPEED IS NOISY ====
        //
        // worldSpeed is measured from actual travel, and actual travel jumps
        // whenever the CharacterController clips a corner, the crowd separation
        // slides the body, or a branch changes pace. Writing that straight into
        // animator.speed makes the run cycle visibly surge and stall — which
        // from the player's seat is the legs changing animation, even though the
        // clip never changed.
        _playRate = Mathf.MoveTowards(_playRate, want, 2.5f * Time.deltaTime);
        _animator.speed = _playRate;
    }

    // Nudge the animator off whatever beat everything else spawned on.
    //
    // Animator.Update is the way to do this. Animator.Play(0, layer, time) looks
    // like it should work and does not: hash 0 is not "whatever state is current",
    // it is an invalid state, and the call is silently ignored.
    private void Desync()
    {
        float offset = (float)_rng.NextDouble() * 1.2f;
        _animator.Update(offset);
    }

    private float Lerp(Vector2 range) => Mathf.Lerp(range.x, range.y, (float)_rng.NextDouble());

    // ---- gait ----------------------------------------------------------------

    // Called by EnemyAI as its state changes. Chasing runs; patrolling, searching
    // and walking back to a post do not — which is the whole reason the game's
    // patrols have always looked like they were sprinting to nowhere.
    public void SetGait(bool running)
    {
        if (_override == null) return;
        if (running != _walking) return;   // already in the gait being asked for
        _walking = !running;

        AnimationClip want = running ? _runClip : _walkClip;
        if (want == null || string.IsNullOrEmpty(_runKey)) return;
        _override[_runKey] = want;
    }

    // ---- named moves ---------------------------------------------------------

    public enum Move { Basic, Slam, Cleave, Charge, Summon, Taunt }

    // Load a specific move into the attack state, then let the caller fire the
    // trigger it already fires.
    //
    // This is how five different boss attacks stop looking like one. The boss has
    // Slam, Cleave, Charge, Summon and a basic swing in code, all of them firing
    // the same "Attack" trigger, so a player has no way to learn to read them —
    // a wind-up that looks identical to four other wind-ups is not a tell, it is
    // a coin toss. The state machine does not need to change for this: the state
    // stays the same, the clip inside it does not.
    //
    // The swap has to happen BEFORE the trigger, which is exactly where every one
    // of those routines already has a telegraph to hide it in.
    public void ArmAttack(Move move)
    {
        if (_set == null || _override == null || string.IsNullOrEmpty(_attackKey)) return;
        AnimationClip c = Resolve(move);
        if (c != null) _override[_attackKey] = c;
    }

    private AnimationClip Resolve(Move m)
    {
        switch (m)
        {
            // A slam wants a downward two-handed commitment.
            case Move.Slam:
                return _set.bossSlam
                    ?? First(_set.attacks2H, "Melee_2H_Attack_Chop")
                    ?? _basicAttack;
            // A cleave is a radial sweep centred on the boss, so a spin is
            // literally the motion — not an approximation of it.
            case Move.Cleave:
                return First(_set.attacks2H, "Melee_2H_Attack_Spin")
                    ?? First(_set.attacks2H, "Melee_2H_Attack_Slice")
                    ?? _basicAttack;
            // A charge ends in a thrust, because the body is already travelling.
            case Move.Charge:
                return First(_set.attacks2H, "Melee_2H_Attack_Stab")
                    ?? First(_set.attacks1H, "Melee_1H_Attack_Stab")
                    ?? _basicAttack;
            case Move.Summon:
                return _set.summon ?? EnemyAnimationSet.Pick(_set.spellcasts, _rng) ?? _basicAttack;
            case Move.Taunt:
                return EnemyAnimationSet.Pick(_set.taunts, _rng) ?? _basicAttack;
            default:
                return _basicAttack;
        }
    }

    // A different swing this time, drawn from the same archetype pool.
    //
    // Called just before each attack fires. The warrior still swings like a
    // warrior — it is picking between that archetype's own two-handed attacks,
    // not borrowing a rogue's — but a drawn-out fight stops being one animation
    // on loop, and two of the same enemy next to each other stop mirroring.
    //
    // Safe to call here and nowhere else: this runs immediately BEFORE the
    // trigger, so the clip is swapped while the attack state is idle. Doing it
    // mid-swing would restart the animation on the frame it should connect.
    public void RerollAttack()
    {
        if (_set == null || _override == null || string.IsNullOrEmpty(_attackKey)) return;
        AnimationClip c = PickAttack();
        if (c == null) return;
        _basicAttack = c;
        _override[_attackKey] = c;
    }

    // One-off swap for a caller that has its own clip in hand.
    public void UseAttackClip(AnimationClip clip)
    {
        if (clip != null) Set(_attackKey, clip);
    }

    public bool HasMoves => _set != null && _override != null && !string.IsNullOrEmpty(_attackKey);
}
