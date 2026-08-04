using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Simple wander AI for a mercenary unit visual living in the camp near the
// barracks. Only Idle + Walk. No combat, no NavMeshAgent chase, no
// interaction with resources — the unit is decorative.
//
// Auto-configures the prefab at Start so designers don't have to manually
// toggle root motion / navmesh speeds. Hero prefabs (Knight/Ranger/Rogue)
// ship with Apply Root Motion = ON because the player uses them; when a
// NavMeshAgent also drives the transform the two systems fight and the
// character "roller-skates" across the ground. We disable root motion for
// the AI-controlled copies and feed the same animator parameters the player
// controller feeds (Speed, IsGrounded, MoveX, MoveZ) so the same
// HeroAnimator plays walk cycles correctly.
[RequireComponent(typeof(NavMeshAgent))]
public class BarracksUnitAI : MonoBehaviour
{
    [Header("Wander")]
    // Points defined by BarracksBuilding — the unit picks one at random each
    // rest cycle. Empty array → wanders within a circle around `home`.
    public Transform[] wanderPoints;
    public Transform home;
    public float wanderRadius = 4f;
    public float minIdleSeconds = 2f;
    public float maxIdleSeconds = 6f;

    [Header("Movement Tuning")]
    // A relaxed patrol pace — walk cycles on HeroAnimator sit around 1.5-2.5
    // metres/sec; matching agent.speed keeps the feet planted.
    public float agentSpeed = 2.0f;
    public float agentAcceleration = 20f;
    public float agentAngularSpeed = 540f;
    public float agentStoppingDistance = 0.4f;

    [Header("Night Schedule (optional)")]
    // Drop the campfire transform here and the merc walks over at deep
    // night, faces it, and sits until dawn. Leave null → mercs wander
    // all night (previous behaviour).
    public Transform nightGatherPoint;
    public string sittingAnimBool = "IsSitting";
    public float sittingArriveRadius = 1.6f;

    [Header("Visual")]
    public Animator anim;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        if (agent != null)
        {
            NPCGait.Configure(agent,
                              speed: agentSpeed,
                              acceleration: agentAcceleration,
                              angularSpeed: agentAngularSpeed,
                              stoppingDistance: agentStoppingDistance);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 4f, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        if (anim != null) anim.SetBoolSafe("IsGrounded", true);

        StartCoroutine(WanderRoutine());
    }

    private void Update()
    {
        NPCGait.Sync(agent, anim, agentSpeed);
        if (anim != null && !string.IsNullOrEmpty(sittingAnimBool))
            anim.SetBoolSafe(sittingAnimBool, NPCGait.ShouldSit(agent, sittingArriveRadius));
    }

    private void LateUpdate() => NPCGait.GroundSnap(transform);

    private IEnumerator WanderRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, 1f));

        while (true)
        {
            if (agent == null || !agent.isOnNavMesh)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Night beats wander — walk to the fire and stay there.
            if (nightGatherPoint != null && CampSchedule.IsDeepNight())
            {
                yield return StartCoroutine(NightGatherRoutine());
                continue;
            }

            Vector3 dest;
            if (wanderPoints != null && wanderPoints.Length > 0)
            {
                var pt = wanderPoints[Random.Range(0, wanderPoints.Length)];
                dest = pt != null ? pt.position : transform.position;
            }
            else
            {
                Vector3 baseP = home != null ? home.position : transform.position;
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                dest = baseP + new Vector3(offset.x, 0, offset.y);
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(dest, out hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);

            float timeout = 0f;
            while (timeout < 20f && agent.isOnNavMesh && (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.2f))
            {
                timeout += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(Random.Range(minIdleSeconds, maxIdleSeconds));
        }
    }

    private IEnumerator NightGatherRoutine()
    {
        Vector2 jitter = Random.insideUnitCircle * 1.4f;
        Vector3 dest = nightGatherPoint.position + new Vector3(jitter.x, 0f, jitter.y);
        NavMeshHit hit;
        if (NavMesh.SamplePosition(dest, out hit, 4f, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }

        float timeout = 0f;
        while (timeout < 15f && agent.isOnNavMesh && (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.4f))
        {
            timeout += Time.deltaTime;
            yield return null;
        }

        // Face the fire smoothly on arrival.
        float faceTimer = 0f;
        while (faceTimer < 1.2f)
        {
            NPCGait.FaceTarget(transform, nightGatherPoint.position, 240f);
            faceTimer += Time.deltaTime;
            yield return null;
        }

        while (CampSchedule.IsDeepNight())
            yield return new WaitForSeconds(1f);
    }
}
