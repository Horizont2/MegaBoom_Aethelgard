using UnityEngine;

public class ShopTeleporter : MonoBehaviour
{
    public string shopSceneName = "ShopScene"; // ����� ����� ��������
    private bool isPlayerNear = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_ENTER_SHOP"));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
            if (GlobalHUD.Instance != null)
                GlobalHUD.Instance.HidePrompt();
        }
    }

    private void Update()
    {
        if (isPlayerNear && Input.GetKeyDown(KeyCode.E))
        {
            isPlayerNear = false; // ������� �������� ����������
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HidePrompt(); // ������ �����
                GlobalHUD.Instance.FadeAndLoadScene(shopSceneName); // ���������� ����� � ����������
            }
        }
    }
}