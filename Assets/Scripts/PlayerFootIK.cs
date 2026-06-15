using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerFootIK : MonoBehaviour
{
    [Header("IK Settings")]
    public bool enableFeetIK = true;
    public float raycastUpOffset = 1.0f;
    public float raycastDownDistance = 1.5f;

    [Tooltip("Вибери тут ТІЛЬКИ шари землі. Не вибирай Player!")]
    public LayerMask environmentLayer;

    [Header("Foot Offsets")]
    public float footYOffset = 0.12f;

    [Header("IK Limits (ААА Запобіжники)")]
    [Tooltip("Максимальна висота об'єкта, на який можна поставити ногу")]
    public float maxStepHeight = 0.45f;
    [Tooltip("Максимальний нахил поверхні (щоб не ставити ноги на рівні стіни)")]
    public float maxSlopeAngle = 45f;

    [Header("Pelvis Settings (ААА Присідання)")]
    public bool adjustPelvis = true;
    public float pelvisSpeed = 10f;
    public float maxPelvisDrop = 0.6f;

    [Header("Smoothing")]
    [Range(0f, 1f)] public float maxIkWeight = 1f;
    public float smoothSpeed = 15f;

    private Animator animator;
    private float leftFootIKWeight = 0f;
    private float rightFootIKWeight = 0f;
    private float lastPelvisPositionY = 0f;

    private void Start()
    {
        animator = GetComponent<Animator>();

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer != -1) environmentLayer &= ~(1 << playerLayer);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || !enableFeetIK) return;

        bool isGrounded = animator.GetBool("IsGrounded");
        float currentSpeed = animator.GetFloat("Speed");

        float targetWeight = 0f;
        if (isGrounded)
        {
            float speedFade = Mathf.Clamp01(1f - (currentSpeed / 1.5f));
            targetWeight = maxIkWeight * speedFade;
        }

        leftFootIKWeight = Mathf.Lerp(leftFootIKWeight, targetWeight, Time.deltaTime * smoothSpeed);
        rightFootIKWeight = Mathf.Lerp(rightFootIKWeight, targetWeight, Time.deltaTime * smoothSpeed);

        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, leftFootIKWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, leftFootIKWeight);
        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightFootIKWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, rightFootIKWeight);

        if (leftFootIKWeight > 0.01f || rightFootIKWeight > 0.01f)
        {
            if (adjustPelvis) AdjustPelvisHeight();

            HandleFootIK(AvatarIKGoal.LeftFoot, leftFootIKWeight);
            HandleFootIK(AvatarIKGoal.RightFoot, rightFootIKWeight);
        }
        else
        {
            lastPelvisPositionY = Mathf.Lerp(lastPelvisPositionY, 0f, Time.deltaTime * pelvisSpeed);
            Vector3 bodyPos = animator.bodyPosition;
            bodyPos.y += lastPelvisPositionY;
            animator.bodyPosition = bodyPos;
        }
    }

    private void AdjustPelvisHeight()
    {
        float lOffset = GetFootOffset(AvatarIKGoal.LeftFoot);
        float rOffset = GetFootOffset(AvatarIKGoal.RightFoot);

        float totalOffset = Mathf.Min(lOffset, rOffset);
        totalOffset = Mathf.Clamp(totalOffset, -maxPelvisDrop, 0f);

        float currentMaxWeight = Mathf.Max(leftFootIKWeight, rightFootIKWeight);
        float targetPelvisPos = totalOffset * currentMaxWeight;

        lastPelvisPositionY = Mathf.Lerp(lastPelvisPositionY, targetPelvisPos, Time.deltaTime * pelvisSpeed);

        Vector3 bodyPos = animator.bodyPosition;
        bodyPos.y += lastPelvisPositionY;
        animator.bodyPosition = bodyPos;
    }

    private float GetFootOffset(AvatarIKGoal footGoal)
    {
        Vector3 footPos = animator.GetIKPosition(footGoal);
        Vector3 rayOrigin = footPos + Vector3.up * raycastUpOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastUpOffset + raycastDownDistance, environmentLayer))
        {
            // ФІКС 1: Перевіряємо кут (не ставимо ногу на стіни)
            if (Vector3.Angle(Vector3.up, hit.normal) > maxSlopeAngle) return 0f;

            // ФІКС 2: Перевіряємо максимальну висоту (не піднімаємо ногу занадто високо)
            float heightDiff = hit.point.y - transform.position.y;
            if (heightDiff > maxStepHeight) return 0f;

            return heightDiff;
        }
        return 0f;
    }

    private void HandleFootIK(AvatarIKGoal footGoal, float weight)
    {
        if (weight <= 0f) return;

        Vector3 footPosition = animator.GetIKPosition(footGoal);
        Quaternion footRotation = animator.GetIKRotation(footGoal);

        Vector3 rayOrigin = footPosition + Vector3.up * raycastUpOffset;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastUpOffset + raycastDownDistance, environmentLayer))
        {
            // Ті самі запобіжники для самої ноги
            if (Vector3.Angle(Vector3.up, hit.normal) > maxSlopeAngle) return;
            if (hit.point.y - transform.position.y > maxStepHeight) return;

            Vector3 targetPosition = hit.point;
            targetPosition.y += footYOffset;

            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * footRotation;

            animator.SetIKPosition(footGoal, targetPosition);
            animator.SetIKRotation(footGoal, targetRotation);
        }
    }
}