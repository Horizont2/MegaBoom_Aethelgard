# Mercenary System — Outstanding Work

This is what still needs to happen after the code + data pass is done.
Ordered by "you can't ship without it" → "nice to have".

## A. Blocking (must be in Unity before anything runs)

1. **_MercBootstrap GameObject** — new empty GO in the first scene loaded
   at boot (BootLogo or MenuScene). Attach `MercenaryRoster` and
   `MercenaryCampaignManager` on it, mark it root, do NOT drop it into
   a scene that unloads.
   - Fill `MercenaryRoster.catalogue` with 3 SO (Militia, Ranger, Knight).
   - Fill `MercenaryCampaignManager.regionCatalogue` with all 24 Region_N.

2. **Barracks GameObject in CampScene**
   - Model: reuse Elias / Hunter / Forge shell — no new visual per your
     note.
   - Add `CampBuilding`. Fill `levels[]` from the 5-row table in
     `MERCENARY_SYSTEM_DATA.md §2`.
   - Add `BarracksBuilding`. Wire `wanderPoints[]` (place 4-6 empty
     Transforms around the model) and `unitSpawnPoint`.
   - Add `BoxCollider` (trigger) for the F prompt, tagged the same way
     as other buildings.

3. **3 unit-catalogue SO assets** (`Assets/RegionData/Mercenaries` or your
   `Unit Datas` folder — either works, just make sure they're the ones
   dragged into `MercenaryRoster.catalogue`).
   - I've filled all numeric fields. Only the artwork slots (icon,
     portrait, mapFigurineSprite, campPrefab) remain empty — plug the
     Figma outputs and the wandering-unit prefab into them.

4. **UI prefabs** (design + wire; scripts already exist)
   - `BarracksUpgradePanel` prefab — hangs off the barracks or from a
     BuildingCanvas. Wire root/tabs/parents/prefab references.
   - `PreBattlePanel` prefab — child of MapCanvas.
   - `BattleResultPanel` prefab — child of MapCanvas (global).
   - Row sub-prefabs (`hireRowPrefab`, `upgradeRowPrefab`, `unitRowPrefab`)
     — one row template per panel, script components auto-added at runtime.

5. **WorldMapArmyMarker on MapCanvas** — attach the component to the map
   root, fill `armyOriginPoint` (the camp icon RectTransform),
   `markerParent`, and `figurinePrefab` (small UI Image with the army
   sprite).

6. **MapPanelUI.preBattlePanel** field — drag PreBattlePanel instance into
   MapPanelUI in the inspector. Without this, clicking a no-totem region
   still opens the "start journey → GameScene" path.

## B. Content the code assumes

1. **Unit campPrefabs** — the wandering NPC visuals with Idle + Walk +
   NavMeshAgent. If none exist, temporary stand-in is fine (any of the
   existing HunterAI models with animator).

2. **Region illustrations** for the 19 auto-battle regions — they already
   have `regionIllustration` in the data but if any are empty, the
   PreBattlePanel Figma design uses that illustration as backdrop. Leave
   as-is if all 24 already have art.

3. **BarracksBuilding.aaaPanel field on CampBuilding** — the parent
   `CampBuilding` still opens its generic AAA panel on F. For the barracks
   we want F to open BarracksUpgradePanel instead. Two options:
   - **Simple**: leave `CampBuilding.aaaPanel = null` on the barracks
     GameObject, and add a tiny raycast/`F` handler on `BarracksBuilding`
     that opens `barracksPanel`. (Add a 10-line `Update()` there.)
   - **Neater**: refactor `CampBuilding.OpenPanel()` into virtual method
     so `BarracksBuilding` overrides it.
   Ping me and I'll wire whichever you prefer. **This is a real gap in
   the current code.**

## C. Nice-to-have polish

1. **Notification when campaign completes** — right now BattleResultPanel
   shows itself via the event when the return trip ends, but only if the
   panel is in the currently-loaded scene. If the player is in
   GameScene when a campaign returns, the popup is missed. Fix: cache the
   completion in `MercenaryCampaignManager` and let a scene-level listener
   surface any pending results on next MapCanvas open.

2. **Diamond HUD reactivity** — the shop already updates on
   `ResourceManager.diamonds` change; make sure the auto-battle win goes
   through `AddDiamonds` (it does — verify in play).

3. **Region conquer VFX on auto-win** — when the world map re-opens, a
   won auto-battle region should probably flash / play the same "region
   unlocked" storm-dissolve as a manually-conquered one. `RegionUI`
   already handles `isNewlyUnlocked=true` — I set it in
   `CompleteCampaign`, so this should Just Work. Verify in play.

4. **Save wipe dev tool** — add a menu item so mercs can be reset without
   clearing all PlayerPrefs. Trivial, half a screen of code.

5. **Localisation** — texts I wrote are English:
   - Panel titles: BARRACKS / MARCH / VICTORY / DEFEAT / etc.
   - Tactic names: Ambush / Assault / Siege
   - Flavour texts.
   None run through `LocalizationManager.Tr`. Wrap them if you're shipping
   the game in Ukrainian. (Trivial edit — I skipped it to keep the diff
   smaller; happy to do a pass.)

6. **Balance retune knobs** — the 5 pivot numbers to tweak if the loop
   feels off:
   - `minTravelSeconds` / `maxTravelSeconds` on Manager (currently 45 / 180)
   - `autoBattleDiamondReward` per region (all 24 set)
   - `baseHireCost` per unit (25 / 65 / 180)
   - `upgradePricePerLevel` arrays (already scaled)
   - `enemyStrength` per region (already scaled)

## D. Known limitations / future work

1. **One campaign per region at a time** — `MapPanelUI` blocks a second
   launch if one is in flight for the same region. But you *can* have
   many campaigns in parallel for *different* regions. Intended.

2. **No retreat option** — once army marches, no cancel. If you want
   "recall army" I'd add a button on the world map when a marker is
   marching, refund half the hires or lose 20 % of the men, your call.

3. **No region weakening on defeat** — I said in the design chat that a
   losing attempt could permanently weaken the region's `enemyStrength`
   by a small %. Not implemented yet — that's a 5-line addition in
   `CompleteCampaign` guarded on `!c.won`.

4. **No visual sound wiring for battle** — the 6-second Fighting phase is
   silent for now. If you want a distant battle SFX + camera zoom on the
   marker, that's a WorldMapArmyMarker pass.

5. **Barracks visual is a static single mesh across levels** — per your
   note. If you later add tier meshes, `CampBuilding.SetupVisualsForCurrentLevel`
   already handles the ghost/real swap; add per-level model children.

---

## Priority order I'd tackle it in

**Day 1 (blockers):**
- A.1 bootstrap
- A.2 barracks placement
- A.3 SO wiring
- C.3 fix (F opens BarracksUpgradePanel)

**Day 2 (UI):**
- A.4 panel prefabs
- A.5 army marker
- A.6 MapPanelUI field
- Playtest one campaign end-to-end (hire → march → resolve → return)

**Day 3 (polish):**
- C.1 cross-scene result caching
- C.5 localisation pass
- Balance retune based on playtest feel
