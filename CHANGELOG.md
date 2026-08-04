# Changelog

All notable player-facing changes to Hollow Siege / Aethelgard.
Format follows [Keep a Changelog](https://keepachangelog.com/); versions follow
[semver](https://semver.org/): `MAJOR.MINOR.PATCH`. Pre-1.0 (`0.x`)
is the pre-Steam-release ramp.

Version-bump rules:
- **PATCH** — bug fixes, loc keys, small polish. No new feature or
  save-schema change.
- **MINOR** — new feature (menu, panel, system), balance rework, or
  systemic UX pass. May add save-schema fields (backwards-compatible).
- **MAJOR** — breaking save-schema change, engine bump, or a scope
  shift big enough to warrant a Steam-side milestone announcement.

The version stamp visible in the bottom-right of the Main Menu comes
from `ProjectSettings/ProjectSettings.asset → bundleVersion`. Bump
that here on every release, add a section below, then commit both in
the same change.

---

## [0.2.0] — 2026-08-04

Steam-prep foundation + a large bug-fix / localisation pass. Everything
below shipped since the `0.1.0` baseline.

### Added
- **Save system**: JSON at `Application.persistentDataPath/save_v1.json`,
  atomic-ish writes with `.bak`, migrates from legacy PlayerPrefs on
  first run. Steam Cloud–friendly.
- **Steam wrapper** (`SteamManager`) with 20 achievements, Rich Presence
  hook (`"In Camp"` / `"On a Mission"` / `"In the Prologue"`), and cloud
  flush stub. Wires to Facepunch.Steamworks / Steamworks.NET once
  imported; no-op otherwise.
- **Achievement toast**: gold banner top-right, runtime-built (no scene
  wiring).
- **Main menu extended**: New Game (with confirm), Continue, Credits,
  Quit (with confirm). `ConfirmDialog` utility for runtime Yes/No
  modals reused by pause-menu Quit-to-Desktop.
- **Credits UI**: auto-scrolling, closes on Esc / Space, headers in
  every shipped language.
- **Victory ending sequence**: fires on 24-region clear, fade + 4
  narration beats + credits + return to menu. Clears all "run in
  progress" flags so Continue can't resume a finished run.
- **Loading screen**: `SceneLoader` routes every scene hop through a
  single choke-point — polished `LoadingManager` when the scene wires
  it, auto-built black-fade overlay otherwise. Flushes save on hop.
- **Pause menu**: Restart Run (arena-only), Quit to Desktop (with
  modal confirm reusing the runtime `ConfirmDialog`).
- **Death cinematic** + slow-mo (recap panel planned, not shipped yet).
- **Controller / gamepad** input abstraction (`InputCompat`) for Steam
  Deck.
- **Analytics** stub with 100-event ring buffer and `region_conquered`
  / `merc_deploy` / `scene_loaded` events wired.
- **Crash logger**: exceptions rotated to `crash_log.txt` (+ `.prev`)
  at `persistentDataPath`, capped at 256 KB, header carries device +
  version so bug reports are useful.
- **Version stamp**: bottom-right label on Menu / MainMenu with
  `v0.2.0` + build GUID suffix in actual builds. Screenshots always
  identify the exact build.
- **Editor Build Validator**: refuses to build if any *Test /
  Prototype / Sandbox / Location_1..3* scene is enabled in Build
  Settings.
- **NPCGait** shared locomotion polish for camp NPCs — foot-planted
  animator tempo, MoveX / MoveZ blend params, smooth turn-in-place,
  terrain ground-snap. Kills the "roller-skate" gait across every
  camp AI.
- **AutoLocalizeScene**: scene-wide TMP walker that translates
  Inspector-authored labels on scene load + language change without
  needing per-panel code changes.

### Changed
- **Missions**: notice-board paper now shows WHAT to do ("Defeat 50
  enemies" / "Survive 5 minutes") as the primary call-to-action
  instead of the flavor description.
- **Language cap**: `MAX_SHIPPED_LANGUAGE = 1` — dropdown shows every
  slot but selection clamps to English / Ukrainian until other
  locales are complete.
- **Camp NPC campfire routine**: workers, hunters, storage NPC,
  patrollers (Elias) now walk to the fire at deep night, face it,
  and sit. Barracks visual mercs deliberately wander 24/7.
- **World layout**: 5 hand-built arenas redistributed across the 24
  regions by actual size; R22 (city) and R24 (throne) reserved.
- **Guide**: extended Camp Guide to 13 steps covering every new
  mechanic + one-shot `TutorialHints` on first encounter.

### Fixed
- **Softlocks**: VictoryEndingSequence leaked `IsRunActive` and
  static `s_alreadyFired`; MapTable NRE'd on null Camera.main; five
  region-conquest / totem NRE hazards via unguarded
  `Camera.main.GetComponent<CameraFollow>()`.
- **Stale player-blocked state**: LoadingManager was clobbering
  tutorial's control block on scene-in — replaced with an
  OR-with-`isLoading` in PlayerController. PauseSceneController now
  snapshots and restores instead of hard-clearing.
- **Coroutine massacres**: GlobalHUD.ClearAllPickupPopups and
  PlayerController.TakeDamage were calling `StopAllCoroutines()`,
  killing every unrelated routine on the target. Both now stop only
  their own tracked coroutines.
- **Cursor race**: Shop Update-loop no longer fights modals for the
  cursor; NoticeBoard cursor lock skips when a modal is up.
- **Giant tree freeze**: `GiantTreeVFXLOD` per-tick GC leak fixed
  (colliders cached in Awake), global cap on active giant-tree lights
  (only the 3 nearest have lights on — Forward+ tile stall gone),
  trunk / canopy renderers now actually hard-cull past 90 m with
  hysteresis, motion-vector generation forced off.
- **Ukrainian localization**: 51 shop keys (weapons + armor + tabs),
  15 level-up upgrades (names + descriptions + stat lines), 20
  achievement descriptions, credits body, compass rose, ending
  narration, and every string surfaced by the audit — EN / UK now
  parity end-to-end. `SHOP_NEED_MORE_DIAMONDS` literal typo fixed;
  `LevelUpManager.statDisplay` now routed through `Tr()`.
- **Save-on-quit**: `SteamLifecycleTicker` now flushes SaveSystem +
  PlayerPrefs on Pause / Focus loss / Quit (Steam Deck sleep +
  alt-tab used to lose the last minute of play).

---

## [0.1.0] — earlier

Baseline before the Steam-prep pass. History pre-dates this file.
