using UnityEngine;

// The cart is both the way out AND the bank.
//
// It used to be only the way out, and that quietly punished the whole
// exploration half of the game: everything in the backpack was lost on death, so
// the moment a chest paid out, the correct play was to stop playing and walk
// home. Adding a stow action makes the cart a save point you can visit as often
// as you like — the walk back is still the risk, but it is a risk you choose the
// size of, instead of the run being all-or-nothing.
public class ExtractionPoint : MonoBehaviour
{
    [Tooltip("Bank the backpack into the camp stash without ending the run.")]
    // NOT Q — that raises the shield now, and a player defending themselves
    // beside the cart should not be banking their backpack every time they
    // block. F is free in a region; it only means "inspect" back in camp.
    public KeyCode stowKey = KeyCode.F;

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

        if (Input.GetKeyDown(stowKey) && ResourceManager.Instance != null)
        {
            if (ResourceManager.Instance.StowRunToStash(out int w, out int s, out int f))
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Purchase);
                if (GlobalHUD.Instance != null)
                    GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_STOWED", w + s + f));
            }
            else if (GlobalHUD.Instance != null)
            {
                // Says WHY nothing happened. Silence here reads as a broken key.
                GlobalHUD.Instance.ShowPrompt(LocalizationManager.Tr("PROMPT_STOW_NOTHING"));
            }
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
        bool carrying = ResourceManager.Instance != null &&
                        (ResourceManager.Instance.runWood + ResourceManager.Instance.runStone
                         + ResourceManager.Instance.runFood) > 0;
        GlobalHUD.Instance.ShowPrompt(carrying
            ? LocalizationManager.Tr("PROMPT_EVACUATE_OR_STOW", stowKey.ToString())
            : LocalizationManager.Tr("PROMPT_EVACUATE"));
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
