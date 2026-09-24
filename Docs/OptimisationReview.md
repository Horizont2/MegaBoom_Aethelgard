# Hollow Siege — where the frame time goes

A read of the project as it stands, ordered by what a change would actually buy.
Every number below was read out of the project files, not estimated. Where I did
not measure something I say so rather than guessing at it.

The standing rule for this project applies throughout: **Ultra must keep looking
the way it looks.** Everything here is either free, or it is a saving on the
presets below Ultra.

---

## First, the good news, so effort goes to the right place

**The script layer is clean.** I went through every `Update`, `LateUpdate` and
`FixedUpdate` in `Assets/Scripts` looking for the usual per-frame sins —
`FindObjectsByType`, `GameObject.Find`, `GetComponentInParent` on a hot path.
Every one that exists is either behind a null-cache that fills once
(`AltarOfFate`, `RitualMonolith`, `Reliquary`, `ExtractionPoint`,
`XpCrystalManager`, `GrenadeUI`), throttled to a timer
(`TotemMinimapTracker`, one second), or inside a key-press branch
(`GlobalHUD`'s Escape handler). There is no free win sitting in C# here.

**The two presets really are different pipelines.** `QualitySettings` has two
levels and they point at different URP assets:

| | Performance | Ultra |
|---|---|---|
| Pipeline asset | `Mobile_RPAsset` | `PC_RPAsset` |
| MSAA | 1× | **8×** |
| Render scale | 0.8 | 1.0 |
| Shadow distance | 50 | 150 |
| Shadow cascades | 1 | 3 |
| Soft shadows | off | on |
| Extra-light shadows | off | on |
| Depth texture | off | **on** |
| Opaque texture | off | **on** |

One trap worth knowing: the `shadowDistance: 40` and `shadowCascades: 2` sitting
in `QualitySettings.asset` are **inert**. Under URP those come from the pipeline
asset, so editing them in the Quality window changes nothing and makes the two
presets look identical when they are not.

**Lights are not a problem.** GameScene has three, one of which casts shadows.

**Terrain is set sensibly.** `m_DrawInstanced: 1`, heightmap pixel error 20,
detail distance 80, trees out to 600 m with billboards from 30 m.

---

## 1. Ultra runs 8× MSAA — **MEASURE, then drop to 4×**

`PC_RPAsset.m_MSAA: 8`.

This is the single most expensive line in the project. MSAA cost scales with
sample count across the whole frame: eight samples means the depth/colour
buffers are eight times the size and every edge is resolved eight ways. At 1080p
it is heavy; at 4K it is the dominant cost of the frame and the most likely
reason a 4K capture crawls.

On this art style — flat-shaded low-poly with hard silhouettes and almost no
high-frequency texture detail — **4× and 8× are very hard to tell apart**, and
the difference in a still is essentially nil. That is exactly the case MSAA
stops paying for itself.

I have not profiled it, so I will not claim a figure. Run the game at 4K, switch
`m_MSAA` between 8 and 4, and read the GPU frame time in the stats window. If
the gap is what I expect, this alone is the whole optimisation job.

Keep 8× as an option if you want it — but it belongs on a switch, not as the
default Ultra ships with.

---

## 2. A full-screen opaque copy, every frame, for one UI blur — **fix this**

`PC_RPAsset` has `m_RequireOpaqueTexture: 1`. That tells URP to copy the colour
buffer into `_CameraOpaqueTexture` after the opaque pass, every frame, for as
long as the game is running. (`m_OpaqueDownsampling: 1` halves it, so it is a
half-resolution copy rather than a full one — better, not free.)

**DONE — and it turned out to be free.** Two things in the project sample the
scene colour, and neither one ships:

| Shader | Materials | Used by |
|---|---|---|
| `UI_BlurShader.shadergraph` | `UI_BlurMaterial`, `UI_BlurMaterial2` | nothing — no prefab, no scene, no script |
| `Bitgem/.../WaterVolume-URP.shadergraph` | `example-water-01/02/03` | only Bitgem's own example scene |

The water the game actually uses is `MI_Water_MeadowsLake`, whose shader is
`M_Water_Lake_Amp.shader`, and that does not sample scene colour at all. So the
copy was being made every frame, for the whole session, for nobody.

`m_RequireOpaqueTexture` is now 0 on `PC_RPAsset` (Mobile already had it off).
Nothing looks different, because nothing was reading it.

`m_RequireDepthTexture: 1` stays — the volumetric fog needs it.

---

## 3. Texture sources are 3.1 GB, and the UI is the reason

1,607 texture source files, 3,148 MB on disk. The largest are not characters or
terrain — they are **UI icon sheets**:

| File | Source resolution |
|---|---|
| `GameUI/MapIcons/ally.png` | 8192 × 8192 |
| `MapUI/WinterStorm.png` | 11264 × 5388 |
| `MapUI/DesertStorm.png` | 11264 × 5407 |
| `Icons/BuildingsIcons.png` | 9600 × 5236 |
| `Icons/PauseMenuIcons.png` | 9600 × 5236 |
| `ShopIcons/ShopButtonsIcons.png` | 9600 × 5240 |
| `Icons/Resource Icons.png` | 9600 × 5236 |
| …fourteen more at 9600 px | |

These look like full-canvas exports rather than authored icons.

**Runtime cost is bounded** — they import at `maxTextureSize: 2048` with
mipmaps off and compression on, which is correct for UI, so a 9600 px sheet
becomes about 2048 × 1117 in memory. Twenty-odd of those is still real VRAM for
pictures shown a few hundred pixels wide.

**The unbounded costs are import time, project size and the build.** Every one
of these is re-encoded on a reimport, and a fresh clone of this project pulls
three gigabytes of textures.

The fix is boring and large: re-export the sheets at the size they are actually
drawn at. Nothing about the game changes; the repository and the build get
dramatically smaller. Do it before the Steam build, not after.

---

## 4. The minimap renders the world a second time — **already mitigated, worth knowing**

`MinimapCamera`'s culling mask is `Default + Damageable + MinimapOnly +
MinimapGraphics`. `Default` is where the terrain, trees, rocks and buildings
live, so every visible one of them is submitted twice per rendered minimap
frame.

That is inherent to a top-down minimap drawn from a real camera, and it is
already rate-limited to `renderHz` (45 by default) and skipped entirely when the
minimap is hidden. As of this session it is also driven by toggling the camera's
`enabled` flag rather than a manual `Camera.Render()`, which was both unsupported
under URP and a crash source.

If it ever needs to be cheaper: give the minimap its own quality settings by
lowering `renderHz` on the lower presets. Dropping `Default` from the mask is
not an option — it is what the minimap is showing.

---

## 5. Where the lower presets could go further

Performance already does the big things right: render scale 0.8, MSAA off, one
cascade, no soft shadows. Two more levers exist and are not being pulled:

- **`DistanceOptimizer`** switches registered objects off past a distance that
  follows the foliage preset — 130 m at the lowest, 600 m on Ultra (effectively
  off, deliberately). That is a good curve and it is working.
- **`WorldEncounterDirector.densityMultiplier`** is a flat 1.8 in GameScene for
  everyone. Encounters stream in within 95 m so live enemy count is bounded, but
  the placement pass and the group objects are not free. This could scale with
  the preset.

Neither is urgent. Both are real if a low-end machine still struggles after 1
and 2.

---

## What I did not measure

I have no profiler capture, so nothing here is a frame-time figure. This is a
read of settings and code, which is good at finding *what is switched on that
should not be* and useless at ranking two things that are both legitimate.

Before acting on 1 or 2, take a capture: **Window → Analysis → Profiler**, play a
generated region for thirty seconds, and look at the GPU module. If the top of
that list is not MSAA resolve and the opaque blit, then I have ordered this
wrongly and the capture will say so in a minute.

---

## Order I would do them in

1. Profile, so the rest is aimed.
2. `m_MSAA` 8 → 4 on `PC_RPAsset` if the capture agrees. One line.
3. Deal with the opaque texture. Half a day for option 1, ten minutes for option 2.
4. Re-export the UI sheets. Tedious, no risk, and it is the difference between a
   sane build and a fat one.
5. Only if a low-end machine is still short: preset-scale the encounter density.
