using UnityEngine;

// The "!" that floats over the notice board.
//
// (This file used to be saved as cp1251, so its Ukrainian comments rendered as
// mojibake in every editor that assumes UTF-8 — including this one. Rewritten
// as UTF-8 in place; Unity reads either, but only one of them is readable.)
public class MissionBoardMarker : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject exclamationMark;   // перетягни сюди знак оклику дошки місій

    private float checkTimer = 0f;

    private void Start()
    {
        UpdateMarkerState();
    }

    private void Update()
    {
        // Раз на секунду — щоб не питати дошку щокадру.
        checkTimer += Time.deltaTime;
        if (checkTimer >= 1f)
        {
            checkTimer = 0f;
            UpdateMarkerState();
        }
    }

    public void UpdateMarkerState()
    {
        if (exclamationMark == null) return;

        // Не раніше, ніж Еліас пояснив, що таке дошка.
        bool hasHeardIntro = PlayerPrefs.GetInt("Elias_Intro", 0) == 1;

        // ==== A MARKER IS A PROMISE, AND IT HAS TO BE KEPT MORE THAN ONCE ====
        //
        // The old condition was hasHeardIntro && !MissionBoard_Visited, plus a
        // board-has-work check. Both halves were wrong.
        //
        // MissionBoard_Visited is set the first time the player opens the board
        // and never cleared, so after that first visit the mark was gone for the
        // rest of the save — no matter how many times the board restocked. A
        // notice board that cannot tell you it has new work is not a notice
        // board.
        //
        // And HasMissionsToTake asked each paper for activeInHierarchy while the
        // papers sit under a canvas the board deactivates at startup, so it
        // answered "empty" until the board was open. Between the two, the mark
        // could only ever be seen by a player who was already reading the board.
        //
        // What it should say: there is something here to take, and you have not
        // seen this batch. Both are now real questions with real answers.
        bool boardHasWork = NoticeBoardManager.Instance == null
                            || NoticeBoardManager.Instance.HasMissionsToTake;

        bool unseen = NoticeBoardManager.HasUnseenRestock;

        exclamationMark.SetActive(hasHeardIntro && boardHasWork && unseen);
    }

    // Kept for the scene's own wiring: the board interaction can still call this
    // directly. Opening the board is what actually marks the batch as seen, so
    // this only forces an immediate refresh rather than owning the state.
    public void MarkAsVisited()
    {
        PlayerPrefs.SetInt("MissionBoard_Visited", 1);
        PlayerPrefs.Save();
        UpdateMarkerState();
    }
}
