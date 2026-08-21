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
    // Ownership token for the WHOLE conversation on the shared subtitle label
    // (CampDirector's intro drives the same TMP). Claimed once per conversation;
    // if another conversation claims a newer one, this whole sequence goes quiet.
    private int _subtitleSeq;
    private float stateCheckTimer = 0f;
    // Optional companion — if present, we pause its patrol during dialogue.
    private NPCPatroller patroller;

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

        patroller = GetComponent<NPCPatroller>();
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
            if (patroller != null) patroller.HoldPosition = true;
            StartCoroutine(EliasDialogueRoutine());
        }
        // Keep patroller frozen while dialogue is active so Elias doesn't
        // walk off mid-sentence.
        if (patroller != null) patroller.HoldPosition = isTalking;
    }

    private void UpdateNPCState()
    {
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
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_TALK_ELIAS"));
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
        _subtitleSeq = SubtitleGuard.Claim(); // own the shared label for this whole talk
        if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        if (exclamationMark != null) exclamationMark.SetActive(false);

        // Բ�� ����̲��ֲ�: ������������� ��� ���������� playerTransform
        if (playerTransform != null)
        {
            Vector3 lookPos = playerTransform.position - transform.position;
            lookPos.y = 0;
            transform.rotation = Quaternion.LookRotation(lookPos);
        }

        int lodgeLvl = PlayerPrefs.GetInt("SaveBld_ScoutsLodge", 1);
        int conqueredCount = PlayerPrefs.GetInt("TotalConqueredRegions", 0);

        if (lodgeLvl == 1 && PlayerPrefs.GetInt("Elias_Intro", 0) == 0)
        {
            yield return StartCoroutine(ShowSubtitle("Elias: Listen closely. This camp won't survive on scraps forever.", 3.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: The skeletons you fought? They are the cursed remains of Aethelgard's royal guard.", 4.5f));
            yield return StartCoroutine(ShowSubtitle("Elias: Centuries ago, the Ashen Blight ruined this kingdom. We must reclaim the 24 lost provinces.", 5f));
            yield return StartCoroutine(ShowSubtitle("Elias: Build me a drafting table here later, and I will chart a safe path to the forests.", 4.5f));
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
        // Guarantee the label is clear when the talk ends (belt-and-suspenders
        // against a line left on screen).
        if (subtitleText != null && SubtitleGuard.Owns(_subtitleSeq))
        {
            subtitleText.text = "";
            subtitleText.ForceMeshUpdate();
        }
        isTalking = false;
        UpdateNPCState();

        MissionBoardMarker board = FindFirstObjectByType<MissionBoardMarker>();
        if (board != null) board.UpdateMarkerState();

        if (isPlayerInRange && GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_TALK_ELIAS"));
    }

    private IEnumerator ShowSubtitle(string text, float stayDuration)
    {
        if (subtitleText == null) yield break;

        // Wrap through Tr so the English literals passed as call-site keys
        // can be translated by the LocalizationManager. Untranslated keys
        // fall back to the passed English verbatim.
        string full = LocalizationManager.Tr(text);
        subtitleText.richText = true;
        subtitleText.maxVisibleCharacters = 99999;
        subtitleText.text = ""; subtitleText.ForceMeshUpdate(); // no residue from a prior line
        // Swallow input for a moment so the SAME keypress that opened the
        // dialogue (or advanced the previous line) can't instantly skip this one.
        float lineStart = Time.unscaledTime;

        // Typewriter via an <alpha=#00> tag: the full line is laid out from the
        // start (reveal "in place", no layout jumps) with the untyped tail
        // transparent. Plain `.text =` + ForceMeshUpdate renders reliably here,
        // unlike maxVisibleCharacters which left later lines un-rendered on this
        // TMP version (dialogues froze on the first line).
        // Skippable — Space/E/Enter jumps to the fully typed line.
        bool skippedTypewriter = false;
        for (int i = 0; i <= full.Length; i++)
        {
            if (!SubtitleGuard.Owns(_subtitleSeq)) yield break;
            if (!skippedTypewriter && Time.unscaledTime - lineStart > 0.25f && WasSkipPressed())
            {
                skippedTypewriter = true;
                i = full.Length; // jump to fully visible
            }
            subtitleText.text = full.Substring(0, i) + "<alpha=#00>" + full.Substring(i);
            subtitleText.ForceMeshUpdate();
            yield return new WaitForSeconds(typingSpeed);
        }
        if (!SubtitleGuard.Owns(_subtitleSeq)) yield break;
        subtitleText.text = full;
        subtitleText.ForceMeshUpdate();

        // Skippable read-hold — same trigger cuts the wait short.
        float t = 0f;
        float holdStart = Time.unscaledTime;
        while (t < stayDuration)
        {
            if (Time.unscaledTime - holdStart > 0.25f && WasSkipPressed()) break;
            if (!SubtitleGuard.Owns(_subtitleSeq)) yield break;
            t += Time.deltaTime;
            yield return null;
        }
        if (!SubtitleGuard.Owns(_subtitleSeq)) yield break;
        subtitleText.text = "";
        subtitleText.ForceMeshUpdate();
    }

    private static bool WasSkipPressed()
    {
        // NOTE: E is deliberately NOT a skip key — it's the interact key that
        // opens/advances the talk, and using it to skip made pressing E to start
        // a conversation instantly blast through the first line.
        return Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter);
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