using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Simple wander AI for a mercenary unit visual living in the camp near the
// barracks. Only Idle + Walk. No combat, no NavMeshAgent chase, no
// interaction with resources — the unit is decorative.
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

    [Header("Visual")]
    public Animator anim;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();

        // Snap onto the navmesh — camp NPCs sometimes spawn slightly above
        // the mesh depending on prefab pivot.
        if (agent != null)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 4f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
        StartCoroutine(WanderRoutine());
    }

    private void Update()
    {
        if (anim != null && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            anim.SetFloatSafe("Speed", agent.velocity.magnitude);
        }
    }

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
            {
                agent.SetDestination(hit.position);
            }

            // Wait to arrive with a hard timeout so a stuck agent doesn't
            // freeze the coroutine forever.
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
