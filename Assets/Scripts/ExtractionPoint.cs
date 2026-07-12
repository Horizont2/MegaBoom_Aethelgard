using UnityEngine;

public class ExtractionPoint : MonoBehaviour
{
    private bool isPlayerNear = false;

    void Update()
    {
        if (isPlayerNear && Input.GetKeyDown(KeyCode.E))
        {
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HidePrompt();

                // ����: ������ ��������� (�������)
                if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

                PlayerController pc = FindFirstObjectByType<PlayerController>();
                if (pc != null) SaveManager.AddCrystals(pc.crystalsCollected);

                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.EvacuateRunToStash();
                }

                GlobalHUD.Instance.FadeAndLoadScene("CampScene");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_EVACUATE"));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
            if (GlobalHUD.Instance != null) GlobalHUD.Instance.HidePrompt();
        }
    }
}