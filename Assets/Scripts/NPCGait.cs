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
                                 float stoppingDistance = DEFAULT_STOP_DIST,
                                 ObstacleAvoidanceType avoidance = ObstacleAvoidanceType.LowQualityObstacleAvoidance)
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

        // ==== AVOIDANCE QUALITY IS A PER-AGENT CPU BUDGET ====
        //
        // Every camp agent was left on the prefab default, HighQualityObstacle-
        // Avoidance: the most expensive tier, sampling the largest velocity-
        // obstacle set against every neighbour, on the main thread's navigation
        // job every frame — for NPCs that walk fixed routes across an empty
        // camp and almost never have to dodge anything.
        //
        // Low quality still avoids; it just tests fewer candidate velocities.
        // Callers that genuinely crowd (army units in a melee) pass a higher
        // tier explicitly.
        agent.obstacleAvoidanceType = avoidance;
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
    private static readonly RaycastHit[] s_groundHits = new RaycastHit[8];

    // ==== NOT EVERY COLLIDER IS A FLOOR ====
    //
    // The snap ray was cast against ~0 — every layer in the project — with an
    // eight-hit buffer. In the camp that means 809 birches' worth of Nature
    // colliders, loot, water and UI volumes all competing for those eight
    // slots, so the cast was both the most expensive form of the query and the
    // one most likely to miss the actual floor by overflowing.
    //
    // Nobody stands on a tree, a coin or the water plane. Keep the layers that
    // can genuinely be ground (Default, IgnorePlayer, PlayerPhysics, Obstacles,
    // InvisibleWall and anything unnamed) and drop the rest.
    private const int GROUND_MASK = ~((1 << 1)  | // TransparentFX
                                      (1 << 2)  | // Ignore Raycast
                                      (1 << 4)  | // Water
                                      (1 << 5)  | // UI
                                      (1 << 6)  | // Player
                                      (1 << 9)  | // Damageable
                                      (1 << 10) | // MinimapOnly
                                      (1 << 12) | // LootPhysics
                                      (1 << 14) | // NPC
                                      (1 << 15) | // Nature
                                      (1 << 16)); // MinimapGraphics

    // Agent-aware overload. Writing transform.position every frame while a
    // NavMeshAgent is driving that same transform desyncs the agent from its
    // internal position: the agent corrects, the snap fights back, and the NPC
    // ends up jittering/turning on the spot instead of walking its path. While
    // the agent is live the NavMesh already supplies a correct Y, so only step
    // in on a REAL drift (fell through the floor, spawned above it).
    public static void GroundSnap(Transform t, NavMeshAgent agent, float driftThreshold = 0.2f)
    {
        bool agentDriving = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && agent.updatePosition;
        GroundSnap(t, agentDriving ? Mathf.Max(driftThreshold, 1.5f) : driftThreshold);
    }

    public static void GroundSnap(Transform t, float driftThreshold = 0.2f)
    {
        if (t == null) return;

        // Prefer a real downward raycast so NPCs standing on a wooden deck /
        // raised camp floor snap to THAT surface. Terrain.SampleHeight ignores
        // mesh colliders, so at the campfire (often on a non-terrain floor) it
        // buried the NPC underground — most visible during the low ground-sit.
        float groundY;
        Vector3 origin = t.position + Vector3.up * 3f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_groundHits, 8f, GROUND_MASK, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = s_groundHits[i];
            if (h.collider == null || h.collider.isTrigger) continue;
            // Ignore the NPC's own colliders.
            if (h.collider.transform == t || h.collider.transform.IsChildOf(t)) continue;
            // NEVER snap onto a character. Raycasting all colliders and taking the
            // highest hit meant that when the player walked into an NPC, the ray
            // hit the PLAYER and the NPC climbed onto their head. Skip the player
            // (CharacterController), tagged Player/Enemy, and any AI-driven body.
            if (IsCharacterCollider(h.collider)) continue;
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        if (found) groundY = best;
        else if (Terrain.activeTerrain != null)
            groundY = Terrain.activeTerrain.SampleHeight(t.position) + Terrain.activeTerrain.transform.position.y;
        else return;

        float drift = t.position.y - groundY;
        if (Mathf.Abs(drift) > driftThreshold)
        {
            Vector3 p = t.position;
            p.y = groundY;
            t.position = p;
        }
    }

    private static bool IsCharacterCollider(Collider c)
    {
        if (c is CharacterController) return true;                 // the player
        if (c.CompareTag("Player") || c.CompareTag("Enemy")) return true;
        if (c.attachedRigidbody != null) return true;             // physics props / ragdolls
        // AI-driven bodies (other NPCs, allies, enemies).
        if (c.GetComponentInParent<NavMeshAgent>() != null) return true;
        return false;
    }

    // Convenience — combined "at deep night, standing still, near a rest
    // spot" test used by the sitting-bool checks in every AI.
    // ==== "STOPPED AT NIGHT" IS NOT "SITTING BY THE FIRE" ====
    //
    // The test was: it is deep night, the agent is not moving, and it has
    // little path left. That is true of an NPC sitting at the campfire — and
    // equally true of a lumberjack STANDING AT A TREE with its agent stopped
    // mid-chop, which is why the woodcutter sat down while swinging an axe.
    //
    // The worker's own routine checks the clock once per task, and a chopping
    // cycle is many seconds long, so night falling mid-chop leaves it working
    // while this says "sit". The animator won that argument every frame.
    //
    // A seat makes the question the right one: sit only when actually AT the
    // place you were sent to sit. Callers that have no seat keep the old
    // behaviour, since for them "stopped at night" really is all there is.
    public static bool ShouldSit(NavMeshAgent agent, float arriveRadius = 1.6f, Transform seat = null)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        if (!CampSchedule.IsDeepNight()) return false;
        if (agent.velocity.sqrMagnitude >= 0.0025f || agent.remainingDistance >= arriveRadius) return false;

        if (seat == null) return true;
        Vector3 d = agent.transform.position - seat.position; d.y = 0f;
        // A little slack over the arrive radius: the agent stops at its
        // stoppingDistance, which is short of the seat by design.
        float reach = arriveRadius + 1.5f;
        return d.sqrMagnitude <= reach * reach;
    }
}
