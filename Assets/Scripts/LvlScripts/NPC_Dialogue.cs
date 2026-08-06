using UnityEngine;

public class NPC_Dialogue : MonoBehaviour
{
    [Header("Quest UI")]
    public GameObject questMarker; // ��������� �� ������� ���� ������

    private bool isPlayerInRange = false;
    private bool hasTalked = false;

    // Used by MinimapQuestTracker to decide whether this NPC should show up as
    // a quest target on the minimap.
    public bool HasActiveQuestMarker => !hasTalked && questMarker != null && questMarker.activeSelf;

    private void Start()
    {
        // ������������, �� ���� ������ ��������� �� ������� �����
        if (questMarker != null) questMarker.SetActive(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !hasTalked)
        {
            isPlayerInRange = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_TALK_STRANGER"));
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

    private void Update()
    {
        if (isPlayerInRange && !hasTalked && Input.GetKeyDown(KeyCode.E))
        {
            hasTalked = true;
            isPlayerInRange = false;

            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();

            // ��������� ���� ������, �� ����� ���������
            if (questMarker != null) questMarker.SetActive(false);

            // ��������� ������ �� ������ — null-guard so a destroyed /
            // absent player entity doesn't NRE and abort the dialogue.
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                Vector3 lookPos = playerObj.transform.position - transform.position;
                lookPos.y = 0;
                if (lookPos.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(lookPos);
            }

            // ���������� Ĳ����
            if (Level1_QuestManager.Instance != null)
            {
                Level1_QuestManager.Instance.StartIntroDialogue();
            }
        }
    }
}