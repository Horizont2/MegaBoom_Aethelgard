using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RegionTotem : MonoBehaviour
{
    [Header("Region Link")]
    public RegionData currentRegion;

    [Header("Visuals")]
    public ParticleSystem idleCorruptionVFX;
    public ParticleSystem purifyExplosionVFX;
    public Light totemLight;

    [Header("Interaction")]
    public float interactionRadius = 3f;
    private bool isActivated = false;
    private bool isPurified = false;

    private List<GameObject> activeBosses = new List<GameObject>();
    private Transform player;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").transform;
        if (totemLight != null) totemLight.color = Color.red; // Колір порчі
    }

    private void Update()
    {
        if (isPurified || isActivated || player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= interactionRadius)
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[F] Purify Totem");

            if (Input.GetKeyDown(KeyCode.F))
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                StartCoroutine(ActivationEventRoutine());
            }
        }
        else
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    private IEnumerator ActivationEventRoutine()
    {
        isActivated = true;

        // 1. Епічна тряска та звук
        if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(1.5f, 0.4f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Enemy_Telegraph); // Звук тривоги

        if (GlobalHUD.Instance != null)
            GlobalHUD.Instance.SetLevelObjective("SLAY THE REGION BOSSES!");

        yield return new WaitForSeconds(2f);

        // 2. Розрахунок Динамічної Складності (Power System)
        int playerPower = PowerSystemManager.Instance.CalculatePlayerPower();
        int powerDelta = playerPower - currentRegion.recommendedPower;

        float difficultyMultiplier = 1f;
        if (powerDelta < 0)
        {
            // Гравець слабший: боси стають ЖОРСТКІШИМИ (від 1x до 3x)
            difficultyMultiplier = Mathf.Clamp(1f + (Mathf.Abs(powerDelta) * 0.02f), 1f, 3.0f);
        }
        else
        {
            // Гравець сильніший: легкий нерф босів, але не менше ніж 0.7x
            difficultyMultiplier = Mathf.Clamp(1f - (powerDelta * 0.005f), 0.7f, 1f);
        }

        // Враховуємо базові множники регіону
        float finalHpMult = currentRegion.enemyHpMultiplier * difficultyMultiplier;
        float finalDmgMult = currentRegion.enemyDamageMultiplier * difficultyMultiplier;

        // 3. Спавн босів навколо Тотема
        for (int i = 0; i < currentRegion.regionBossPrefabs.Length; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle.normalized * 6f;
            Vector3 spawnPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);
            spawnPos.y = GetGroundHeight(spawnPos);

            GameObject bossPrefab = currentRegion.regionBossPrefabs[i];
            GameObject bossObj = Instantiate(bossPrefab, spawnPos, Quaternion.identity);

            // Застосовуємо розраховану складність (Якщо у боса є скрипт TutorialBossAI або EnemyAI)
            TutorialBossAI bossAI = bossObj.GetComponent<TutorialBossAI>();
            if (bossAI != null)
            {
                bossAI.maxHealth *= finalHpMult;
                bossAI.damage *= finalDmgMult;
            }

            activeBosses.Add(bossObj);

            // Ефект спавну
            if (Camera.main != null) Camera.main.GetComponent<CameraFollow>().TriggerShake(0.5f, 0.5f);
            yield return new WaitForSeconds(0.5f); // Затримка між появою босів
        }

        // 4. Чекаємо, поки всі боси помруть
        StartCoroutine(MonitorBossesRoutine());
    }

    private IEnumerator MonitorBossesRoutine()
    {
        while (true)
        {
            // Перевіряємо, чи залишились живі боси
            activeBosses.RemoveAll(item => item == null); // Видаляє знищених босів зі списку

            if (activeBosses.Count == 0)
            {
                PurifyRegion();
                break;
            }
            yield return new WaitForSeconds(1f);
        }
    }

    private void PurifyRegion()
    {
        isPurified = true;

        if (GlobalHUD.Instance != null)
        {
            GlobalHUD.Instance.HideLevelObjective();
            GlobalHUD.Instance.ShowPrompt("REGION PURIFIED!");
        }

        // Ефекти очищення
        if (idleCorruptionVFX != null) idleCorruptionVFX.Stop();
        if (purifyExplosionVFX != null) purifyExplosionVFX.Play();
        if (totemLight != null) totemLight.color = new Color(0f, 0.8f, 1f); // Стає синім (Чистим)

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

        // Змінюємо статус регіону на екрані мапи
        currentRegion.currentState = RegionState.Conquered;

        // Видаємо нагороду гравцю
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddStashResources(currentRegion.woodReward, currentRegion.stoneReward, currentRegion.foodReward);
            ResourceManager.Instance.diamonds += currentRegion.diamondReward;
            ResourceManager.Instance.UpdateUI();
        }
    }

    private float GetGroundHeight(Vector3 pos)
    {
        if (Terrain.activeTerrain != null)
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        return pos.y;
    }
}