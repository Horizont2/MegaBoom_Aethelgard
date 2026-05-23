using UnityEngine;
using System.Collections;

public class ShopCameraController : MonoBehaviour
{
    public static ShopCameraController Instance;

    [Header("Animation Settings")]
    public float transitionDuration = 0.5f;
    [Tooltip("Крива анімації (рекомендується InOut для плавного старту і зупинки)")]
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Camera Views (Empty GameObjects)")]
    public Transform defaultView; // Загальний вигляд (всі категорії)
    public Transform headView;
    public Transform chestView;
    public Transform armsView;
    public Transform beltView;
    public Transform legsView;
    public Transform feetView;

    private Coroutine moveCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        // При старті сцени камера автоматично стає на позицію за замовчуванням
        if (defaultView != null)
        {
            transform.position = defaultView.position;
            transform.rotation = defaultView.rotation;
        }
    }

    // Метод для кнопок категорій (приймає індекс з твого enum ArmorCategory)
    public void MoveToCategory(int categoryIndex)
    {
        Transform target = defaultView;

        switch (categoryIndex)
        {
            case 0: target = headView; break;  // Head
            case 1: target = chestView; break; // Chest
            case 2: target = armsView; break;  // Arms
            case 3: target = beltView; break;  // Belt
            case 4: target = legsView; break;  // Legs
            case 5: target = feetView; break;  // Feet
        }

        if (target != null) MoveToTarget(target);
    }

    // Метод для кнопки "Back to Categories"
    public void MoveToDefault()
    {
        if (defaultView != null) MoveToTarget(defaultView);
    }

    private void MoveToTarget(Transform target)
    {
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(MoveRoutine(target));
    }

    private IEnumerator MoveRoutine(Transform target)
    {
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);
            float curveT = transitionCurve.Evaluate(t);

            transform.position = Vector3.Lerp(startPos, target.position, curveT);
            transform.rotation = Quaternion.Slerp(startRot, target.rotation, curveT);

            yield return null;
        }

        transform.position = target.position;
        transform.rotation = target.rotation;
    }
}