using UnityEngine;
using System.Collections;

public class LootChest : MonoBehaviour
{
    [Header("References")]
    public Animator chestAnimator;

    [Header("Interaction Settings")]
    public float interactRange = 3f;
    public KeyCode interactKey = KeyCode.E;

    [Header("Shake Settings")]
    public float shakeDuration = 0.6f;
    public float shakeAmount = 0.15f;

    [Header("Loot Settings")]
    public GameObject[] possibleLoot;
    public int minLootItems = 3;
    public int maxLootItems = 6;

    public float delayForLoot = 1.5f;

    [Header("Destruction")]
    public float destroyDelay = 10f;

    private bool isInteracted = false;
    private bool isPromptShowing = false;
    private Transform player;
    private Vector3 originalPos;

    private void Start()
    {
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) player = pObj.transform;

        originalPos = transform.position;

        if (chestAnimator == null) chestAnimator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (isInteracted || player == null) return;

        // sqrMagnitude — sqrt was firing every frame per chest.
        float rangeSqr = interactRange * interactRange;
        bool inRange = (transform.position - player.position).sqrMagnitude <= rangeSqr;

        if (inRange)
        {
            // Discoverability: chests had no floating prompt, so players
            // walked past them. Show the standard [E] prompt while in range.
            if (!isPromptShowing && GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_OPEN_CHEST"));
                isPromptShowing = true;
            }
            if (Input.GetKeyDown(interactKey))
            {
                if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
                isPromptShowing = false;
                StartCoroutine(OpenSequence());
            }
        }
        else if (isPromptShowing)
        {
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
            isPromptShowing = false;
        }
    }

    private IEnumerator OpenSequence()
    {
        isInteracted = true;

        // Правильний звук відкриття скрині замість перевикористаного
        // звуку рубання дерева, який тут стояв раніше.
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(AudioID.Env_ChestOpen);

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            transform.position = originalPos + Random.insideUnitSphere * shakeAmount;
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.position = originalPos;

        if (chestAnimator != null)
        {
            chestAnimator.SetTrigger("Open");
        }

        yield return new WaitForSeconds(delayForLoot);

        SpawnLoot();

        Destroy(gameObject, destroyDelay);
    }

    private void SpawnLoot()
    {
        int count = Random.Range(minLootItems, maxLootItems + 1);
        for (int i = 0; i < count; i++)
        {
            if (possibleLoot.Length > 0)
            {
                GameObject loot = possibleLoot[Random.Range(0, possibleLoot.Length)];
                Instantiate(loot, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}