using UnityEngine;

// Keeps a gameplay animator in its resting state during the trailer.
//
// HeroAnimator's Locomotion is gated on IsGrounded, Speed, MoveX and MoveZ, all
// of which PlayerController normally drives. The trailer's rider has no
// PlayerController, so after his controller is swapped back nothing ever sets
// them: the controller sits in its default 'Empty' state, which has no motion,
// and the rig shows its bind pose — the T-pose.
//
// Added by TrailerRideEvent right after the hand-back.
public class TrailerAnimatorHold : MonoBehaviour
{
    public Animator animator;
    [Tooltip("Bool parameters forced true — the grounded flag nothing else is setting.")]
    public string[] trueBools = { "IsGrounded" };
    [Tooltip("Float parameters forced to zero, so the locomotion blend rests at idle.")]
    public string[] zeroFloats = { "Speed", "MoveX", "MoveZ" };

    private void Reset() { animator = GetComponentInChildren<Animator>(); }

    private void LateUpdate()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool)
            {
                foreach (var n in trueBools) if (p.name == n) { animator.SetBool(p.name, true); break; }
            }
            else if (p.type == AnimatorControllerParameterType.Float)
            {
                foreach (var n in zeroFloats) if (p.name == n) { animator.SetFloat(p.name, 0f); break; }
            }
        }
    }
}
