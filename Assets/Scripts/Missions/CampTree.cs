using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class CampTree : MonoBehaviour
{
    [Header("Tree Settings")]
    public int requiredHits = 3;
    public float respawnTime = 15f;
    public bool isChopped = false;

    private int currentHits;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 originalScale;

    // ��'���� �� ����� �������� ������
    private TreeVFX vfxHandler;

    private void Start()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalScale = transform.localScale;

        currentHits = requiredHits;

        // ����������� ������ ������ VFX �� ����� � �����
        vfxHandler = GetComponent<TreeVFX>();
    }

    public void TakeHit()
    {
        if (isChopped) return;

        currentHits--;

        // ������ ������� ������ �������� ����� (���� �� �)
        if (vfxHandler != null) vfxHandler.PlayHitEffect();

        StopCoroutine("ShakeRoutine");
        StartCoroutine("ShakeRoutine");

        if (currentHits <= 0)
        {
            StartCoroutine(FallAndRespawnRoutine());
        }
    }

    private IEnumerator ShakeRoutine()
    {
        float t = 0;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            float angle = Mathf.Sin(t * 40f) * 3f;
            transform.rotation = originalRotation * Quaternion.Euler(angle, 0, angle);
            yield return null;
        }

        if (!isChopped) transform.rotation = originalRotation;
    }

    private IEnumerator FallAndRespawnRoutine()
    {
        isChopped = true;

        // Timber! Play the same tree-fall crack/crash the arena trees use — the
        // camp tree toppled silently before.
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX3D(AudioID.Env_TreeFall, transform.position);

        Collider col = GetComponent<Collider>();
        Vector3 rootPoint = transform.position;
        if (col != null)
        {
            rootPoint = new Vector3(col.bounds.center.x, col.bounds.min.y, col.bounds.center.z);
        }

        Vector2 randDir = Random.insideUnitCircle.normalized;
        Vector3 fallAxis = new Vector3(randDir.x, 0, randDir.y);

        float fallDuration = 1.5f;
        float elapsed = 0f;
        float currentAngle = 0f;

        while (elapsed < fallDuration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / fallDuration;

            float easedProgress = Mathf.Pow(progress, 2.5f);
            float targetAngle = Mathf.Lerp(0f, 90f, easedProgress);
            float angleDiff = targetAngle - currentAngle;

            transform.RotateAround(rootPoint, fallAxis, angleDiff);
            currentAngle = targetAngle;

            yield return null;
        }

        // ������ �����! ������ ������� ������ �������� ����� ���� ������� ������
        if (vfxHandler != null) vfxHandler.PlayFallEffect(rootPoint);

        yield return new WaitForSeconds(2f);

        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * 2f;
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, t);
            yield return null;
        }

        yield return new WaitForSeconds(respawnTime);

        transform.position = originalPosition;
        transform.rotation = originalRotation;

        t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * 0.5f;
            transform.localScale = Vector3.Lerp(Vector3.zero, originalScale, t);
            yield return null;
        }

        currentHits = requiredHits;
        isChopped = false;
    }
}