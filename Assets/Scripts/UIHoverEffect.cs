using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // ������ ��� ������� �� Button

public class UIHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("������������ ��������")]
    public float hoverScale = 1.05f;
    public float clickScale = 0.95f;
    public float speed = 15f;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private Button myButton; // ��������� �� ������

    void Start()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;
        myButton = GetComponent<Button>(); // �������� ��������� ������
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * speed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Якщо кнопка є і вона вимкнена - взагалі не реагуємо
        if (myButton != null && !myButton.interactable) return;
        targetScale = originalScale * hoverScale;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUI(AudioID.UI_Hover);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (myButton != null && !myButton.interactable) return;
        targetScale = originalScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (myButton != null && !myButton.interactable) return;
        targetScale = originalScale * clickScale;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (myButton != null && !myButton.interactable) return;
        targetScale = originalScale * hoverScale;
    }
}