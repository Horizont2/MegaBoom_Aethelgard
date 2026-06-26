using UnityEngine;
using UnityEngine.AI;

public class CatAI : AnimalAI
{
    [Header("Cat Settings")]
    public float walkSpeed = 1.5f;
    public float runSpeed = 4.5f;
    public float fleeDistance = 5f;

    [Header("Effects & Interaction")]
    public ParticleSystem heartParticles;
    public string poiTag = "CatPOI";

    private Transform currentPOI;
    private bool isResting = false;
    private bool playerInRange = false;

    protected override void Awake()
    {
        base.Awake();
        agent.speed = walkSpeed;
    }

    protected override void Update()
    {
        base.Update();

        if (playerInRange && currentState != AnimalState.Flee && currentState != AnimalState.Jumping)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                PetCat();
            }
        }
    }

    protected override void UpdateIdle()
    {
        agent.speed = 0f;
        stateTimer += Time.deltaTime;

        if (stateTimer >= Random.Range(minStateTime, maxStateTime))
        {
            WakeUp();

            if (Random.value > 0.5f && FindRandomPOI())
            {
                ChangeState(AnimalState.MoveToPOI);
            }
            else
            {
                Vector3 dest = GetRandomNavMeshPoint(startPosition, wanderRadius);
                agent.SetDestination(dest);
                agent.speed = walkSpeed;
                ChangeState(AnimalState.Wander);
            }
        }
    }

    protected override void UpdateWander()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            ChangeState(AnimalState.Idle);
        }
    }

    protected override void UpdateMoveToPOI()
    {
        if (currentPOI == null)
        {
            ChangeState(AnimalState.Idle);
            return;
        }

        agent.SetDestination(currentPOI.position);
        agent.speed = walkSpeed;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            ChangeState(AnimalState.InteractPOI);
        }
    }

    protected override void UpdateInteractPOI()
    {
        if (!isResting)
        {
            isResting = true;
            agent.speed = 0f;

            transform.position = currentPOI.position;
            transform.rotation = currentPOI.rotation;
        }

        stateTimer += Time.deltaTime;
        if (stateTimer > 30f)
        {
            ChangeState(AnimalState.Idle);
        }
    }

    protected override void UpdateFlee()
    {
        WakeUp();
        agent.speed = runSpeed;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            ChangeState(AnimalState.Idle);
        }
    }

    protected override void CheckPlayerPresence()
    {
        if (player == null || currentState == AnimalState.Jumping) return;

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist < fleeDistance)
        {
            // Ô²ÊÑ ÎÏÒÈÌ²ÇÀÖ²¯: Âèêîðèñòîâóºìî êåøîâàíèé playerCC
            if (playerCC != null && playerCC.velocity.magnitude > 3f && currentState != AnimalState.Flee)
            {
                if (GlobalHUD.Instance != null && playerInRange) GlobalHUD.Instance.HidePrompt();
                playerInRange = false;

                Vector3 fleeDirection = (transform.position - player.position).normalized;
                Vector3 fleeTarget = transform.position + fleeDirection * 8f;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(fleeTarget, out hit, 8f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    ChangeState(AnimalState.Flee);
                    return;
                }
            }
        }

        if (dist <= 2.5f && currentState != AnimalState.Flee)
        {
            if (!playerInRange)
            {
                playerInRange = true;
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Pet Cat");
            }
        }
        else if (dist > 2.5f && playerInRange)
        {
            playerInRange = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    private void PetCat()
    {
        if (currentState == AnimalState.Jumping || currentState == AnimalState.Flee) return;

        if (!isResting)
        {
            Vector3 lookDir = (player.position - transform.position).normalized;
            lookDir.y = 0;
            transform.rotation = Quaternion.LookRotation(lookDir);
        }

        if (heartParticles != null) heartParticles.Play();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Animal_CatMeow);
    }

    private void WakeUp()
    {
        if (isResting) isResting = false;
    }

    private bool FindRandomPOI()
    {
        GameObject[] pois = GameObject.FindGameObjectsWithTag(poiTag);
        if (pois.Length == 0) return false;

        currentPOI = pois[Random.Range(0, pois.Length)].transform;
        return true;
    }
}