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

    // Public flag so dialogue or cinematic scripts can pause the patrol.
    [HideInInspector] public bool HoldPosition = false;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed = agentSpeed;
            agent.angularSpeed = agentAngularSpeed;
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
        }
        StartCoroutine(ScheduleLoop());
    }

    private void Update()
    {
        if (anim == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        Vector3 vel = agent.velocity;
        anim.SetFloatSafe("Speed", vel.magnitude);
        anim.SetBoolSafe("IsGrounded", true);
        if (agent.speed > 0.01f)
        {
            Vector3 local = transform.InverseTransformDirection(vel);
            anim.SetFloatSafe("MoveX", Mathf.Clamp(local.x / agent.speed, -1f, 1f));
            anim.SetFloatSafe("MoveZ", Mathf.Clamp(local.z / agent.speed, -1f, 1f));
        }
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
