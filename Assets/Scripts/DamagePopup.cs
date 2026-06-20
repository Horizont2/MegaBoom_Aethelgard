using UnityEngine;
using TMPro;
using System.Collections;

public class DamagePopup : MonoBehaviour
{
    [Header("Popup Settings")]
    public TextMeshPro textMesh;
    public float lifetime = 1f;

    [Header("Visual Styles")]
    public Color normalColor = Color.white;
    public Color critColor = new Color(1f, 0.8f, 0f);
    public float normalSize = 5f;
    public float critSize = 8f;

    private Color textColor;
    private Transform camTransform;
    private static Transform s_cachedCamTransform;

    private void Awake()
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();
    }

    private void Start()
    {
        if (s_cachedCamTransform == null)
        {
            Camera main = Camera.main;
            if (main != null) s_cachedCamTransform = main.transform;
        }
        camTransform = s_cachedCamTransform;
    }

    public void Setup(float damageAmount, bool isCrit = false)
    {
        // Re-acquire camera ref if cleared by scene unload (safe for pooled instances)
        if (camTransform == null)
        {
            if (s_cachedCamTransform == null)
            {
                Camera main = Camera.main;
                if (main != null) s_cachedCamTransform = main.transform;
            }
            camTransform = s_cachedCamTransform;
        }

        textMesh.text = "-" + Mathf.CeilToInt(damageAmount).ToString();

        // ����������� ��� ������� �� ������
        if (isCrit)
        {
            textMesh.text += "!";
            textMesh.color = critColor;
            textMesh.fontSize = critSize;
        }
        else
        {
            textMesh.color = normalColor;
            textMesh.fontSize = normalSize;
        }

        textColor = textMesh.color;

        // ���������� ����-����, ��� ����� �� ��������� � ����� �����, ���� ����� ������
        float randomX = Random.Range(-0.5f, 0.5f);
        float randomZ = Random.Range(-0.5f, 0.5f);
        transform.position += new Vector3(randomX, 1f, randomZ);

        // �������� � ��������� �������� ��� ������ ����� (Pop)
        transform.localScale = Vector3.zero;

        // ��������� �������� ��������
        StartCoroutine(AnimatePopupRoutine(isCrit));
    }

    private IEnumerator AnimatePopupRoutine(bool isCrit)
    {
        // ���� ������ �� ������ ����� �����
        float duration = isCrit ? lifetime + 0.5f : lifetime;
        float elapsed = 0f;

        Vector3 startPos = transform.position;

        // ���������� �������� �������� ��� � ����� ������/�����
        Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;

        // �������� ����� �������� ���
        Vector3 targetPos = startPos + randomDir * (isCrit ? 2.5f : 1.5f);

        Vector3 baseScale = Vector3.one;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration; // ������� �� 0 �� 1

            // 1. ��� �� ��� (Lerp + Sin ��� ������ �������������/���������)
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * 2f; // ������ ����

            // 2. ̳���-������� ��� ����� �� ������ ������� (����� �������� �������)
            if (isCrit && t < 0.2f)
            {
                currentPos += (Vector3)Random.insideUnitCircle * 0.15f;
            }

            transform.position = currentPos;

            // 3. ������� (Juicy Pop Effect: ���� ����������, ���������, ������ �� �����)
            float scaleCurve;
            if (t < 0.15f) scaleCurve = Mathf.Lerp(0f, 1.3f, t / 0.15f);
            else if (t < 0.3f) scaleCurve = Mathf.Lerp(1.3f, 1f, (t - 0.15f) / 0.15f);
            else scaleCurve = 1f;

            transform.localScale = baseScale * scaleCurve;

            // 4. ������ �������� ��������� � ����� �������� ��������
            if (t > 0.5f)
            {
                textColor.a = Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f);
                textMesh.color = textColor;
            }

            // 5. ������ �������� ����� � ������
            if (camTransform != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - camTransform.position);
            }

            yield return null;
        }

        // ������� ��'��� ���� ���������� ��������
        ObjectPoolManager.Instance.ReturnToPool(gameObject);
    }
}