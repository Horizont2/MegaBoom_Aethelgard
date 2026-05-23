using UnityEngine;
using TMPro;
using System.Collections;

public class CampNPC_Elias : MonoBehaviour
{
    [Header("UI & Objects")]
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.04f;

    [Header("Features")]
    public GameObject exclamationMark;
    public GameObject mapTableObject;

    [Header("Dialogue Settings")]
    public float dialogueBreakDistance = 7f;

    private Transform playerTransform;

    private bool isPlayerInRange = false;
    private bool isTalking = false;
    private float stateCheckTimer = 0f;

    private string[] idlePhrases = new string[]
    {
        "Elias: The Blight never sleeps. Neither should we.",
        "Elias: Keep your blade sharp. The outlands are unforgiving.",
        "Elias: I smell ash on the wind today...",
        "Elias: If you find any ancient scrolls out there, bring them to me.",
        "Elias: Aethelgard will rise again. I feel it."
    };

    private void Start()
    {
        UpdateNPCState();

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null) playerTransform = pObj.transform;
    }

    private void Update()
    {
        if (!isTalking)
        {
            stateCheckTimer += Time.deltaTime;
            if (stateCheckTimer >= 1f)
            {
                stateCheckTimer = 0f;
                UpdateNPCState();
            }
        }
        else
        {
            if (playerTransform != null && Vector3.Distance(transform.position, playerTransform.position) > dialogueBreakDistance)
            {
                InterruptDialogue();
            }
        }

        if (isPlayerInRange && !isTalking && Input.GetKeyDown(KeyCode.E))
        {
            StartCoroutine(EliasDialogueRoutine());
        }
    }

    private void UpdateNPCState()
    {
        // ПОВЕРНЕНО: Стіл вмикається СТРОГО від рівня будівлі, діалог на нього не впливає
        int lodgeLevel = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);
        if (mapTableObject != null)
        {
            mapTableObject.SetActive(lodgeLevel >= 2);
        }

        if (exclamationMark != null)
        {
            exclamationMark.SetActive(HasPendingDialogue());
        }
    }

    private bool HasPendingDialogue()
    {
        int lodgeLvl = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);
        int conqueredCount = PlayerPrefs.GetInt("TotalConqueredRegions", 0);

        if (lodgeLvl == 1 && PlayerPrefs.GetInt("Elias_Intro", 0) == 0) return true;
        if (lodgeLvl == 2 && PlayerPrefs.GetInt("Elias_TableBuilt", 0) == 0) return true;
        if (conqueredCount >= 1 && PlayerPrefs.GetInt("Elias_Lore1", 0) == 0) return true;
        if (conqueredCount >= 4 && PlayerPrefs.GetInt("Elias_Lore2", 0) == 0) return true;
        if (lodgeLvl == 3 && PlayerPrefs.GetInt("Elias_DesertBuilt", 0) == 0) return true;
        if (conqueredCount >= 10 && PlayerPrefs.GetInt("Elias_Lore3", 0) == 0) return true;
        if (lodgeLvl == 4 && PlayerPrefs.GetInt("Elias_WinterBuilt", 0) == 0) return true;
        if (conqueredCount >= 20 && PlayerPrefs.GetInt("Elias_Lore4", 0) == 0) return true;

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !isTalking)
        {
            isPlayerInRange = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Talk to Elias");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }

    private IEnumerator EliasDialogueRoutine()
    {
        isTalking = true;
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        if (exclamationMark != null) exclamationMark.SetActive(false);

        Transform player = GameObject.FindGameObjectWithTag("Player").transform;
        Vector3 lookPos = player.position - transform.position;
        lookPos.y = 0;
        transform.rotation = Quaternion.LookRotation(lookPos);

        int lodgeLvl = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);
        int conqueredCount = PlayerPrefs.GetInt("TotalConqueredRegions", 0);

        if (lodgeLvl == 1 && PlayerPrefs.GetInt("Elias_Intro", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: Listen closely. This camp won't survive on scraps forever.", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: The skeletons you fought? They are the cursed remains of Aethelgard's royal guard.", 4.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: Centuries ago, the Ashen Blight ruined this kingdom. We must reclaim the 24 lost provinces.", 5f));
            yield return StartCoroutine(ShowSubtitle("Elias: Build me a drafting table here later, and I will chart a safe path to the forests.", 4.5f));

            // Тільки кажемо грі, що інтро пройдено
            PlayerPrefs.SetInt("Elias_Intro", 1);
        }
        else if (lodgeLvl >= 2 && PlayerPrefs.GetInt("Elias_TableBuilt", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: The new table is perfect. I've charted the first 8 regions on the map behind me.", 4f));
            yield return StartCoroutine(ShowSubtitle("Elias: Interact with the table to plan your assaults. We need those territories back.", 4.5f));
            PlayerPrefs.SetInt("Elias_TableBuilt", 1);
        }
        else if (conqueredCount >= 1 && PlayerPrefs.GetInt("Elias_Lore1", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: You survived your first conquest. I knew you had the spark.", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: Did you notice the black ash falling in the woods? That is the physical form of the Blight.", 4.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: It corrupts the land and the minds of those who fall in battle. Stay vigilant.", 4.5f));
            PlayerPrefs.SetInt("Elias_Lore1", 1);
        }
        else if (conqueredCount >= 4 && PlayerPrefs.GetInt("Elias_Lore2", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: You fight like a demon. It reminds me of the old days...", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: I wasn't always a ragged scout. I was the Chief Cartographer of Aethelgard.", 4f));
            yield return StartCoroutine(ShowSubtitle("Elias: I drew the very borders you now bleed to reclaim. It breaks my heart to see them ruined.", 4.5f));
            PlayerPrefs.SetInt("Elias_Lore2", 1);
        }
        else if (lodgeLvl >= 3 && PlayerPrefs.GetInt("Elias_DesertBuilt", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: The alchemical lab is complete. The reagents cleared the faded ink on the parchments.", 4.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: The Southern Wastes are now open to you. But beware, the heat is the least of your worries there.", 4.5f));
            PlayerPrefs.SetInt("Elias_DesertBuilt", 1);
        }
        else if (conqueredCount >= 10 && PlayerPrefs.GetInt("Elias_Lore3", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: We are pushing them back. The Blight recedes where you walk.", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: But the deeper you go into the Wastes, the older the magic gets. Do not underestimate them.", 4.5f));
            PlayerPrefs.SetInt("Elias_Lore3", 1);
        }
        else if (lodgeLvl >= 4 && PlayerPrefs.GetInt("Elias_WinterBuilt", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: The astrolabe is calibrated. I can finally chart a path through the magical blizzards.", 4.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: The Northern Peaks are unlocked. The entire map of Aethelgard is restored.", 4f));
            PlayerPrefs.SetInt("Elias_WinterBuilt", 1);
        }
        else if (conqueredCount >= 20 && PlayerPrefs.GetInt("Elias_Lore4", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: You are so close. Only the harshest lands remain.", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: The King's personal guard fell in those mountains. They are ruthless. Prepare yourself.", 4.5f));
            PlayerPrefs.SetInt("Elias_Lore4", 1);
        }
        else
        {
            string randomPhrase = idlePhrases[Random.Range(0, idlePhrases.Length)];
            yield return StartCoroutine(ShowSubtitle(randomPhrase, 3f));
        }

        PlayerPrefs.Save();
        isTalking = false;
        UpdateNPCState();

        // Оновлюємо маркер дошки місій напряму, якщо вона зараз активна на сцені
        MissionBoardMarker board = FindFirstObjectByType<MissionBoardMarker>();
        if (board != null) board.UpdateMarkerState();

        if (isPlayerInRange && GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt("[E] Talk to Elias");
    }

    private IEnumerator ShowSubtitle(string text, float stayDuration)
    {
        if (subtitleText == null) yield break;

        subtitleText.text = text;
        subtitleText.ForceMeshUpdate();
        int totalChars = subtitleText.textInfo.characterCount;
        subtitleText.maxVisibleCharacters = 0;

        for (int i = 0; i <= totalChars; i++)
        {
            subtitleText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typingSpeed);
        }

        yield return new WaitForSeconds(stayDuration);
        subtitleText.maxVisibleCharacters = 99999;
        subtitleText.text = "";
    }

    private void InterruptDialogue()
    {
        StopAllCoroutines();

        if (subtitleText != null)
        {
            subtitleText.text = "";
            subtitleText.maxVisibleCharacters = 99999;
        }

        isTalking = false;
        UpdateNPCState();
    }
}