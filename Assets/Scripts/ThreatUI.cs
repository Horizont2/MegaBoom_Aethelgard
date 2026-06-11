using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ThreatUI : MonoBehaviour
{
    public static ThreatUI Instance;

    public Image threatIndicatorImage;
    public float displayDuration = 0.8f;

    private Transform playerTrans;
    private Camera mainCam;
    private Transform currentAttacker; // Зберігаємо ворога, який зараз атакує

    private void Awake()
    {
        Instance = this;
        if (threatIndicatorImage != null)
        {
            threatIndicatorImage.color = new Color(1f, 0.8f, 0f, 0f);
        }
    }

    private void Start()
    {
        mainCam = Camera.main;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTrans = p.transform;
    }

    public void ShowThreat(Transform attacker)
    {
        if (threatIndicatorImage == null || playerTrans == null) return;

        currentAttacker = attacker; // Запам'ятовуємо ціль для ідеального ухилення

        StopAllCoroutines();
        StartCoroutine(ThreatRoutine(attacker));
    }

    // Метод для PlayerController, щоб отримати координати цілі
    public Transform GetCurrentThreat()
    {
        return currentAttacker;
    }

    private IEnumerator ThreatRoutine(Transform attacker)
    {
        threatIndicatorImage.color = new Color(1f, 0.8f, 0f, 1f);
        float elapsed = 0f;

        while (elapsed < displayDuration)
        {
            elapsed += Time.deltaTime;

            if (attacker != null)
            {
                Vector3 directionToEnemy = attacker.position - playerTrans.position;
                directionToEnemy.y = 0;

                Vector3 playerForward = mainCam.transform.forward;
                playerForward.y = 0;

                float angle = Vector3.SignedAngle(playerForward, directionToEnemy, Vector3.up);

                transform.localRotation = Quaternion.Euler(0, 0, -angle);
            }

            float alpha = Mathf.Lerp(1f, 0f, elapsed / displayDuration);
            threatIndicatorImage.color = new Color(1f, 0.8f, 0f, alpha);

            yield return null;
        }

        threatIndicatorImage.color = new Color(1f, 0.8f, 0f, 0f);
        currentAttacker = null; // Очищаємо ціль, коли час вийшов
    }
}