using UnityEngine;

// Sits on the character's model root (child of the Player or a merc unit)
// and forwards Animator events onto the parent controller. When the parent
// is not a PlayerController (e.g. a mercenary unit spawned by the barracks)
// the receiver gracefully no-ops instead of throwing — Unity fires
// "AnimationEvent 'X' has no receiver" once per event otherwise, and the
// walk clip's per-footstep event was spamming the console.
public class AnimationEventReceiver : MonoBehaviour
{
    private PlayerController player;
    private bool ownerResolved = false;

    private void Start()
    {
        ResolveOwner();
    }

    private void ResolveOwner()
    {
        player = GetComponentInParent<PlayerController>();
        ownerResolved = true;
    }

    public void ExecuteAttack()
    {
        if (!ownerResolved) ResolveOwner();
        if (player != null) player.ExecuteAttack();
    }

    public void ExecuteThrow()
    {
        if (!ownerResolved) ResolveOwner();
        if (player != null) player.ExecuteThrow();
    }

    // Walk / run clips fire this per-footstep. For the player we forward to
    // the real PlayerController handler (particles + audio). For merc NPCs
    // we still want the 3D footstep sound — no dust particles because the
    // NPCs don't have run-dust prefabs wired.
    public void TriggerFootstepDust()
    {
        if (!ownerResolved) ResolveOwner();
        if (player != null)
        {
            player.TriggerFootstepDust();
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX3D(AudioID.Player_Footstep, transform.position);
        }
    }
}
