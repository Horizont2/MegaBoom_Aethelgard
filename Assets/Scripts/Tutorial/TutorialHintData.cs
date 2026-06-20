using UnityEngine;
using UnityEngine.Video;

[CreateAssetMenu(fileName = "Hint_", menuName = "Tutorial/Hint Data")]
public class TutorialHintData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Унікальний ключ. Він і є аргумент TutorialHints.Instance.ShowIfNew(key).")]
    public string key;

    [Header("Content")]
    [Tooltip("Заголовок панелі ('MOVE', 'COMBAT', 'TOTEM' тощо)")]
    public string title = "TIP";
    [Tooltip("Основний текст. Підтримує rich-text (<b>, <color=#...>)")]
    [TextArea(2, 6)] public string body;
    [Tooltip("Маленька іконка зліва від заголовку (опціонально)")]
    public Sprite icon;

    [Header("Video Guide (AC4-style)")]
    [Tooltip("Короткий VideoClip, що циклічно програється поряд із текстом. Як у Assassin's Creed.")]
    public VideoClip videoClip;

    [Header("Display")]
    [Tooltip("Скільки секунд показувати, перш ніж приховати")]
    public float duration = 6f;
    [Tooltip("Чекати на натискання будь-якої клавіші для закриття замість таймера")]
    public bool waitForInput = false;
    [Tooltip("Звук, що грає при появі панелі (опціонально)")]
    public AudioClip showSound;
}
