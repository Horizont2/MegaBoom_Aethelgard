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
    private Transform currentAttacker;

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
        // √равц€ може ще не бути (в≥н генеруЇтьс€), тому знайдемо його п≥зн≥ше!
        FindPlayerIfNeeded();
    }

    private void FindPlayerIfNeeded()
    {
        if (playerTrans == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTrans = p.transform;
        }
    }

    public void ShowThreat(Transform attacker, float duration = 0.8f)
    {
        FindPlayerIfNeeded(); // ‘≤ —: ЎукаЇмо гравц€ кожен раз, €кщо в≥н загубивс€

        if (threatIndicatorImage == null || playerTrans == null) return;

        currentAttacker = attacker;
        displayDuration = duration;

        StopAllCoroutines();
        StartCoroutine(ThreatRoutine(attacker));
    }

    public Transform GetCurrentThreat()
    {
        return currentAttacker;
    }

    private IEnumerator ThreatRoutine(Transform attacker)
    {
        float elapsed = 0f;
        Color warningColor = new Color(1f, 0.4f, 0f, 1f);
        threatIndicatorImage.color = warningColor;

        Vector3 startScale = Vector3.one * 1.3f;

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

            float t = elapsed / displayDuration;

            if (t < 0.2f) transform.localScale = Vector3.Lerp(startScale, Vector3.one, t / 0.2f);

            if (t > 0.8f)
            {
                float fadeT = (t - 0.8f) / 0.2f;
                threatIndicatorImage.color = Color.Lerp(warningColor, new Color(1f, 0f, 0f, 0f), fadeT);
            }
            else
            {
                threatIndicatorImage.color = warningColor;
            }

            yield return null;
        }

        threatIndicatorImage.color = new Color(1f, 0f, 0f, 0f);
        currentAttacker = null;
    }
}