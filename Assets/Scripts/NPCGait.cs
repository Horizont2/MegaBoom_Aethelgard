using UnityEngine;
using UnityEngine.AI;

// Shared locomotion polish for every camp NPC. Kills two ugly-looking
// bugs at once:
//
//   1. FOOT-SLIDING. If the NavMeshAgent moves at, say, 2 m/s but the
//      walk-cycle animation plays at its authored tempo (usually calibrated
//      for ~1.4 m/s), the feet appear to skate. Sync animator.speed to the
//      ratio agent.velocity/agentBaseSpeed and the tempo matches ground
//      speed at every gait.
//
//   2. INSTANT SNAP TURNS. NavMeshAgent's default 120°/s angularSpeed
//      combined with a stationary agent means turn-in-place looks robotic.
//      Configure() bumps angularSpeed to 540°/s and acceleration high
//      enough that starts/stops don't look like a physics glitch.
//
// Also collects the Speed / MoveX / MoveZ / IsGrounded animator writes
// that were duplicated across five AI scripts.
public static class NPCGait
{
    // Sensible defaults for a slow-walking camp NPC. Every AI can override
    // by passing its own values.
    public const float DEFAULT_SPEED = 1.7f;
    public const float DEFAULT_ACCEL = 20f;
    public const float DEFAULT_ANGULAR = 540f;
    public const float DEFAULT_STOP_DIST = 0.4f;

    // Tempo-match tuning. If the walk-cycle animation is authored for a
    // hero moving at ~1.4 m/s, that's the reference. Anything above it
    // plays faster (feet keep up); anything below plays slower (a slow
    // shuffle at low velocity).
    public const float REFERENCE_ANIM_SPEED = 1.4f;
    // Clamp so a stationary NPC doesn't freeze the animator (which
    // freezes IK, breathing, etc.) or a running NPC doesn't hit
    // cartoon-fast playback.
    public const float MIN_ANIM_SPEED = 0.75f;
    public const float MAX_ANIM_SPEED = 1.6f;

    // Call from Start() after grabbing the agent reference. Writes to
    // fields the individual AI probably already set — safe to call after
    // per-AI overrides so those win.
    public static void Configure(NavMeshAgent agent,
                                 float speed = DEFAULT_SPEED,
                                 float acceleration = DEFAULT_ACCEL,
                                 float angularSpeed = DEFAULT_ANGULAR,
                                 float stoppingDistance = DEFAULT_STOP_DIST)
    {
        if (agent == null) return;
        agent.speed = speed;
        agent.acceleration = acceleration;
        agent.angularSpeed = angularSpeed;
        agent.stoppingDistance = stoppingDistance;
        agent.autoBraking = true;
        // baseOffset 0 pins the agent flat on the NavMesh — grass detail
        // colliders otherwise nudge it up per-frame and it hovers.
        agent.baseOffset = 0f;
    }

    // Call from Update(). Feeds the animator the same params the player
    // controller does, and tempo-syncs the walk cycle so feet don't slide.
    // Also disables root motion since NavMeshAgent is driving position —
    // both together = the "roller-skate" bug.
    public static void Sync(NavMeshAgent agent, Animator anim, float baseSpeed = DEFAULT_SPEED)
    {
        if (agent == null || anim == null) return;
        if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        if (anim.applyRootMotion) anim.applyRootMotion = false;

        Vector3 vel = agent.velocity;
        float mag = vel.magnitude;

        anim.SetBoolSafe("IsGrounded", true);
        anim.SetFloatSafe("Speed", mag);

        if (baseSpeed > 0.01f)
        {
            Vector3 local = Vector3.zero;
            if (mag > 0.001f) local = anim.transform.InverseTransformDirection(vel);
            anim.SetFloatSafe("MoveX", Mathf.Clamp(local.x / baseSpeed, -1f, 1f));
            anim.SetFloatSafe("MoveZ", Mathf.Clamp(local.z / baseSpeed, -1f, 1f));
        }

        // Foot-planted tempo sync. When the NPC is moving, scale animator
        // playback so the walk cycle covers exactly the ground distance.
        // When stopped, hold at 1.0 so idle plays at authored tempo.
        if (mag > 0.05f)
        {
            float tempo = Mathf.Clamp(mag / REFERENCE_ANIM_SPEED, MIN_ANIM_SPEED, MAX_ANIM_SPEED);
            anim.speed = tempo;
        }
        else
        {
            anim.speed = 1f;
        }
    }

    // Smoothly rotate `t` to face `target` on the XZ plane. Call from
    // Update while parked (agent is stopped) — replaces the hard
    // transform.rotation = LookRotation snaps that read as instant.
    public static void FaceTarget(Transform t, Vector3 target, float degPerSecond = 180f)
    {
        if (t == null) return;
        Vector3 dir = target - t.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        t.rotation = Quaternion.RotateTowards(t.rotation, want, degPerSecond * Time.deltaTime);
    }

    // Ground-snap helper for grass-heavy terrain that lifts the agent up.
    // Call from LateUpdate. Only kicks in on drifts > threshold so cliff
    // edges and small NavMesh height variations aren't clobbered.
    public static void GroundSnap(Transform t, float driftThreshold = 0.2f)
    {
        if (t == null || Terrain.activeTerrain == null) return;
        Terrain terrain = Terrain.activeTerrain;
        float groundY = terrain.SampleHeight(t.position) + terrain.transform.position.y;
        float drift = t.position.y - groundY;
        if (Mathf.Abs(drift) > driftThreshold)
        {
            Vector3 p = t.position;
            p.y = groundY;
            t.position = p;
        }
    }

    // Convenience — combined "at deep night, standing still, near a rest
    // spot" test used by the sitting-bool checks in every AI.
    public static bool ShouldSit(NavMeshAgent agent, float arriveRadius = 1.6f)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        if (!CampSchedule.IsDeepNight()) return false;
        return agent.velocity.sqrMagnitude < 0.0025f && agent.remainingDistance < arriveRadius;
    }
}
