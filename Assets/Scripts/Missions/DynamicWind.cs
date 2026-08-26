using UnityEngine;
using System.Collections;

[RequireComponent(typeof(WindZone))]
public class DynamicWind : MonoBehaviour
{
    private WindZone windZone;

    [Header("Wind Settings")]
    public float minWindWaitTime = 15f; // ̳��������� ��� �� ������ ����
    public float maxWindWaitTime = 35f; // ������������ ���

    [Header("Systemic wind")]
    [Tooltip("Publish the wind as global shader properties (_GlobalWindDir.xyz = direction, .w = strength) so grass/foliage/cloth shaders can all sway with the SAME wind as the trees.")]
    public bool publishGlobalWind = true;
    [Tooltip("Also push scene particle systems (smoke, dust, ash) with the wind so they drift the same way. Uses WindZone as an external force — the particle systems must enable their External Forces module.")]
    public bool driveParticles = true;

    void Start()
    {
        windZone = GetComponent<WindZone>();
        StartCoroutine(WindRoutine());
    }

    void Update()
    {
        if (windZone == null) return;
        // One global wind vector the whole world can read — the systemic part.
        if (publishGlobalWind)
        {
            Vector3 dir = transform.forward;
            float strength = windZone.windMain;
            Shader.SetGlobalVector("_GlobalWindDir", new Vector4(dir.x, dir.y, dir.z, strength));
            Shader.SetGlobalFloat("_GlobalWindStrength", strength);
            // Also feed Unity's built-in foliage wind param used by many shaders.
            Shader.SetGlobalVector("_Wind", new Vector4(dir.x, dir.z, strength * 0.5f, windZone.windTurbulence));
        }
    }

    IEnumerator WindRoutine()
    {
        while (true)
        {
            // �������� ���� ���� ��� ����
            float targetMain = Random.Range(0.1f, 1.2f); // ���� ����
            float targetTurbulence = Random.Range(0.1f, 0.8f); // �����������

            // ���������� �������� (��������� ��� ��'���)
            Quaternion targetRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            float t = 0;
            float transitionDuration = 6f; // ³��� ������ ��������� ����� 6 ������!

            float startMain = windZone.windMain;
            float startTurbulence = windZone.windTurbulence;
            Quaternion startRotation = transform.rotation;

            // ������ ������������ (���� �� �'� �� ����)
            while (t < 1)
            {
                t += Time.deltaTime / transitionDuration;
                windZone.windMain = Mathf.Lerp(startMain, targetMain, Mathf.SmoothStep(0, 1, t));
                windZone.windTurbulence = Mathf.Lerp(startTurbulence, targetTurbulence, Mathf.SmoothStep(0, 1, t));
                transform.rotation = Quaternion.Lerp(startRotation, targetRotation, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }

            // ������ ����� ��������� �����
            yield return new WaitForSeconds(Random.Range(minWindWaitTime, maxWindWaitTime));
        }
    }
}