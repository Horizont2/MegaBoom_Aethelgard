# Mercenary System — Data & Integration Reference

All balance numbers, prefab wiring, texts and Figma image prompts for the
barracks / auto-battle system live here. Copy the tables into Unity as-is.

---

## 1. Mercenary Unit Data (3 SO assets)

| Field                | Militia     | Ranger        | Knight            |
|----------------------|------------:|--------------:|------------------:|
| unitID               | `militia`   | `ranger`      | `knight`          |
| displayName          | Militia     | Ranger        | Knight            |
| minBarracksLevel     | 1           | 2             | 3                 |
| baseHireCost (◆)     | 25          | 65            | 180               |
| baseAttack           | 15          | 30            | 55                |
| attackPerLevel       | 5           | 10            | 18                |
| baseHP               | 40          | 45            | 90                |
| hpPerLevel           | 15          | 18            | 35                |
| upgradePricePerLevel | [50,120,250,500] | [120,260,550,1200] | [280,620,1350,3000] |

### Score curves (auto-computed, informational)

Score = attack × (HP ÷ 20). Used by `BattleResolver` to compare armies.

| Unit    | Lv1 | Lv2 | Lv3 | Lv4 | Lv5  |
|---------|----:|----:|----:|----:|-----:|
| Militia | 30  | 55  | 88  | 128 | 175  |
| Ranger  | 67  | 132 | 220 | 335 | 481  |
| Knight  | 247 | 456 | 731 | 1076 | 1499 |

### Flavour text

- **Militia** — *Farmers with pitchforks and stubborn courage. Cheap to hire, quick to fall, but a full line of them turns a hopeless assault into an even one.*
- **Ranger** — *Silent scouts from the borderland forests. Devastating against unarmoured conscripts and the pace-setters of any ambush.*
- **Knight** — *Anointed champions of Aethelgard, sworn to steel and fire. A single Knight in the line can hold a breach the Militia would break against.*

---

## 2. Barracks Building Levels (fill CampBuilding.levels[])

Barracks auto-builds at Level 1 on scene start. Levels 2-5 are upgrades. Same
cost scale as Lumberjack/Hunter/Forge — resource ratios lean stone/food since
the barracks houses trained warriors.

| Lv | costWood | costStone | costFood | buildTime (s) | productionValue | productionDescription             |
|----|---------:|----------:|---------:|--------------:|----------------:|-----------------------------------|
| 1  | 0        | 0         | 0        | 0             | 1               | UNLOCKS MILITIA                   |
| 2  | 55       | 30        | 20       | 12            | 2               | UNLOCKS RANGER                    |
| 3  | 95       | 65        | 45       | 22            | 3               | UNLOCKS KNIGHT                    |
| 4  | 160      | 115       | 80       | 40            | 4               | +ARMY CAP · TACTICS AMBUSH/SIEGE |
| 5  | 240      | 190       | 140      | 65            | 5               | ALL TACTICS · +30% MARCH SPEED    |

The `productionValue` isn't spent by the barracks — I use it as a "Level"
label displayed in the inspector so max-level detection matches the shared
`CampBuilding.UpdateUIData` code.

---

## 3. Region Data (all 24, auto-battle strengths + rewards)

Regions 1-5 have hand-built totem locations — their `enemyStrength` /
`autoBattleDiamondReward` are set to sensible values but never used (the
player conquers via the location). Regions 6-24 route to `PreBattlePanel`.

| Region  | Name (existing)     | ID | totem? | enemyStrength | ◆ reward |
|---------|---------------------|---:|:------:|--------------:|---------:|
| 1       | Old Lumberyard      | 0  | ✓      | 60            | 15       |
| 2       | Whispering Thicket  | 1  | ✓      | 85            | 20       |
| 3       | Bandit's Crossing   | 2  | ✓      | 110           | 25       |
| 4       | Forgotten Shrine    | 3  | ✓      | 140           | 30       |
| 5       | Mossy Foothills     | 4  | ✓      | 175           | 35       |
| **6**   | Ruined Tollkeep     | 5  | —      | 60            | 20       |
| **7**   | Stonefall Quarry    | 6  | —      | 80            | 25       |
| **8**   | Sunken Outpost      | 7  | —      | 110           | 30       |
| **9**   | Howling Valley      | 8  | —      | 150           | 40       |
| **10**  | The Ashen Woods     | 9  | —      | 190           | 50       |
| **11**  | Ironpeak Pass       | 10 | —      | 240           | 65       |
| **12**  | Deadman's Gorge     | 11 | —      | 300           | 80       |
| **13**  | Smuggler's Cove     | 12 | —      | 370           | 100      |
| **14**  | Cursed Swampland    | 13 | —      | 450           | 120      |
| **15**  | Bloodstone Mines    | 14 | —      | 540           | 145      |
| **16**  | Desolate Tundra     | 15 | —      | 640           | 170      |
| **17**  | Warlord's Camp      | 16 | —      | 750           | 200      |
| **18**  | Shattered Bridge    | 17 | —      | 870           | 230      |
| **19**  | Obsidian Crags      | 18 | —      | 1000          | 265      |
| **20**  | The Poisoned Vein   | 19 | —      | 1150          | 305      |
| **21**  | Abyssal Descent     | 20 | —      | 1320          | 350      |
| **22**  | Citadel Outskirts   | 21 | —      | 1520          | 400      |
| **23**  | Gates of Ruin       | 22 | —      | 1800          | 480      |
| **24**  | The Throne Room     | 23 | —      | 2500          | 700      |

Auto-battle regions total reward pool: **~4 100 diamonds** if every one is
won. Combined with the 5 location regions' fixed rewards (~230 diamonds)
this gives roughly enough currency to fully hire and Lv-3-upgrade every
archetype (~7 500 diamonds needed), so the diamond farming loop
(surplus-resource conversion, replayable region levels) covers the rest.

Values were written into every `Region_*.asset` by a script — verify one
in the inspector before running.

---

## 4. Travel timing (already in MercenaryCampaignManager)

- `minTravelSeconds = 45` (Region 6)
- `maxTravelSeconds = 180` (Region 24)
- Linear interpolation across regionID. Multiplied by tactic:
  - Ambush ×0.85
  - Assault ×1.0
  - Siege ×1.5

Return trip is 80 % of outbound. Fighting pause is 6 s (audio-only interval,
no simulation cost).

---

## 5. Bootstrap wiring checklist

1. Create empty GO `_MercBootstrap` in **CampScene** and any scene that has
   the world map. Recommended: same scene as `ResourceManager`.
2. Add `MercenaryRoster`. In `catalogue` drag Militia, Ranger, Knight SO.
3. Add `MercenaryCampaignManager`. In `regionCatalogue` drag ALL 24
   `Region_N.asset`. Leave `minTravelSeconds=45`, `maxTravelSeconds=180`.
4. Both components have `DontDestroyOnLoad` — instantiate them only once
   per game boot.
5. On the **Map** canvas root:
   - Add `WorldMapArmyMarker`. Wire `armyOriginPoint` (camp icon RT) and
     `markerParent` (usually the same). Drag figurine prefab in.
   - Add `BattleResultPanel`. Wire its root/text/reward fields.
   - Add `PreBattlePanel`. Wire its root/rows/tactics/buttons.
6. On `MapPanelUI`, drag your `PreBattlePanel` into the new
   **Mercenary Auto-Battle** field.
7. On the barracks GameObject in Camp:
   - Add `CampBuilding` (like other buildings) — fill the 5 levels above.
   - Add `BarracksBuilding` — leave `autoBuildFirstLevel=true`.
   - Wire `barracksPanel` → your BarracksUpgradePanel prefab instance.
   - Wire `wanderPoints` (4-6 Transforms around the barracks) and
     `unitSpawnPoint`.

---

## 6. Save file reset (dev QoL)

The system uses these PlayerPrefs keys:
- `MercRoster_v1`, `MercUpgrades_v1`, `MercNextUID_v1`
- `MercCampaigns_v1`, `MercCampNextID_v1`

Add a "Wipe Mercenary Save" dev button (or Menu → Reset) if you want a
one-click clean slate during testing. Not required for shipping.
