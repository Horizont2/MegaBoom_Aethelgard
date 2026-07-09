using UnityEngine;

public class POISettings : MonoBehaviour
{
    [Header("Terraforming Passport")]
    [Tooltip("Радіус землі навколо локації, який буде ідеально вирівняно.")]
    public float flattenRadius = 30f;

    [Tooltip("Наскільки глибоко посадити префаб у вирівняну землю (зазвичай мінусове значення, щоб приховати фундамент).")]
    public float yOffset = -0.5f;

    [Tooltip("Максимально допустимий перепад висот на цій площі ДО вирівнювання (захист від гір). Для великих сіл став 5-7, для дрібних таборів 10.")]
    public float maxAllowedSlope = 6f;

    // Цей метод малює жовте коло в редакторі Unity! 
    // Ти одразу візуально побачиш, скільки місця займе локація і де її нульова точка.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position + Vector3.up * yOffset, flattenRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 1f); // Центр (Pivot)
    }
}