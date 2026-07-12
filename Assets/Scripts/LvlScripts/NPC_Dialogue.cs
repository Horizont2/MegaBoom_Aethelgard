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

            // ��������� ������ �� ������
            Transform player = GameObject.FindGameObjectWithTag("Player").transform;
            Vector3 lookPos = player.position - transform.position;
            lookPos.y = 0;
            transform.rotation = Quaternion.LookRotation(lookPos);

            // ���������� Ĳ����
            if (Level1_QuestManager.Instance != null)
            {
                Level1_QuestManager.Instance.StartIntroDialogue();
            }
        }
    }
}