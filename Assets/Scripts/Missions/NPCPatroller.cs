using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Optional companion component that gives a static NPC a schedule.
//
// - By default the NPC stays at `homePoint`.
// - During dusk (17:00–20:00 game time) they walk between `patrolPoints`.
// - Deep night (21:00–04:00) they return to home or a `nightPoint`
//   (perfect for "gather around the fire" without needing to change
//   the NPC's dialogue logic).
//
// Attach to a GameObject that also has a NavMeshAgent. Won't fight
// with dialogue systems — pauses movement while an external script has
// set `HoldPosition = true` (e.g. the NPC is in conversation).
[RequireComponent(typeof(NavMeshAgent))]
public class NPCPatroller : MonoBehaviour
{
    [Header("Waypoints")]
    public Transform homePoint;
    public Transform[] patrolPoints;
    // Optional — where the NPC goes at deep night. Falls back to homePoint.
    public Transform nightPoint;

    [Header("Timings")]
    public float waypointReachedThreshold = 0.6f;
    public float minIdleAtWaypoint = 3f;
    public float maxIdleAtWaypoint = 6f;

    [Header("Movement (auto-configured)")]
    // Same treatment as BarracksUnitAI so the NPC doesn't roller-skate.
    public float agentSpeed = 1.7f;
    public float agentAngularSpeed = 540f;

    [Header("Animator (optional)")]
    public Animator anim;
    // Bool set true when the NPC has reached nightPoint (or homePoint if
    // nightPoint is null) during deep night and is stationary. Wire a
    // Sitting/Resting Animator state that this bool transitions into.
    // Default name matches the CampWorkerAI convention.
    public string sittingAnimBool = "IsSitting";
    public float sittingArriveRadius = 1.4f;
    // Bool set true whenever the NPC is actually moving. For simple
    // animators without a blend tree (just Idle ↔ Walk), wire this bool
    // as the transition condition. Default name: "IsWalking".
    public string walkingAnimBool = "IsWalking";
    public float walkingSpeedThreshold = 0.05f;

    [Header("Animator Fallback (state names)")]
    // When the animator controller has NO Speed float and NO IsWalking
    // bool, the patroller can't drive it via parameters — so it CrossFades
    // directly to these named states instead. Leave blank to disable the
    // fallback. Elias's Lvl1Npc controller has walk/idle STATES but no
    // matching parameter, which is why he never animated — this fixes it
    // without editing the .controller asset.
    public string walkStateName = "Walk";
    public string idleStateName = "Idle";
    public float stateCrossfade = 0.15f;

    // Resolved once in Start — do we have parameters to drive, or must we
    // fall back to CrossFade on named states?
    private bool hasSpeedParam;
    private bool hasWalkingParam;
    private bool useStateFallback;
    private bool wasWalking;

    // Public flag so dialogue or cinematic scripts can pause the patrol.
    [HideInInspector] public bool HoldPosition = false;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            NPCGait.Configure(agent, speed: agentSpeed, angularSpeed: agentAngularSpeed);
            agent.updateRotation = true;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 4f, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }
        if (anim == null) anim = GetComponentInChildren<Animator>();
        // Same root-motion & animator-blend fix as BarracksUnitAI.
        if (anim != null)
        {
            if (anim.applyRootMotion) anim.applyRootMotion = false;
            anim.SetBoolSafe("IsGrounded", true);

            // Detect what the controller actually exposes. If it has
            // neither a Speed float nor an IsWalking bool, drive the
            // animation by CrossFading named states directly.
            hasSpeedParam = AnimatorSafeExtensions.HasParameter(anim, "Speed");
            hasWalkingParam = !string.IsNullOrEmpty(walkingAnimBool)
                           && AnimatorSafeExtensions.HasParameter(anim, walkingAnimBool);
            useStateFallback = !hasSpeedParam && !hasWalkingParam
                            && !string.IsNullOrEmpty(walkStateName);

            if (useStateFallback)
            {
                GameLog.Info($"[NPCPatroller] '{name}': animator '{anim.runtimeAnimatorController?.name}' has no Speed/IsWalking parameter — driving walk via CrossFade to states '{walkStateName}'/'{idleStateName}'. Add an 'IsWalking' bool + transitions if you prefer parameter control.");
            }
            else if (!hasSpeedParam && !hasWalkingParam)
            {
                Debug.LogWarning($"[NPCPatroller] '{name}': animator has no Speed float, no '{walkingAnimBool}' bool, and no walkStateName set — the NPC will not animate while walking. Add one of them.");
            }
        }
        StartCoroutine(ScheduleLoop());
    }

    private void LateUpdate() => NPCGait.GroundSnap(transform);

    private void Update()
    {
        if (anim == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        // Foot-planted gait sync (Speed/MoveX/MoveZ/IsGrounded, animator
        // tempo → agent velocity, root motion off).
        NPCGait.Sync(agent, anim, agentSpeed);

        // Simple two-state animators can just listen to this bool.
        bool walking = agent.velocity.magnitude > walkingSpeedThreshold;
        if (!string.IsNullOrEmpty(walkingAnimBool))
            anim.SetBoolSafe(walkingAnimBool, walking);

        // Parameter-less controllers (Elias's Lvl1Npc): CrossFade named
        // states on the walk/idle transition edge.
        if (useStateFallback && walking != wasWalking)
        {
            wasWalking = walking;
            string target = walking ? walkStateName : idleStateName;
            if (!string.IsNullOrEmpty(target)) anim.CrossFadeInFixedTime(target, stateCrossfade);
        }

        // Sitting bool — deep night, parked, near a rest spot.
        bool shouldSit = !HoldPosition && NPCGait.ShouldSit(agent, sittingArriveRadius);
        if (!string.IsNullOrEmpty(sittingAnimBool))
            anim.SetBoolSafe(sittingAnimBool, shouldSit);

        // Face the campfire/rest spot smoothly while parked overnight so
        // the sitting anim reads facing the light source, not staring
        // into the woods.
        if (shouldSit && nightPoint != null)
            NPCGait.FaceTarget(transform, nightPoint.position, 180f);
    }

    private IEnumerator ScheduleLoop()
    {
        yield return new WaitForSeconds(Random.Range(0f, 1.5f));

        while (true)
        {
            if (HoldPosition)
            {
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = true;
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

            Vector3 dest = ResolveScheduledDestination();
            if (agent != null && agent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(dest, out hit, 4f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);

                    float timeout = 0f;
                    while (timeout < 20f && agent.isOnNavMesh &&
                           (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + waypointReachedThreshold))
                    {
                        if (HoldPosition) break;
                        timeout += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            yield return new WaitForSeconds(Random.Range(minIdleAtWaypoint, maxIdleAtWaypoint));
        }
    }

    private Vector3 ResolveScheduledDestination()
    {
        // Dusk → walk one of the patrol waypoints. Deep night → nightPoint
        // (or homePoint fallback). Any other time → homePoint.
        if (CampSchedule.IsDusk() && patrolPoints != null && patrolPoints.Length > 0)
        {
            var pt = patrolPoints[Random.Range(0, patrolPoints.Length)];
            if (pt != null) return pt.position;
        }
        if (CampSchedule.IsDeepNight())
        {
            if (nightPoint != null) return nightPoint.position;
            if (homePoint != null) return homePoint.position;
        }
        return homePoint != null ? homePoint.position : transform.position;
    }
}
