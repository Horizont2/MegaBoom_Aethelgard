// Marker interface implemented by a component that lives on the same
// GameObject as a CampBuilding and wants to intercept the [F] panel open.
// CampBuilding.OpenPanel checks for a sibling implementing this before it
// falls through to the generic aaaPanel — that's how BarracksBuilding
// substitutes its own Hire / Upgrade / Upgrade Barracks screen.
public interface ICustomBuildingPanel
{
    void OpenCustomPanel();
    // Called by CampBuilding's F-toggle when the panel is already open, so
    // pressing F a second time closes it instead of leaving it stuck open.
    void CloseCustomPanel();
}
