using System.Collections;
using UnityEngine;

// Part-2 climax beats along the ride:
//   lookBackProgress — the rider glances back over the shoulder.
//   strikeProgress   — lightning cracks down beside the horse: white flash and
//                      thunder, the horse REARS and holds, the rider is thrown,
//                      FALLS physically to the ground, lands, and gets up.
//
// The rider's controller (OnHorseAnimator) already carries LookBack / Fall /
// GetUp triggers, so those are fired directly — that is the reliable path now
// that the rig is set up. TrailerCutsceneAnim is only the fallback for an
// animator whose controller has no such parameter.
public class TrailerRideEvent : MonoBehaviour
{
    public TrailerHorseRide ride;
    [Range(0f, 1f)] public float lookBackProgress = 0.35f;
    [Range(0f, 1f)] public float strikeProgress = 0.88f;

    [Header("Animator triggers (used when the controller has them)")]
    public string lookBackTrigger = "LookBack";
    public string fallTrigger = "Fall";
    public string getUpTrigger = "GetUp";
    public string horseRearTrigger = "Rear";

    [Header("Clip fallback (only if the controller has no such trigger)")]
    public AnimationClip lookBehindClip;
    public AnimationClip fallingBackClip;
    public AnimationClip horseRearClip;
    public AvatarMask upperBodyMask;
    [Range(0f, 1f)] public float lookBackWeight = 0.85f;
    public AvatarMask fallMask;
    public AvatarMask horseMask;

    [Header("After the beat")]
    [Tooltip("Seconds the horse holds the reared pose before dropping back to Idle. Its controller has an Idle state, so it is cross-faded explicitly — a rear state with no exit transition would otherwise stand frozen forever.")]
    public float rearHoldSeconds = 1.6f;
    [Tooltip("Seconds the get-up takes before the rider is handed back to his normal gameplay controller.")]
    public float getUpSeconds = 2.2f;
    [Tooltip("The rider's normal controller, swapped in once he is back on his feet. Assigned by 'Setup Cutscene Animations'.")]
    public RuntimeAnimatorController heroAnimator;
    [Tooltip("Resting state to enter after the swap, first match wins. HeroAnimator's DEFAULT state is 'Empty', which has no motion — landing there is what showed the bind pose (the T-pose).")]
    public string[] riderIdleStates = { "Locomotion", "Idle" };
    [Tooltip("Resting state for the horse after the rear.")]
    public string[] horseIdleStates = { "Idle" };

    [Header("Fall physics")]
    [Tooltip("Sideways shove off the saddle.")]
    public float throwSideways = 2.0f;
    [Tooltip("Backwards shove off the saddle.")]
    public float throwBackwards = 1.4f;
    [Tooltip("Upward kick before gravity takes over.")]
    public float throwUp = 2.6f;
    public float fallGravity = 14f;
    [Tooltip("Seconds on the ground before he pushes himself up.")]
    public float getUpDelay = 1.4f;
    [Tooltip("Metres between the rider's pivot and his feet. Leave 0 for a rig pivoted between the feet; raise it if he ends up sunk.")]
    public float riderFootOffset = 0f;

    [Header("Fall camera (cuts in at the strike)")]
    public bool useFallCamera = true;
    public float fallCamDistance = 7.5f;
    public float fallCamSide = 4.0f;
    public float fallCamHeight = 1.3f;
    public float fallCamFov = 42f;

    [Header("Strike direction")]
    [Tooltip("How far to the side of the horse the bolt lands.")]
    public float strikeSideOffset = 4.5f;
    [Tooltip("Metres AHEAD of the horse — the bolt cuts him off, which is what makes the horse rear.")]
    public float strikeForwardOffset = 3f;
    [Tooltip("Beat between the flash and the horse rearing. Keep it tiny: the two are cause and effect, and any real gap reads as the horse stopping first and the lightning arriving afterwards.")]
    public float rearDelay = 0.04f;
    [Tooltip("Beat between the rear and the rider losing his seat.")]
    public float throwDelay = 0.45f;

    [Header("Sound")]
    public string neighId = "Animals/Horse_Snort";
    [Tooltip("Thunder crack layered over the bolt. Prefers the trailer's own close thunder and falls back to the ambient one, which is mixed for gameplay and far too polite for this.")]
    public string thunderId = "Trailer/Thunder_Close";
    public string thunderFallbackId = "AMB/AMB_Thunder";
    [Tooltip("Low cinematic hit on the cut to the fall camera.")]
    public string impactStingId = "Trailer/Impact";
    [Tooltip("Seconds of TOTAL silence before the strike. The single most effective thing in the piece: the ear reads the gap, and the thunder that follows lands far harder than its own level would suggest.")]
    public float silenceBeforeStrike = 0.4f;
    [Tooltip("Played as the rider hits the ground.")]
    public string landId = "Player/Land";
    [Tooltip("Played under the look-back, as the horde is noticed.")]
    public string dreadId = "AMB/AMB_Crow";

    [Header("Ending")]
    [Tooltip("Hold on him lying there before the picture goes out. The pause is the point — cutting straight from impact to black throws the moment away.")]
    public float endHold = 2.6f;
    [Tooltip("Seconds to fade to black at the end, handing off to Part 3.")]
    public float endFade = 1.6f;
    [Tooltip("Fade out after the get-up. Turn off while iterating on the beats.")]
    public bool fadeOutAtEnd = true;

    [Header("Part 3 — the battle")]
    [Tooltip("Hand off to the fight once he is on his feet, instead of ending on black. The skeletons that chased him down close the ring.")]
    public bool startBattle = true;
    [Tooltip("Enemy prefabs for the fight — the game's real ones. Assigned by 'Setup Part 2'. The trailer scene has no EnemySpawner to borrow a pool from, which is why the battle found nothing to spawn.")]
    public GameObject[] battleEnemyPrefabs;
    public int battleAttackerCount = 7;

    private TrailerLightningStrike _bolt;
    private Transform _riderGO;
    private Animator _riderAnimator, _horseAnimator;
    private TrailerCutsceneAnim _riderAnim, _horseAnim;
    private bool _struck, _lookedBack;

    private void OnEnable()
    {
        _struck = false; _lookedBack = false;

        if (ride != null)
        {
            // Find the RIDER first, then take the horse from what is left. The
            // old fallback was GetComponentInChildren, which walks depth-first
            // and happily returned the RIDER's animator as the horse — so the
            // rear trigger was fired at a controller that has no such parameter
            // and the horse just kept galloping.
            foreach (var a in ride.GetComponentsInChildren<Animator>(true))
            {
                if (a == null || a.transform == ride.transform) continue;
                if (HasParam(a, lookBackTrigger) || HasParam(a, getUpTrigger))
                {
                    _riderAnimator = a; _riderGO = TopUnder(a.transform, ride.transform); break;
                }
            }

            _horseAnimator = ride.GetComponent<Animator>();
            if (_horseAnimator == null || _horseAnimator == _riderAnimator)
            {
                _horseAnimator = null;
                foreach (var a in ride.GetComponentsInChildren<Animator>(true))
                {
                    if (a == null || a == _riderAnimator) continue;
                    if (_riderGO != null && a.transform.IsChildOf(_riderGO)) continue;
                    if (HasParam(a, horseRearTrigger)) { _horseAnimator = a; break; }
                    _horseAnimator ??= a;
                }
            }

            // Last resort: any animator that is not the rider's.
            if (_riderAnimator == null)
                foreach (var a in ride.GetComponentsInChildren<Animator>(true))
                    if (a != null && a != _horseAnimator) { _riderAnimator = a; _riderGO = TopUnder(a.transform, ride.transform); break; }

            _horseAnim = GetOrAdd(ride.gameObject, _horseAnimator);
            if (_riderGO != null) _riderAnim = GetOrAdd(_riderGO.gameObject, _riderAnimator);
        }

        Debug.Log($"[Trailer] RideEvent ready — rider='{(_riderGO ? _riderGO.name : "NOT FOUND")}' " +
                  $"riderCtrl='{(_riderAnimator != null && _riderAnimator.runtimeAnimatorController != null ? _riderAnimator.runtimeAnimatorController.name : "none")}' " +
                  $"triggers: look={HasParam(_riderAnimator, lookBackTrigger)} fall={HasParam(_riderAnimator, fallTrigger)} getUp={HasParam(_riderAnimator, getUpTrigger)} | " +
                  $"horse='{(_horseAnimator != null ? _horseAnimator.name : "NOT FOUND")}' ctrl='{(_horseAnimator != null && _horseAnimator.runtimeAnimatorController != null ? _horseAnimator.runtimeAnimatorController.name : "none")}' rear={HasParam(_horseAnimator, horseRearTrigger)}");

        if (_bolt == null)
        {
            var go = new GameObject("Trailer_LightningBolt");
            go.transform.SetParent(transform, false);
            go.AddComponent<LineRenderer>();
            _bolt = go.AddComponent<TrailerLightningStrike>();
        }
    }

    private void Update()
    {
        if (ride == null) return;

        if (!_lookedBack && ride.progress01 >= lookBackProgress)
        {
            _lookedBack = true;
            // MASKED OVERLAY FIRST. Firing the controller's LookBack trigger runs
            // the clip full-body on the base layer, which stands the rider up out
            // of the saddle — the legs stop riding. Playing it as an upper-body
            // overlay keeps the riding pose underneath and only turns the torso
            // and head. The trigger is the fallback for a rig with no clip wired.
            bool played = false;
            if (_riderAnim != null && lookBehindClip != null)
            {
                _riderAnim.Play(lookBehindClip, upperBodyMask, hold: false, weight: lookBackWeight);
                played = true;
            }
            if (!played) Fire(_riderAnimator, lookBackTrigger);
            // A cry behind him sells WHY he looks back — and a brief dip in time
            // gives the glance weight, which reads even if the clip itself is
            // still not playing.
            if (AudioManager.Instance != null && !string.IsNullOrEmpty(dreadId))
            {
                AudioManager.Instance.PlaySFX(dreadId);
                // Start the music swelling here, not at the strike — tension has
                // to be rising BEFORE the payoff or the payoff has nothing to
                // release.
                AudioManager.Instance.NotifyCombat(14f);
            }
            if (TrailerCinematicPolish.Instance != null)
                TrailerCinematicPolish.Instance.TimeRamp(0.65f, 0.35f, 0.15f, 0.4f);
            Debug.Log($"[Trailer] BEAT look-back — {(played ? "masked overlay" : "controller trigger")} (clip={(lookBehindClip != null ? lookBehindClip.name : "NONE")}, mask={(upperBodyMask != null ? upperBodyMask.name : "NONE")})");
        }

        if (!_struck && ride.progress01 >= strikeProgress)
        {
            _struck = true;
            StartCoroutine(StrikeRoutine());
        }
    }

    // Flash → horse rears → rider is thrown → he falls to the ground → gets up.
    // Staged so each beat reads, instead of everything happening on one frame.
    private IEnumerator StrikeRoutine()
    {
        var t = ride.transform;

        // The fall camera sits AHEAD of the horse looking back at him, so a bolt
        // placed further ahead than the camera lands BEHIND it and is never on
        // screen — which is why the strike was invisible. Put it on the far side
        // of the horse from the camera and only slightly ahead, so it is between
        // the two and squarely in frame.
        float side = fallCamSide >= 0f ? strikeSideOffset : -strikeSideOffset;
        Vector3 gp = t.position + t.forward * strikeForwardOffset + t.right * side;
        if (TryGround(gp, out float gy)) gp.y = gy;

        // CUT FIRST. The bolt used to fire while the previous shot was still
        // live and the camera only cut 0.12s later — so on screen the horse
        // stopped, and the lightning appeared afterwards. The frame has to be
        // right BEFORE the flash, not after it.
        CutToFallCamera();

        // Then drop the sound out. A held gap before the strike is worth more
        // than any amount of level on the thunder itself — the ear notices the
        // absence, and the crack that follows fills a hole it just made.
        if (silenceBeforeStrike > 0.01f)
        {
            float restore = AudioListener.volume;
            AudioListener.volume = 0f;
            yield return new WaitForSecondsRealtime(silenceBeforeStrike);
            AudioListener.volume = restore;
        }

        if (_bolt != null) _bolt.Strike(gp);
        else Debug.LogWarning("[Trailer] No lightning bolt component — strike has no visual.");
        if (AudioManager.Instance != null)
        {
            if (!TryPlay3D(thunderId, gp)) TryPlay3D(thunderFallbackId, gp);
            TryPlay(impactStingId);
            AudioManager.Instance.NotifyCombat(25f);
        }
        CameraShakeUtil.TryShake(0.45f, 0.25f);

        // The climax of the whole piece: drop into slow motion on the flash and
        // punch the lens. Held through the rear so the horse rises in slow
        // motion, then released as the rider is thrown.
        var polish = TrailerCinematicPolish.Instance;
        if (polish != null)
        {
            polish.TimeRamp(0.35f, rearDelay + throwDelay, 0.06f, 0.45f);
            polish.ImpactPunch(1f, 0.5f);
        }
        Debug.Log($"[Trailer] BEAT strike — bolt at {gp}, horse at {t.position}.");

        // The horse rears ON the flash. Cause and effect have to be the same
        // moment here: a gap between them reads as the horse stopping for its own
        // reasons and the lightning arriving late.
        if (rearDelay > 0.001f) yield return new WaitForSeconds(rearDelay);

        // Horse rears and holds.
        ride.enabled = false;
        if (AudioManager.Instance != null && !string.IsNullOrEmpty(neighId)) AudioManager.Instance.PlaySFX(neighId);
        if (!Fire(_horseAnimator, horseRearTrigger) && _horseAnim != null)
            _horseAnim.Play(horseRearClip, horseMask, hold: true);

        yield return new WaitForSeconds(throwDelay);

        if (_riderGO == null) yield break;

        // Off the saddle, then a REAL fall to the ground — previously the fall
        // animation played in mid-air because nothing ever brought him down.
        _riderGO.SetParent(null, true);
        if (!Fire(_riderAnimator, fallTrigger) && _riderAnim != null)
            _riderAnim.Play(fallingBackClip, fallMask, hold: true);

        // Root motion off, or the fall and get-up clips drag him around and
        // fight the fall we are driving here.
        if (_riderAnimator != null) _riderAnimator.applyRootMotion = false;

        Vector3 vel = (-t.forward * throwBackwards) + (t.right * throwSideways) + (Vector3.up * throwUp);
        // The TERRAIN surface, sampled from the heightmap — not a raycast. The
        // raycast was catching grass and prop colliders on the way down, which is
        // the fall that landed half-way and then dropped again.
        float groundY = TrailerGroundClamp.TryTerrainY(_riderGO.position, out float g0) ? g0 : _riderGO.position.y - 2f;

        while (_riderGO.position.y > groundY + 0.02f)
        {
            vel.y -= fallGravity * Time.deltaTime;
            _riderGO.position += vel * Time.deltaTime;
            if (TrailerGroundClamp.TryTerrainY(_riderGO.position, out float g)) groundY = g;
            if (_riderGO.position.y <= groundY)
            {
                var p = _riderGO.position; p.y = groundY; _riderGO.position = p;
                break;
            }
            yield return null;
        }

        // Stay planted for the rest of the shot: the get-up left his legs buried
        // until the idle popped him back out, because nothing held him on the
        // surface between the beats.
        var clamp = _riderGO.GetComponent<TrailerGroundClamp>() ?? _riderGO.gameObject.AddComponent<TrailerGroundClamp>();
        clamp.footOffset = riderFootOffset;
        clamp.snapNow = true;

        // Landed: face away from the horse, sprawled on the ground.
        Vector3 away = _riderGO.position - t.position; away.y = 0f;
        if (away.sqrMagnitude > 0.01f) _riderGO.rotation = Quaternion.LookRotation(away.normalized);
        CameraShakeUtil.TryShake(0.25f, 0.12f);
        if (!TryPlay3D("Trailer/Body_Fall", _riderGO.position))
            TryPlay3D(landId, _riderGO.position);
        // A shorter, harder hit than the strike — the body meeting the ground.
        if (TrailerCinematicPolish.Instance != null)
            TrailerCinematicPolish.Instance.ImpactPunch(0.7f, 0.3f);

        yield return new WaitForSeconds(getUpDelay);
        if (!Fire(_riderAnimator, getUpTrigger) && _riderAnim != null && fallingBackClip != null)
            _riderAnim.Play(fallingBackClip, fallMask, hold: true);

        // The horse has held the rear long enough — drop it back to Idle instead
        // of standing frozen on the last frame.
        yield return new WaitForSeconds(Mathf.Max(0f, rearHoldSeconds - getUpDelay));
        if (_horseAnim != null) _horseAnim.Release();
        GoToIdle(_horseAnimator, horseIdleStates);

        // Once he is on his feet the cutscene is over, so hand the rider back to
        // his normal gameplay controller.
        yield return new WaitForSeconds(getUpSeconds);
        SwapToHeroAnimator();

        // The chase has to PAY OFF. He is up, and what ran him down arrives —
        // Part 3 takes over from here and ends the piece on its own fade.
        if (startBattle)
        {
            TrailerBattleDirector.Begin(_riderGO != null ? _riderGO : ride.transform,
                                        battleEnemyPrefabs, battleAttackerCount);
            yield break;
        }

        // Or, with the battle off, hold on him and go out on black. The hold is
        // the beat that lets the moment land; cutting from impact straight to the
        // next thing spends it for nothing.
        if (fadeOutAtEnd)
        {
            yield return new WaitForSeconds(endHold);
            if (TrailerCinematicPolish.Instance != null)
                TrailerCinematicPolish.Instance.FadeToBlack(endFade);
            Debug.Log("[Trailer] Part 2 complete — faded out.");
        }
    }

    // Enter a resting state by name, trying the bare name AND the full path.
    // Animator.StringToHash("Idle") only matches a state sitting directly in the
    // root state machine; anything nested needs "Base Layer.Idle", which is why
    // the first attempt reported HeroAnimator as having no Idle when it plainly
    // does.
    private static bool GoToIdle(Animator a, string[] candidates, float fade = 0.3f)
    {
        if (a == null || a.runtimeAnimatorController == null || candidates == null) return false;
        foreach (var name in candidates)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var path in new[] { name, "Base Layer." + name })
            {
                int h = Animator.StringToHash(path);
                if (!a.HasState(0, h)) continue;
                if (fade > 0f) a.CrossFade(h, fade, 0);
                else a.Play(h, 0, 0f);
                return true;
            }
        }
        Debug.LogWarning($"[Trailer] '{a.name}' has none of [{string.Join(", ", candidates)}] on layer 0 — it will hold its last pose.");
        return false;
    }

    private void SwapToHeroAnimator()
    {
        if (_riderAnimator == null || heroAnimator == null)
        {
            Debug.LogWarning($"[Trailer] Cannot restore the rider's controller — animator={(_riderAnimator != null)}, heroAnimator={(heroAnimator != null)}.");
            return;
        }
        // The overlay graph holds an AnimatorControllerPlayable built from the
        // OLD controller, so it has to come down before the swap or it keeps
        // driving the animator with the cutscene rig.
        if (_riderAnim != null) _riderAnim.Detach();

        _riderAnimator.runtimeAnimatorController = heroAnimator;
        _riderAnimator.Rebind();

        // Rebind leaves the animator on the controller's default state, and if
        // that state has no motion the rig shows its bind pose — the T-pose.
        // Enter Idle explicitly, then evaluate a frame so the pose is applied
        // before anything is rendered.
        // Locomotion is gated on IsGrounded / Speed / MoveX / MoveZ, which
        // PlayerController normally drives — and the trailer's rider has no
        // PlayerController. Without them the controller never leaves its default
        // 'Empty' state, which has no motion, so the rig shows its bind pose.
        var hold = _riderGO != null ? _riderGO.gameObject : _riderAnimator.gameObject;
        var holder = hold.GetComponent<TrailerAnimatorHold>() ?? hold.AddComponent<TrailerAnimatorHold>();
        holder.animator = _riderAnimator;

        GoToIdle(_riderAnimator, riderIdleStates, 0f);
        _riderAnimator.Update(0f);
        Debug.Log($"[Trailer] Rider handed back to '{heroAnimator.name}'.");
    }

    // Cut to a low camera set off to the side so BOTH the rearing horse and the
    // falling rider are in frame — the old one sat too close to read.
    private void CutToFallCamera()
    {
        if (!useFallCamera) return;

        var t = ride.transform;
        Vector3 pos = t.position + t.forward * fallCamDistance - t.right * fallCamSide;
        if (TryGround(pos, out float gy)) pos.y = gy;
        pos.y += fallCamHeight;

        var go = new GameObject("CM_Part2_Fall");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        Vector3 look = t.position + Vector3.up * 1.4f;
        go.transform.rotation = Quaternion.LookRotation((look - pos).normalized);

        var cam = go.AddComponent<Unity.Cinemachine.CinemachineCamera>();
        cam.Lens.FieldOfView = fallCamFov;
        var pr = cam.Priority; pr.Value = 300; cam.Priority = pr;   // beats the cutter's 100

        var brain = Object.FindFirstObjectByType<Unity.Cinemachine.CinemachineBrain>();
        if (brain != null)
            brain.DefaultBlend = new Unity.Cinemachine.CinemachineBlendDefinition(
                Unity.Cinemachine.CinemachineBlendDefinition.Styles.Cut, 0f);

        cam.PreviousStateIsValid = false;
        cam.InternalUpdateCameraState(Vector3.up, -1f);
    }

    // ── Animator helpers ─────────────────────────────────────────────────
    // Play if the event is actually wired; report whether it was, so a beat can
    // fall back to the gameplay event it was borrowing before.
    private static bool TryPlay(string id)
    {
        if (AudioManager.Instance == null || !AudioManager.Instance.HasEvent(id)) return false;
        AudioManager.Instance.PlaySFX(id);
        return true;
    }

    private static bool TryPlay3D(string id, Vector3 at)
    {
        if (AudioManager.Instance == null || !AudioManager.Instance.HasEvent(id)) return false;
        AudioManager.Instance.PlaySFX3D(id, at);
        return true;
    }

    private static bool HasParam(Animator a, string param)
    {
        if (a == null || string.IsNullOrEmpty(param) || a.runtimeAnimatorController == null) return false;
        foreach (var p in a.parameters) if (p.name == param) return true;
        return false;
    }

    private static bool Fire(Animator a, string param)
    {
        if (!HasParam(a, param)) return false;
        a.ResetTrigger(param);
        a.SetTrigger(param);
        Debug.Log($"[Trailer] Fired '{param}' on '{a.name}'.");
        return true;
    }

    private static TrailerCutsceneAnim GetOrAdd(GameObject go, Animator anim)
    {
        var c = go.GetComponent<TrailerCutsceneAnim>() ?? go.AddComponent<TrailerCutsceneAnim>();
        if (anim != null) c.animator = anim;
        return c;
    }

    private static Transform TopUnder(Transform t, Transform root)
    {
        var cur = t;
        while (cur != null && cur.parent != null && cur.parent != root) cur = cur.parent;
        return cur;
    }

    private static readonly string[] GroundNames = { "ground", "floor", "road", "path" };

    // The TERRAIN wins outright. Taking the highest ground-ish hit landed the
    // rider on whatever prop or foliage collider stood tallest, and only then did
    // he settle onto the real ground — which is the two-stage fall.
    private static bool TryGround(Vector3 pos, out float y)
    {
        y = pos.y;
        var hits = Physics.RaycastAll(pos + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);

        bool onTerrain = false; float terrainY = 0f;
        float best = float.NegativeInfinity; bool found = false;

        foreach (var h in hits)
        {
            var col = h.collider; if (col == null) continue;
            if (col is TerrainCollider || col.GetComponentInParent<Terrain>() != null)
            {
                if (!onTerrain || h.point.y > terrainY) { terrainY = h.point.y; onTerrain = true; }
                continue;
            }
            string n = col.name.ToLowerInvariant();
            foreach (var s in GroundNames)
                if (n.Contains(s)) { if (h.point.y > best) { best = h.point.y; found = true; } break; }
        }

        if (onTerrain) { y = terrainY; return true; }
        if (found) { y = best; return true; }
        return false;
    }
}
