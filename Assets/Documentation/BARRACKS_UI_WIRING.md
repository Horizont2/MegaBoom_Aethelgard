# Barracks UI — Field wiring cheatsheet

Map of every UI element in your Figma panel → the `[SerializeField]` on
`BarracksUpgradePanel` / `BarracksHireRow` / `BarracksUpgradeUnitRow`.
Wire each one, and everything text/state/colour-related updates itself.

**Rule of thumb:** if a slot is missing in your prefab, leave that field
`None` in the inspector — nothing crashes, that widget just stays untouched.

---

## 1. Root panel (BarracksUpgradePanel component)

Attach to the top-level `BarracksPanel` GameObject in your Canvas.

### Root

| Figma element | Field | Type | Notes |
|---|---|---|---|
| Whole panel container (visible/hidden) | `rootObject` | GameObject | The parent of the entire panel that toggles on/off |
| CanvasGroup on same GO | `canvasGroup` | CanvasGroup | Fade + interactable control |
| `[F] CLOSE` / X button | `closeButton` | Button | Static label — script only handles click |

### Header

| Figma element | Field | Type | Notes |
|---|---|---|---|
| "BARRACKS" title text | `titleText` | TMP | Script writes "BARRACKS" on Open |
| `◆ 68` diamond chip **number only** | `diamondsText` | TMP | Just the number; ◆ glyph stays static in prefab |
| 5 helm level pips (Image[]) | `barracksLevelPips` | Image[] | Array — drag 5 pip Images left→right |
| Filled helm sprite | `pipHelmFilledSprite` | Sprite | Warm-brass silhouette |
| Empty helm sprite | `pipHelmEmptySprite` | Sprite | Hollow steel outline |

### Tab strip

| Figma element | Field | Type | Notes |
|---|---|---|---|
| HIRE tab hit-area | `tabHireButton` | Button | |
| UPGRADE UNITS tab hit-area | `tabUpgradeUnitsButton` | Button | |
| UPGRADE BARRACKS tab hit-area | `tabUpgradeBarracksButton` | Button | |
| HIRE tab text | `tabHireLabel` | TMP | Color swaps active/inactive |
| UPGRADE UNITS tab text | `tabUpgradeUnitsLabel` | TMP | |
| UPGRADE BARRACKS tab text | `tabUpgradeBarracksLabel` | TMP | |
| HIRE brass underline | `tabHireUnderline` | GameObject | Shown on active tab only |
| UPGRADE UNITS underline | `tabUpgradeUnitsUnderline` | GameObject | |
| UPGRADE BARRACKS underline | `tabUpgradeBarracksUnderline` | GameObject | |
| HIRE darker "active" background rect | `tabHireActiveBg` | GameObject | Optional — only if you have a separate dark rect |
| UPGRADE UNITS active bg | `tabUpgradeUnitsActiveBg` | GameObject | |
| UPGRADE BARRACKS active bg | `tabUpgradeBarracksActiveBg` | GameObject | |
| Active/Inactive text colours | `tabActiveTextColor` / `tabInactiveTextColor` | Color | Defaults match #F0E4CB / #8A8478 |

### Tab containers

| Figma element | Field | Type | Notes |
|---|---|---|---|
| HIRE tab content root | `hireContainer` | GameObject | Toggled visible when Hire active |
| UPGRADE UNITS tab content root | `upgradeUnitsContainer` | GameObject | |
| UPGRADE BARRACKS tab content root | `upgradeBarracksContainer` | GameObject | |

### Hire tab

| Figma element | Field | Type | Notes |
|---|---|---|---|
| Vertical parent for 3 unit rows | `hireRowParent` | Transform | Script instantiates 3 row prefabs into here |
| One-row prefab (see §2 below) | `hireRowPrefab` | GameObject | Must expose `BarracksHireRow` fields |
| The 3 unit SO catalogue (fallback) | `roster` | MercenaryRoster | Usually leave `None`; singleton found at runtime |

### Upgrade Units tab

| Figma element | Field | Type | Notes |
|---|---|---|---|
| Vertical parent for 3 rows | `upgradeRowParent` | Transform | |
| One-row prefab (see §3 below) | `upgradeRowPrefab` | GameObject | Exposes `BarracksUpgradeUnitRow` fields |
| Filled diamond pip sprite | `unitPipFilledSprite` | Sprite | Small brass diamond (~14px) |
| Empty diamond pip sprite | `unitPipEmptySprite` | Sprite | Hollow steel diamond |

### Upgrade Barracks tab

| Figma element | Field | Type | Notes |
|---|---|---|---|
| CampBuilding on the barracks GO | `hostBuilding` | CampBuilding | Drag your Building_Barracks |
| Big circular diorama image | `barracksDioramaImage` | Image | You supply the diorama sprite; script doesn't swap it |
| "LEVEL 3 / 5" text | `barracksCurrentLevelText` | TMP | Auto-formatted from `hostBuilding.currentLevel` |
| "MAX SIZE 5 UNITS · KNIGHT TIER UNLOCKED" | `barracksSummaryText` | TMP | Auto from CampBuilding.levels[current-1].productionDescription (or override via `summaryTextPerLevel[]`) |
| "LEVEL 4" next-level card title | `barracksNextLevelText` | TMP | Auto from `currentLevel + 1` |
| Perks list (2-line ◆ bulleted) | `barracksPerksText` | TMP | Auto with ◆ bullet from next.productionDescription (or override via `perksTextPerLevel[]`) |
| Wood cost number | `barracksCostWoodText` | TMP | Rust-red if unaffordable |
| Stone cost number | `barracksCostStoneText` | TMP | |
| Food cost number | `barracksCostFoodText` | TMP | |
| Big UPGRADE button | `barracksUpgradeButton` | Button | Non-interactable when unaffordable |
| UPGRADE button label | `barracksUpgradeButtonText` | TMP | "UPGRADE" / "MAX" |
| "4:00" build time text | `barracksBuildTimeText` | TMP | Auto formatted `M:SS` |
| Affordable colour | `costAffordableColor` | Color | Warm cream default |
| Unaffordable colour | `costUnaffordableColor` | Color | Muted rust default |

### Per-level overrides (optional)

If you want custom perks / summary strings that don't come from
`CampBuilding.levels[N].productionDescription`, fill these:

| Field | Type | Notes |
|---|---|---|
| `perksTextPerLevel[]` | string[5] | Perks shown when at level N going to N+1. Use `\n` for multi-line, ◆ for bullets |
| `summaryTextPerLevel[]` | string[6] | Summary shown at current level N. Index 0 = at Lv 0, etc. |

Example for `perksTextPerLevel[3]` (going from Lv 3 to Lv 4):
```
◆ +2 army capacity
◆ Ambush & Siege tactics unlocked
```

---

## 2. Hire row prefab (BarracksHireRow component)

Component auto-added to your `hireRowPrefab` at runtime — just drag the
UI slot references into these fields on the prefab.

| Figma element | Field | Type |
|---|---|---|
| Circular portrait Image | `iconImage` | Image |
| Unit name (e.g. "MILITIA") | `nameText` | TMP |
| One-line flavour under the name | `descriptionText` | TMP |
| "OWNED: 15" text (full string) | `ownedText` | TMP |
| Cost number "50" (◆ stays static in prefab) | `costText` | TMP |
| HIRE button | `hireButton` | Button |

Script writes:
- `nameText.text` = data.displayName
- `descriptionText.text` = data.flavourText
- `ownedText.text` = `OWNED: N`
- `costText.text` = base hire cost
- `hireButton.interactable` = unlocked (barracks level ≥ minBarracksLevel) AND can afford

---

## 3. Upgrade Units row prefab (BarracksUpgradeUnitRow component)

| Figma element | Field | Type |
|---|---|---|
| Circular portrait Image | `iconImage` | Image |
| Unit name | `nameText` | TMP |
| Flavour under name | `descriptionText` | TMP |
| 5 diamond level pips | `levelPips` | Image[] |
| ATK current number | `atkCurrentText` | TMP |
| ATK next number | `atkNextText` | TMP |
| HP current number | `hpCurrentText` | TMP |
| HP next number | `hpNextText` | TMP |
| Cost number | `costText` | TMP |
| UPGRADE button | `upgradeButton` | Button |
| UPGRADE button text | `upgradeButtonText` | TMP |

Script writes:
- `levelPips[i].sprite` = filled/empty based on current unit level
- `atkCurrentText.text` / `atkNextText.text` = ATK at level N and N+1
- `hpCurrentText.text` / `hpNextText.text` = HP at level N and N+1
- `costText.text` = upgrade cost or "MAX"
- `upgradeButtonText.text` = "UPGRADE" or "MAX"
- `upgradeButton.interactable` = false when maxed or unaffordable

**Note on the ATK/HP `→` arrow** — that stays as a static Text element in
your prefab, positioned between the current and next value TMPs. Script
doesn't touch it.

---

## 4. What the script does NOT touch (stays static in prefab)

- Panel background rectangle + border
- Header divider under "BARRACKS"
- Crossed-swords glyph SVG
- ◆ glyph next to each cost number
- ATK / HP labels (left of the values)
- The `→` arrow in stat-preview rows
- Resource icons (wood/stone/food) — you drop the game's existing sprites in
- `[F] CLOSE` hint text
- Clock icon next to build time

---

## 5. Live refresh

The panel refreshes automatically when:
- `MercenaryRoster` changes (hire, upgrade, permadeath)
- The player's diamond total changes (polled once per Update)
- The player switches tabs

There's no manual `.Refresh()` you need to call from elsewhere.
