using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyEncounterGroup : MonoBehaviour
{
    public enum EncounterStyle { Patrol, Camp }

    [Header("Style")]
    public EncounterStyle style = EncounterStyle.Patrol;

    [Header("Enemy Setup")]
    public GameObject[] enemyPrefabs;
    [Range(1, 8)] public int enemyCount = 3;
    [Tooltip("Радіус, у якому вороги розставляються відносно центру групи на спавні")]
    public float spawnSpread = 2.5f;

    [Header("Passive Behavior")]
    [Tooltip("Радіус, у якому вороги блукають навколо центру групи")]
    public float roamRadius = 3.5f;
    [Tooltip("Дистанція, на якій група агриться на гравця")]
    public float aggroRange = 14f;

    [Header("Patrol (Style=Patrol)")]
    [Tooltip("Якщо передано — група циклічно ходить по цих точках. Інакше WorldEncounterDirector сам генерує waypoints")]
    public Transform[] patrolPoints;
    [Tooltip("Скільки секунд група стоїть на кожному waypoint перед переходом")]
    public float waypointPauseDuration = 6f;
    [Tooltip("Швидкість руху групи між waypoints")]
    public float patrolMoveSpeed = 1.6f;

    [Header("Camp (Style=Camp)")]
    [Tooltip("Префаб вогнища у центрі табору. Якщо null — нічого не спавнить")]
    public GameObject campfirePrefab;

    [Header("Lifecycle")]
    [Tooltip("Якщо true — група сама спавнить ворогів на Start(). Інакше викличте SpawnGroup() ззовні")]
    public bool autoStart = true;

    private readonly List<EnemyAI> spawnedEnemies = new List<EnemyAI>(8);
    private int currentPatrolIndex = 0;
    private bool spawned = false;
    private GameObject spawnedCampfire;

    public IReadOnlyList<EnemyAI> SpawnedEnemies => spawnedEnemies;
    public bool AllAggroed
    {
        get
        {
            for (int i = 0; i < spawnedEnemies.Count; i++)
            {
                EnemyAI e = spawnedEnemies[i];
                if (e != null && !e.IsAggroed) return false;
            }
            return true;
        }
    }

    private void Start()
    {
        if (autoStart) SpawnGroup();
    }

    public void SpawnGroup()
    {
        if (spawned) return;
        spawned = true;

        if (style == EncounterStyle.Camp && campfirePrefab != null)
        {
            spawnedCampfire = Instantiate(campfirePrefab, transform.position, Quaternion.identity, transform);
        }

        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        for (int i = 0; i < enemyCount; i++)
        {
            GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
            if (prefab == null) continue;

            Vector2 off = Random.insideUnitCircle * spawnSpread;
            Vector3 pos = transform.position + new Vector3(off.x, 0f, off.y);
            if (Terrain.activeTerrain != null)
                pos.y = Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;

            // Face campfire (camp) or random direction (patrol)
            Quaternion rot;
            if (style == EncounterStyle.Camp)
            {
                Vector3 toCenter = transform.position - pos;
                toCenter.y = 0f;
                rot = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter) : Quaternion.identity;
            }
            else
            {
                rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }

            GameObject obj = Instantiate(prefab, pos, rot);
            EnemyAI ai = obj.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.startPassive = true;
                ai.parentGroup = this;
                ai.anchorPoint = transform.position;
                ai.roamRadius = roamRadius;
                ai.aggroRange = aggroRange;
                spawnedEnemies.Add(ai);
            }
        }

        if (style == EncounterStyle.Patrol && patrolPoints != null && patrolPoints.Length > 1)
        {
            StartCoroutine(PatrolRoutine());
        }
    }

    private IEnumerator PatrolRoutine()
    {
        // Wait a moment so spawned enemies finish their rise-from-ground animation
        yield return new WaitForSeconds(2f);

        while (true)
        {
            // Stop moving the group as soon as anyone aggroed — the chase logic takes over
            if (AnyAggroed())
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            Transform wp = patrolPoints[currentPatrolIndex];
            if (wp == null)
            {
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                yield return null;
                continue;
            }

            while (true)
            {
                if (AnyAggroed()) yield break;

                Vector3 toWP = wp.position - transform.position;
                toWP.y = 0f;
                float distXZ = toWP.magnitude;
                if (distXZ < 1.2f) break;

                Vector3 step = toWP / distXZ * (patrolMoveSpeed * Time.deltaTime);
                Vector3 nextPos = transform.position + step;
                if (Terrain.activeTerrain != null)
                    nextPos.y = Terrain.activeTerrain.SampleHeight(nextPos) + Terrain.activeTerrain.transform.position.y;
                transform.position = nextPos;

                yield return null;
            }

            yield return new WaitForSeconds(waypointPauseDuration);
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        }
    }

    private bool AnyAggroed()
    {
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            EnemyAI e = spawnedEnemies[i];
            if (e != null && e.IsAggroed) return true;
        }
        return false;
    }

    public void AlertAll()
    {
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            EnemyAI e = spawnedEnemies[i];
            if (e != null) e.Aggro();
        }
    }
}
