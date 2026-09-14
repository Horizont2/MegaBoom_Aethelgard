using UnityEngine;

// The cart is the way out. Only the way out.
//
// It briefly doubled as a bank: a stow key that pushed the backpack into the
// camp stash without ending the run. Removed on request, and the design reason
// is sound — a save point you can visit as often as you like takes the whole
// decision out of a run. Carrying a full backpack is supposed to be a growing
// bet on getting home with it, and being able to cash out at the exit whenever
// you pass it means there is never anything at stake.
//
// Everything is banked on evacuation, which is the one moment the risk actually
// resolves.
public class ExtractionPoint : MonoBehaviour
{
    private bool isPlayerNear = false;

    void Update()
    {
        if (!isPlayerNear) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (GlobalHUD.Instance != null)
            {
                GlobalHUD.Instance.HidePrompt();

                // Звук: успішна евакуація
                if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_QuestComplete);

                PlayerController pc = FindFirstObjectByType<PlayerController>();
                if (pc != null) SaveManager.AddCrystals(pc.crystalsCollected);

                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.EvacuateRunToStash();
                }

                GlobalHUD.Instance.FadeAndLoadScene("CampScene");
            }
            return;
        }

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
            ShowPrompt();
        }
    }

    // Re-shown on a timer so the confirmation text above reverts to the standing
    // instructions instead of sticking until the player walks away.
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _promptTimer -= Time.deltaTime;
        if (_promptTimer <= 0f) ShowPrompt();
    }

    private float _promptTimer;

    private void ShowPrompt()
    {
        _promptTimer = 1.5f;
        if (GlobalHUD.Instance == null) return;
        GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_EVACUATE"));
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
