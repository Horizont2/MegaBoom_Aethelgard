# Mercenary System — Current Status (Post-Playtest)

Snapshot of what's actually done vs still open after tonight's session.

---

## ✅ Done and testable now

### Code
- All 11 barracks scripts (`Assets/Scripts/Barracks/*`)
- `ICustomBuildingPanel` hook so barracks `F` opens the custom UI when wired
- `RegionCombatMode` enum with `Auto/PlayerCombat/MercenaryAutoBattle`
- `EnemySpawner`/`WorldEncounterDirector` fix so conquered regions have
  radial spawns
- Debug context menu on `MercenaryCampaignManager` (hire free, dispatch,
  force-unlock, fast-forward, wipe save)
- Barracks visual auto-promotes to L1 at scene start (invisible-first-frame
  bug fixed)
- Merc unit visuals no longer roller-skate: root motion off, full
  HeroAnimator params (Speed + IsGrounded + MoveX + MoveZ)
- Camp NPCs (Lumberjack / Hunter / Storage) got the same fix
- Lumberjack `workDistance` bumped so he stops before clipping into tree
- `AnimationEventReceiver.TriggerFootstepDust` handler added — the
  spam log is gone, and NPCs now get 3D footstep audio

### Data
- 3 `MercenaryUnitData` assets (`Militia/Ranger/Knight`) with costs, stats,
  upgrade curves. **You've wired `campPrefab` on all three** — good.
- 24 `Region_*.asset` files carry `enemyStrength` +
  `autoBattleDiamondReward` + new `combatMode` field
- Barracks scene overrides updated: description + 5 levels (costs, times,
  descriptions)
- Full docs in `Assets/Documentation/`:
  `MERCENARY_SYSTEM_DATA.md`, `MERCENARY_FIGMA_PROMPTS.md`,
  `MERCENARY_AUDIT_TODO.md`, this file

---

## 🟡 What YOU still need to wire in Unity (unblocks the merc loop)

Ordered by "test-blocker" → "polish":

### Test-blocker

1. **`_MercenaryBootstrap` GameObject**
   - You've already created it (visible in your screenshot). Confirm it
     carries both `MercenaryRoster` and `MercenaryCampaignManager`.
   - `MercenaryRoster.catalogue` = [Militia, Ranger, Knight] SO — set.
   - `MercenaryCampaignManager.regionCatalogue` = 24 Region assets — set
     (shows "24" in your screenshot, ✓).

2. **Barracks GameObject `BarracksBuilding` component**
   - `barracksPanel` field is `None` in your screenshot. Until you wire
     it, hitting `F` on the barracks silently does nothing. Blocked until
     UI prefab exists (see #3).
   - `wanderPoints[]` array — populate with SpawnPoint/wanderPoint (0..3)
     children you already have in the hierarchy.

3. **BarracksUpgradePanel prefab** (screen shown on `F`)
   - Panel root with a CanvasGroup + close button.
   - 3 tab buttons + 3 content containers.
   - `hireRowPrefab` — one row per unit type. Fields:
     iconImage / nameText / ownedText / costText / hireButton
     (component `BarracksHireRow` will be added at runtime).
   - `upgradeRowPrefab` — same shape, fields:
     iconImage / nameText / levelText / costText / upgradeButton
     (`BarracksUpgradeUnitRow`).
   - Barracks-upgrade section: level label, cost label, upgrade button.
   - Then drag it into `BarracksBuilding.barracksPanel` and set
     `hostBuilding` on the panel to point at Building_Barracks.

4. **PreBattlePanel prefab** (opens on Map when clicking an auto-battle
   region → replaces "Start Journey" travel)
   - Fields wired via SerializeField, layout in
     `MERCENARY_FIGMA_PROMPTS.md §2`.
   - `unitRowPrefab` per-type row: iconImage / nameText / availableText /
     countText / plus/minus buttons.
   - Drag the prefab instance into `MapPanelUI.preBattlePanel`.

5. **BattleResultPanel prefab** (popup when campaign returns)
   - Root/canvasGroup/close + title/regionName/outcome/casualties/reward.
   - Instance lives on the MapCanvas; it subscribes to
     `MercenaryCampaignManager.OnCampaignReturned` automatically.

6. **WorldMapArmyMarker on MapCanvas**
   - Component on the map root.
   - `armyOriginPoint` = camp icon RectTransform on the map.
   - `markerParent` = same parent (usually the map root).
   - `figurinePrefab` = simple UI Image with a small flag/helm sprite;
     will get repositioned per-campaign.

### Polish (nice-to-have but not blocking)

7. **Icons/portraits/map figurines** for the 3 units — prompts in
   `MERCENARY_FIGMA_PROMPTS.md §5`. Currently the merc SO assets have
   `icon`/`portrait`/`mapFigurineSprite` fields empty.

8. **Tactic sigils** (Ambush/Assault/Siege) — prompts in §6.

9. **Risk band ribbons** (Overwhelming/Favourable/…/Suicidal) — §7.

10. **AudioID.Player_Footstep** — verify this event actually exists in
    your FMOD project. If not, the merc footsteps will silently no-op.
    (Not blocking — just silence instead of thump.)

---

## 🔴 Known code issues I still owe you

### None critical right now.

Everything from the earlier audit is either done or now waiting on your
UI prefabs. Once #3-6 above are wired, we can do a full end-to-end merc
run and I'll fix whatever else the playtest surfaces.

---

## 🧪 How to test right now (without any UI)

1. Play the CampScene.
2. Find `_MercenaryBootstrap` in the hierarchy.
3. Right-click `MercenaryCampaignManager` component → **Debug** submenu:
   - `Hire 3 Militia (free)` — 3 militia visuals should walk out of the
     barracks and wander. **They should walk, not slide.**
   - `Force-Unlock & Send All Idle Units → First Auto-Battle Region` —
     forces Region 6 to Available and marches your 3 militia there.
     Console prints "Sent 3 units to 'Ruined Tollkeep'. Outbound 45.0s…"
   - `Fast-Forward Active Campaigns By 30s` — 2× clicks and the campaign
     hits Fighting → resolves. Console prints result and diamonds granted.
4. Verify:
   - Diamonds increased (ResourceManager) if the battle was a win
   - Region 6 is now `Conquered` state
   - Casualty units are removed from the roster (check
     `MercenaryRoster.roster` in inspector)
   - Re-clicking `Force-Unlock & Send…` grabs the next unconquered
     region up the chain

Everything after step 4 is UI polish + Figma art. The gameplay simulation
is functionally complete.
