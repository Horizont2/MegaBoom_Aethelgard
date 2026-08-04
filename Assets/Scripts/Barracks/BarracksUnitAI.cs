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
        // Gait sync only — barracks units are decorative, they wander
        // 24/7 and do NOT gather at the fire (that's for the camp
        // workers / hunters / storage NPC only).
        NPCGait.Sync(agent, anim, agentSpeed);
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
}
