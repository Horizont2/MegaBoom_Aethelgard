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

        // Disable Apply Root Motion — the hero prefabs ship with it enabled
        // for the player, but a NavMeshAgent is now driving the transform.
        // Leaving both on = classic "sliding feet" (units glide instead of
        // stepping in time with the walk cycle).
        if (anim != null && anim.applyRootMotion) anim.applyRootMotion = false;

        if (agent != null)
        {
            agent.speed = agentSpeed;
            agent.acceleration = agentAcceleration;
            agent.angularSpeed = agentAngularSpeed;
            agent.stoppingDistance = agentStoppingDistance;
            agent.autoBraking = true;

            // Snap onto the navmesh — camp NPCs sometimes spawn slightly above
            // the mesh depending on prefab pivot.
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 4f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        // HeroAnimator drives everything off IsGrounded — a stale false keeps
        // it in the fall/land state and the walk cycle never plays. Set it
        // true here since these NPCs live on the ground the whole time.
        if (anim != null) anim.SetBoolSafe("IsGrounded", true);

        StartCoroutine(WanderRoutine());
    }

    private void Update()
    {
        if (anim == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        // Feed the animator the exact same parameters the player controller
        // feeds — Speed absolute, MoveX/MoveZ normalised in local space.
        // HeroAnimator is a blend-tree that reads these; without them the
        // hero stands in Idle even while the transform slides forward.
        Vector3 vel = agent.velocity;
        float mag = vel.magnitude;
        anim.SetFloatSafe("Speed", mag);
        anim.SetBoolSafe("IsGrounded", true);

        if (agent.speed > 0.01f)
        {
            Vector3 local = transform.InverseTransformDirection(vel);
            float ax = Mathf.Clamp(local.x / agent.speed, -1f, 1f);
            float az = Mathf.Clamp(local.z / agent.speed, -1f, 1f);
            anim.SetFloatSafe("MoveX", ax);
            anim.SetFloatSafe("MoveZ", az);
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
