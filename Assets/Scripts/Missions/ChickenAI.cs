using UnityEngine;
using UnityEngine.AI;

public class ChickenAI : AnimalAI
{
    [Header("Chicken Settings")]
    public float walkSpeed = 1f;
    public float panicSpeed = 3.5f;
    public float panicDistance = 4f;

    [Header("Effects")]
    public ParticleSystem featherParticles;

    private bool isPanicking = false;
    private float panicTimer = 0f;
    private float nextIdleCluckTime = 0f;

    protected override void Awake()
    {
        base.Awake();
        agent.speed = walkSpeed;
    }

    protected override void UpdateIdle()
    {
        agent.speed = 0f;
        stateTimer += Time.deltaTime;

        // Quiet idle cluck every 8–16s per bird so a camp full of hens
        // has a bit of ambient life instead of eerie silence.
        if (Time.time >= nextIdleCluckTime)
        {
            nextIdleCluckTime = Time.time + Random.Range(8f, 16f);
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(AudioID.Animal_Chicken);
        }

        if (stateTimer >= Random.Range(minStateTime, maxStateTime))
        {
            Vector3 dest = GetRandomNavMeshPoint(startPosition, wanderRadius);
            agent.SetDestination(dest);
            agent.speed = walkSpeed;
            ChangeState(AnimalState.Wander);
        }
    }

    protected override void UpdateWander()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            ChangeState(AnimalState.Idle);
        }
    }

    protected override void UpdateFlee()
    {
        if (!isPanicking)
        {
            isPanicking = true;
            agent.speed = panicSpeed;
            if (featherParticles != null) featherParticles.Play();
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Animal_Chicken);
        }

        panicTimer += Time.deltaTime;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (panicTimer > 4f)
            {
                isPanicking = false;
                panicTimer = 0f;

                if (featherParticles != null) featherParticles.Stop();
                ChangeState(AnimalState.Idle);
            }
            else
            {
                Vector3 dest = GetRandomNavMeshPoint(transform.position, 5f);
                agent.SetDestination(dest);
            }
        }
    }

    protected override void UpdateMoveToPOI() { }
    protected override void UpdateInteractPOI() { }

    protected override void CheckPlayerPresence()
    {
        if (player == null || currentState == AnimalState.Jumping || currentState == AnimalState.Flee) return;

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist < panicDistance)
        {
            // Բ�� ����̲��ֲ�: ������������� ��������� playerCC
            if (playerCC != null && playerCC.velocity.magnitude > 2f)
            {
                Vector3 runDirection = (transform.position - player.position).normalized;
                Vector3 runTarget = transform.position + runDirection * 6f;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(runTarget, out hit, 6f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    ChangeState(AnimalState.Flee);
                }
            }
        }
    }
}