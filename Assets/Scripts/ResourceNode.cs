using UnityEngine;
using System.Collections;

public class ResourceNode : MonoBehaviour, IDamageable
{
    public enum NodeType { Tree, Rock, Barrel }

    [Header("Type Settings")]
    public NodeType nodeType = NodeType.Tree;
    public float minHealth = 50f;
    public float maxHealth = 200f;

    [Header("Drops")]
    public GameObject dropPrefab;
    public int minDrops = 2;
    public int maxDrops = 5;

    [Header("Effects")]
    public ParticleSystem hitEffect;
    public GameObject stumpPrefab;

    private float currentHealth;
    private float actualMaxHealth;
    private Vector3 originalScale;
    private bool isDead = false;

    // Tracked handle for the last hit-SFX instance so we can manually stop
    // it before the FMOD event's tail finishes — otherwise the rustle
    // outlived the bush that made it and stacked over the next chop.
    private int hitSfxHandle = -1;
    // Timer that fades the hit-SFX out shortly after the last hit. Restarted
    // on every TakeDamage; if it completes we know the player stopped hitting.
    private Coroutine autoStopSfxRoutine;

    private IEnumerator AutoStopHitSfxRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.35f);
            hitSfxHandle = -1;
        }
        autoStopSfxRoutine = null;
    }

    private void Start()
    {
        actualMaxHealth = Random.Range(minHealth, maxHealth);
        currentHealth = actualMaxHealth;
        originalScale = transform.localScale;
    }

    private void OnDestroy()
    {
        // Belt & braces — if the node is destroyed by an external system
        // (pool return, scene unload) mid-hit, don't leak the FMOD instance.
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0f);
            hitSfxHandle = -1;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        if (isDead) return;

        currentHealth -= info.Amount;

        // Fire the impact SFX at the exact moment damage lands. The old
        // code fired it back in PlayerController.ExecuteAttack, which is
        // driven by an animation event whose placement varies per swing
        // clip — some clips triggered it late (thud after the visible
        // contact), some very early (thud on mouse-down instead of on
        // impact). Playing here couples the sound to the actual
        // physics moment the axe connects with the resource.
        if (AudioManager.Instance != null)
        {
            string clip = nodeType == NodeType.Rock
                ? AudioID.Player_HitResource_Stone
                : AudioID.Player_HitResource_Wood;

            // Tracked 3D instance: kill any previous hit-sfx from THIS node
            // before starting the new one so successive chops don't stack a
            // long-tailed rustle on top of itself. Handle is stopped again
            // in DeathRoutine so the sound cannot outlive the bush.
            if (hitSfxHandle != -1)
                AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.05f);
            hitSfxHandle = AudioManager.Instance.PlayLoopingSFX3D(clip, transform.position);
        }

        if (hitEffect != null)
        {
            if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
            hitEffect.Play();
        }

        StopAllCoroutines();

        // Restart auto-fade timer AFTER StopAllCoroutines so it doesn't
        // get killed by its own siblings. If the player stops attacking,
        // this fades the hit loop out ~0.4 s later instead of leaving the
        // FMOD instance ringing until the node dies.
        autoStopSfxRoutine = StartCoroutine(AutoStopHitSfxRoutine(0.4f));

        if (currentHealth <= 0)
        {
            isDead = true;
            StartCoroutine(DeathRoutine());
        }
        else
        {
            if (nodeType == NodeType.Rock)
            {
                float healthPercent = currentHealth / actualMaxHealth;
                Vector3 targetScale = originalScale * Mathf.Max(0.4f, healthPercent);
                StartCoroutine(SquishRoutine(targetScale));
            }
            else
            {
                StartCoroutine(WobbleRoutine());
            }
        }
    }

    private IEnumerator SquishRoutine(Vector3 targetScale)
    {
        float t = 0;
        Vector3 squishScale = new Vector3(targetScale.x * 1.1f, targetScale.y * 0.9f, targetScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(transform.localScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, targetScale, t); yield return null; }
        transform.localScale = targetScale;
    }

    private IEnumerator WobbleRoutine()
    {
        float t = 0;
        Vector3 squishScale = new Vector3(originalScale.x * 1.1f, originalScale.y * 0.9f, originalScale.z * 1.1f);
        while (t < 1) { t += Time.deltaTime * 15f; transform.localScale = Vector3.Lerp(originalScale, squishScale, t); yield return null; }
        t = 0;
        while (t < 1) { t += Time.deltaTime * 10f; transform.localScale = Vector3.Lerp(squishScale, originalScale, t); yield return null; }
        transform.localScale = originalScale;
    }

    private IEnumerator DeathRoutine()
    {
        // Kill the last hit rustle with a short fade so it doesn't sing
        // out over the destroyed bush / falling tree.
        if (hitSfxHandle != -1 && AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoopingSFX(hitSfxHandle, 0.25f);
            hitSfxHandle = -1;
        }

        int dropCount = Random.Range(minDrops, maxDrops + 1);
        for (int i = 0; i < dropCount; i++)
        {
            if (dropPrefab != null) Instantiate(dropPrefab, transform.position + Vector3.up * 1.5f, Quaternion.identity);
        }

        if (nodeType == NodeType.Tree)
        {
            // Timber! Play the tree-fall crack/crash as the trunk starts to topple.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Env_TreeFall, transform.position);

            Vector3 pivotPoint = transform.position;
            Collider[] cols = GetComponentsInChildren<Collider>();

            // ��������� �������� ����� (�����) ��� �� ���������
            if (cols.Length > 0 && cols[0] != null) pivotPoint.y = cols[0].bounds.min.y;

            // ==========================================
            // Բ�� 2: ������ �������� �� ��������� ������, ��� ���� �� ���������� ������
            // ==========================================
            foreach (Collider c in cols)
            {
                if (c != null) c.enabled = false;
            }

            // ���в����� ��������� ����ֲ� ������ �� ���� �� ���� �����
            Quaternion initialRotation = transform.rotation;

            float fallDuration = 0.32f;   // snappier fall, tighter to the crack/crash SFX
            float fallSpeed = 90f / fallDuration;
            float t = 0;

            while (t < fallDuration)
            {
                t += Time.deltaTime;
                transform.RotateAround(pivotPoint, transform.right, fallSpeed * Time.deltaTime);
                yield return null;
            }

            if (hitEffect != null)
            {
                if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
                hitEffect.Play();
            }

            // ����������Ӫ�� ��������� ����ֲ� ��� ������
            if (stumpPrefab != null) Instantiate(stumpPrefab, pivotPoint, initialRotation);

            yield return new WaitForSeconds(0.5f);
            Destroy(gameObject);
        }
        else
        {
            // Rock/ore shatters — play the stone-break crumble.
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX3D(AudioID.Env_StoneBreak, transform.position);

            if (hitEffect != null)
            {
                if (!hitEffect.gameObject.activeSelf) hitEffect.gameObject.SetActive(true);
                hitEffect.Play();
            }

            // Hide every renderer AND every collider in the hierarchy —
            // compound rocks/nodes use child colliders that used to keep
            // ghosting invisible walls when only the root got disabled.
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            yield return new WaitForSeconds(1.5f);
            Destroy(gameObject);
        }
    }
}