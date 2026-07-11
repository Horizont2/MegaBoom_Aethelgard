using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class StorageWorkerAI : MonoBehaviour
{
    public NavMeshAgent agent;
    public Animator anim;
    public GameObject carryVisual;
    public Transform storageDropPoint;

    private List<CampBuilding> productionBuildings = new List<CampBuilding>();

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (carryVisual != null) carryVisual.SetActive(false);

        // Root motion off (see BarracksUnitAI for the roller-skate fix).
        if (anim != null && anim.applyRootMotion) anim.applyRootMotion = false;
        if (anim != null) anim.SetBoolSafe("IsGrounded", true);

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
        }
    }

    private IEnumerator InitAndStartRoutine()
    {
        yield return new WaitForSeconds(0.5f);
        if (transform.position.y < -2f)
        {
            if (agent != null) agent.enabled = false;
            yield return new WaitForSeconds(2.5f);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 10f, NavMesh.AllAreas))
                transform.position = hit.position;
        }
        if (agent != null)
        {
            agent.enabled = true;
            agent.Warp(transform.position);
            agent.stoppingDistance = 0.5f;
        }

        FindBuildings();
        StartCoroutine(LogisticsRoutine());
    }

    void FindBuildings()
    {
        productionBuildings.Clear();
        CampBuilding[] all = FindObjectsByType<CampBuilding>(FindObjectsSortMode.None);
        foreach (var b in all)
        {
            if (!b.isStorageVault) productionBuildings.Add(b);
        }
    }

    private IEnumerator WanderAroundStorage()
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

        if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
        {
            Vector3 randomDirection = Random.insideUnitSphere * 6f;
            randomDirection += storageDropPoint != null ? storageDropPoint.position : transform.position;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomDirection, out hit, 6f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
        yield return new WaitForSeconds(Random.Range(4f, 8f));
    }

    private IEnumerator LogisticsRoutine()
    {
        yield return new WaitForSeconds(3f);

        while (true)
        {
            bool collectedAnything = false;

            foreach (var building in productionBuildings)
            {
                if (building != null && building.currentLevel > 0 && building.pendingResourcesCount > 0)
                {
                    collectedAnything = true;

                    // 1. ����� �� Pickup Point (� �������� �������)
                    agent.isStopped = false;
                    Vector3 rawTargetPos = building.pickupPoint != null ? building.pickupPoint.position : building.transform.position;
                    Vector3 targetPos = rawTargetPos;

                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(rawTargetPos, out hit, 5f, NavMesh.AllAreas))
                    {
                        targetPos = hit.position;
                    }

                    agent.SetDestination(targetPos);
                    yield return StartCoroutine(WaitArrival());

                    agent.isStopped = true;
                    transform.LookAt(targetPos);

                    // 2. ϳ�������
                    if (anim != null) anim.SetTrigger("Pickup");
                    yield return new WaitForSeconds(1.5f);

                    int amount = building.CollectResourcesByStorageNPC();
                    if (carryVisual != null) carryVisual.SetActive(true);

                    yield return new WaitForSeconds(0.5f);

                    // 3. ������ �� ����� (� �������� ������� ������� ��� ����� ������!)
                    agent.isStopped = false;
                    Vector3 rawDropPos = storageDropPoint != null ? storageDropPoint.position : transform.position;
                    Vector3 dropTargetPos = rawDropPos;

                    NavMeshHit dropHit;
                    if (NavMesh.SamplePosition(rawDropPos, out dropHit, 5f, NavMesh.AllAreas))
                    {
                        dropTargetPos = dropHit.position;
                    }

                    agent.SetDestination(dropTargetPos);
                    yield return StartCoroutine(WaitArrival());

                    // 4. �������
                    agent.isStopped = true;
                    // ������ ����������� � ������� ������
                    if (storageDropPoint != null) transform.rotation = storageDropPoint.rotation;

                    if (anim != null) anim.SetTrigger("Pickup");
                    yield return new WaitForSeconds(1.0f);

                    if (carryVisual != null) carryVisual.SetActive(false);

                    if (building.productionType == ResourceType.Wood)
                        ResourceManager.Instance.AddStashResources(amount, 0, 0);
                    else if (building.productionType == ResourceType.Food)
                        ResourceManager.Instance.AddStashResources(0, 0, amount);
                    else if (building.productionType == ResourceType.Stone)
                        ResourceManager.Instance.AddStashResources(0, amount, 0);

                    yield return new WaitForSeconds(1.5f);
                }
            }

            if (!collectedAnything)
            {
                yield return StartCoroutine(WanderAroundStorage());
            }
            else
            {
                yield return new WaitForSeconds(2f);
            }
        }
    }

    private IEnumerator WaitArrival()
    {
        // ���� ����� Unity 1 ����, ��� �� 100% ����� ������ ���������� �����
        yield return null;

        float timeout = 0f;
        while (timeout < 20f)
        {
            timeout += Time.deltaTime;
            if (agent != null && agent.isOnNavMesh && !agent.pathPending)
            {
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid) break;
                if (agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
            }
            yield return null;
        }
    }
}