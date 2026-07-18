using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class CampHunterAI : MonoBehaviour
{
    [Header("Link to Building")]
    public CampBuilding myBuilding;

    [Header("Locations")]
    public Transform lodgePoint;
    public Transform forestEdgePoint;

    [Header("Timings")]
    public float prepDuration = 5f;
    public float huntDuration = 15f;

    [Header("Visuals & Animation")]
    public GameObject visualsParent;
    public Animator anim;
    public GameObject carryItemVisual;

    [Header("Effects")]
    public GameObject leavesVFX;

    [Header("Night Schedule (optional)")]
    // Where the hunter walks at deep night to gather at the fire.
    // Leave null → normal hunting cycle overnight.
    public Transform nightGatherPoint;
    public string sittingAnimBool = "IsSitting";
    public float sittingArriveRadius = 1.6f;

    private NavMeshAgent agent;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (carryItemVisual != null) carryItemVisual.SetActive(false);

        // Root motion off — see BarracksUnitAI for the "roller skate" fix.
        if (anim != null && anim.applyRootMotion) anim.applyRootMotion = false;
        if (anim != null) anim.SetBoolSafe("IsGrounded", true);

        // Same trick as CampWorkerAI: park the hunter under the map until
        // the hut is actually built. Otherwise a hunter GO placed at ground
        // level in the prefab would be visible next to a ghost/unbuilt
        // hut. Disable the agent up-front so the sub-map position doesn't
        // fight the NavMesh.
        if (agent != null) agent.enabled = false;
        transform.position = new Vector3(0, -1000f, 0);

        StartCoroutine(InitAndStartRoutine());
    }

    private void Update()
    {
        if (anim != null && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            Vector3 vel = agent.velocity;
            anim.SetFloatSafe("Speed", vel.magnitude);
            anim.SetBoolSafe("IsGrounded", true);
            if (agent.speed > 0.01f)
            {
                Vector3 local = transform.InverseTransformDirection(vel);
                anim.SetFloatSafe("MoveX", Mathf.Clamp(local.x / agent.speed, -1f, 1f));
                anim.SetFloatSafe("MoveZ", Mathf.Clamp(local.z / agent.speed, -1f, 1f));
            }

            bool shouldSit = nightGatherPoint != null
                          && CampSchedule.IsDeepNight()
                          && vel.magnitude < 0.05f
                          && Vector3.Distance(transform.position, nightGatherPoint.position) <= sittingArriveRadius;
            if (!string.IsNullOrEmpty(sittingAnimBool))
                anim.SetBoolSafe(sittingAnimBool, shouldSit);
        }
    }

    private IEnumerator InitAndStartRoutine()
    {
        // Wait until the hut is actually built before showing up. If no
        // building is wired (misconfigured prefab), skip the wait so the
        // hunter still spawns for debugging.
        if (myBuilding != null)
        {
            while (myBuilding.currentLevel == 0) yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        // Spawn point — prefer the lodge, fall back to forest edge, then
        // world origin. Same pattern the worker uses so the hunter always
        // enters the scene next to their hut, not at (0,0,0).
        Vector3 startPos = lodgePoint != null
            ? lodgePoint.position
            : (forestEdgePoint != null ? forestEdgePoint.position : Vector3.zero);

        if (Terrain.activeTerrain != null)
        {
            float terrainHeight = Terrain.activeTerrain.SampleHeight(startPos)
                                + Terrain.activeTerrain.transform.position.y;
            startPos.y = terrainHeight;
        }

        transform.position = startPos;

        if (agent != null)
        {
            agent.enabled = true;
            yield return null;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(startPos, out hit, 6f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.stoppingDistance = 0.5f;
        }

        StartCoroutine(HunterRoutine());
    }

    private bool CheckIfWorkAvailable()
    {
        if (myBuilding != null && myBuilding.IsVisualsFull()) return false;
        return true;
    }

    private IEnumerator WanderAround()
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

        while (true)
        {
            if (CheckIfWorkAvailable()) break;

            if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
            {
                Vector3 randomDirection = Random.insideUnitSphere * 8f;
                randomDirection += transform.position;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomDirection, out hit, 8f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
            }
            yield return new WaitForSeconds(Random.Range(4f, 8f));
        }
    }

    // Walks to the campfire and sits there until deep-night ends. Cancels
    // any in-flight destination and hides the carry visual. Rejoins the
    // normal hunt loop the moment dawn kicks in.
    private IEnumerator NightGatherRoutine()
    {
        if (carryItemVisual != null) carryItemVisual.SetActive(false);
        if (visualsParent != null && !visualsParent.activeSelf) visualsParent.SetActive(true);

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(nightGatherPoint.position, out hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);

            yield return StartCoroutine(WaitForDestination());
            agent.isStopped = true;
        }

        // Poll — leave when it's no longer deep night.
        while (CampSchedule.IsDeepNight())
            yield return new WaitForSeconds(1f);
    }

    private IEnumerator HunterRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, 2f));

        while (true)
        {
            // Night shift beats work — during deep night, drop everything
            // and sit by the fire.
            if (nightGatherPoint != null && CampSchedule.IsDeepNight())
            {
                yield return StartCoroutine(NightGatherRoutine());
                continue;
            }

            if (!CheckIfWorkAvailable())
            {
                if (carryItemVisual != null) carryItemVisual.SetActive(false);
                yield return StartCoroutine(WanderAround());
                continue;
            }

            if (carryItemVisual != null) carryItemVisual.SetActive(false);
            if (!agent.isOnNavMesh) yield break;

            // --- ����� �� ������ ---
            agent.isStopped = false;
            if (lodgePoint != null) agent.SetDestination(lodgePoint.position);
            yield return StartCoroutine(WaitForDestination());

            agent.isStopped = true;
            if (lodgePoint != null) transform.rotation = lodgePoint.rotation;

            if (anim != null) anim.SetTrigger("Work");
            yield return new WaitForSeconds(prepDuration);

            // --- ����� �� ˲�� ---
            agent.isStopped = false;

            if (forestEdgePoint != null)
            {
                NavMeshHit forestHit;
                // ������ ������ ����� � ����� �� 20 �����
                if (NavMesh.SamplePosition(forestEdgePoint.position, out forestHit, 20.0f, NavMesh.AllAreas))
                {
                    agent.SetDestination(forestHit.position);
                }
                else
                {
                    agent.SetDestination(forestEdgePoint.position);
                }
            }

            yield return StartCoroutine(WaitForDestination());

            // �����������: ��������� ������� ������� � ������� ������ ����� (agent.destination), � �� � ��������� ��'�����
            if (Vector3.Distance(transform.position, agent.destination) > 2.5f)
            {
                Debug.LogWarning("[Hunter AI] �� ��� ���� �� ���. ������� ��� ���� �� ��������. ������� ���� ������.");
                continue;
            }

            agent.isStopped = true;
            yield return StartCoroutine(VFXTransitionRoutine(false));
            yield return new WaitForSeconds(huntDuration);

            if (carryItemVisual != null) carryItemVisual.SetActive(true);
            yield return StartCoroutine(VFXTransitionRoutine(true));

            // --- ������������ �� ������ ---
            agent.isStopped = false;
            if (lodgePoint != null) agent.SetDestination(lodgePoint.position);
            yield return StartCoroutine(WaitForDestination());

            agent.isStopped = true;
            if (lodgePoint != null) transform.rotation = lodgePoint.rotation;

            if (carryItemVisual != null) carryItemVisual.SetActive(false);
            yield return new WaitForSeconds(0.5f);

            if (myBuilding != null) myBuilding.ShowNextVisualResource();
            yield return new WaitForSeconds(1.5f);
        }
    }

    private IEnumerator VFXTransitionRoutine(bool show)
    {
        if (leavesVFX != null)
        {
            GameObject fx = Instantiate(leavesVFX, transform.position + Vector3.up, Quaternion.identity);
            Destroy(fx, 3f);
        }
        yield return new WaitForSeconds(0.3f);
        if (visualsParent != null) visualsParent.SetActive(show);
    }

    private IEnumerator WaitForDestination()
    {
        // ������� �����������: ������ 1 ����, ��� Unity ����� ������ ������������� ����
        yield return null;

        float timeout = 0f;
        while (timeout < 45f)
        {
            timeout += Time.deltaTime;
            if (agent != null && agent.isOnNavMesh)
            {
                // ���� ���� � ���䳿 ���������� - ������ ������ � ������ �� ������
                if (agent.pathPending)
                {
                    yield return null;
                    continue;
                }

                // ���� ���� ��������
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathStatus == NavMeshPathStatus.PathPartial)
                {
                    break;
                }

                // ���� �� �����
                if (agent.remainingDistance <= agent.stoppingDistance + 0.5f)
                {
                    break;
                }
            }
            yield return null;
        }
    }
}