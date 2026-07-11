# Figma / AI Image Prompts — Mercenary System Panels

Style anchor: sunlit trader-booth parchment aesthetic, low-poly diegetic UI,
Cinzel Bold + Montserrat Light + JetBrains Mono, tier colours
(muted parchment / moss / steel / violet / honey). Do NOT go dark-fortress.

Resolution: 1920×1080 for each full screen; 512×512 for isolated icons.

---

## 1. BarracksUpgradePanel (screen)

**Prompt (paste into Figma AI / Midjourney / Ideogram):**

> A game UI mockup, 1920×1080, sunlit warm parchment aesthetic in the style
> of a medieval trader booth. Central panel is a large open scroll pinned
> onto weathered oak planks, subtle grain, edges curling. Top header reads
> "BARRACKS" in Cinzel Bold, warm brown ink, small crossed-swords insignia
> centred underneath. Right of the header is a level pip row: five
> knight-helm icons, first three filled with honey gold, last two empty
> pewter, no dashes or lines between them.
>
> Below the header is a tab strip of three ribbon-shaped buttons: HIRE,
> UPGRADE UNITS, UPGRADE BARRACKS. Active tab is the leftmost, painted
> honey-yellow with a small hanging brass tag; inactive tabs are muted
> parchment brown.
>
> Content area (HIRE tab shown) — three horizontal wooden shelves, one per
> unit type. Each shelf: on the left a circular hemp portrait framed with
> rope (Militia peasant with pitchfork, Ranger hooded scout with longbow,
> Knight bearing kite shield), portrait rendered in warm painterly style.
> To the right of the portrait, the unit name in Cinzel Bold, one-line
> flavour in Montserrat Light italic below. On the far right of each shelf,
> a stack: current owned count in a small wax-seal circle, a diamond ◆
> icon with the hire cost in JetBrains Mono, and an amber primary button
> "HIRE" — softly glowing.
>
> Top-right corner of the full panel shows the player's diamond count on a
> small hanging brass placard. Bottom-right corner has a subtle small
> "[F] CLOSE" hint. Background is a soft cross-hatched off-white with hints
> of a distant camp painted in tan and moss green.
>
> Textures: paper grain, soft drop shadows, no hard black, no neon. Zero
> gradients on buttons, only flat painterly fills.

**Layout guidance for Figma frame** (use these numbers when replicating):

- Root frame: 1920×1080, background HSB(38, 12%, 96%)
- Central scroll: x=200 y=90, size 1520×900, curl accents at corners
- Header row: y=110, "BARRACKS" 96px Cinzel Bold, colour #5B3A1A
- Level pips: x=1440 y=120, 5 helm icons 40×40 gap 12
- Tab strip: y=220, three ribbons 380×60 gap 8
- Shelf area: y=310, three shelves 1380×180, vertical gap 30
- Shelf inner: portrait Ø140 padding 20, name text at x=180, cost stack right-aligned
- Diamond placard: top-right corner x=1720 y=40, 160×60
- Palette:
  - Parchment BG: #F5EBD6
  - Ink brown: #5B3A1A
  - Honey gold accent: #D9A24A
  - Moss (Ranger tint): #7A8E5C
  - Steel (Knight tint): #8B94A0
  - Wine (Militia tint): #B25E52

---

## 2. PreBattlePanel (screen)

**Prompt:**

> A game UI mockup for a pre-battle campaign screen, 1920×1080, medieval
> war-table aesthetic. A large oak war table shot from above, illuminated
> by warm afternoon light from an off-frame window. Centre-top: a rolled
> hand-drawn map of the target region, corners pinned by daggers, region
> name written in Cinzel Bold in dark ink ("SHATTERED BRIDGE").
> Underneath the map, a small carved wooden plaque: "ENEMY STRENGTH: 870"
> in JetBrains Mono, with a red pennant next to it. Below that: "TRAVEL:
> 2m 30s" beside a compass sigil.
>
> Left half of the screen: three horizontal rows for army selection, one
> per unit type. Each row: circular portrait framed with rope on the left,
> unit name and "Available: N" underneath in Montserrat Light, minus/plus
> engraved wooden buttons flanking a large chalk-marked count number in
> the middle, all set against a linen banner background.
>
> Right half: a stone slab pin-board titled "TACTIC" in Cinzel Bold.
> Three vertical banners:
>   • AMBUSH — dark green wolf-head sigil, subtitle "Fewer losses, faster
>     march" in Montserrat Light italic.
>   • ASSAULT — bronze crossed-swords sigil, subtitle "Balanced".
>   • SIEGE — dark grey trebuchet sigil, subtitle "Fewest losses, slow".
> Active tactic (Assault) is lifted forward with a soft brass glow.
>
> Below the tactic column, a forecast card: "RISK: EVEN" in warm amber,
> "EXPECTED LOSSES: 2-4" in muted brown, "ARMY SCORE: 690" in JetBrains
> Mono in charcoal.
>
> Bottom of the screen: a huge honey-gold primary button labelled
> "MARCH ◆" with a subtle glow, and a smaller plain "CANCEL" text button
> to its left. No gradients on buttons — flat painterly fills. Zero neon.

**Layout numbers:**

- Root: 1920×1080, BG #F1E6CE
- Region scroll top-centre: x=520 y=60, size 880×280
- Region title Cinzel Bold 84px #3E2617
- Enemy strength plaque: below scroll y=350, 400×64
- Left army column: x=140 y=440, three rows 600×160 gap 24
- Right tactic slab: x=1200 y=440, 560×360
- Forecast card: x=1200 y=820, 560×140
- MARCH button: bottom-centre x=760 y=980, 400×72, colour #D9A24A
- CANCEL: text left of MARCH, 24px Montserrat

---

## 3. BattleResultPanel (modal)

**Prompt:**

> A game UI mockup, 1200×720, victory result modal for a medieval campaign.
> Centre: a huge unfurled banner in warm honey, pinned at the top by iron
> studs, hanging in front of a subtly blurred stone hall. Banner reads
> "VICTORY" in Cinzel Bold ~150px in deep umber, with laurel garlands on
> both sides. Underneath, a smaller line: region name "SHATTERED BRIDGE"
> in Cinzel Bold 42px. Below that, a linen strip: "Your army routed the
> defenders." in Montserrat Light italic.
>
> Bottom half of the modal shows two carved wooden panels side by side:
>   • Left: a small tombstone silhouette icon and text "LOSSES: 2 / 6" in
>     Cinzel Bold, colour a muted rust.
>   • Right: a diamond ◆ icon in honey gold and "+230" in Cinzel Bold in
>     brass, with a smaller "Delivered to camp" in Montserrat Light below.
>
> A single big primary button at the bottom centre: "CONTINUE" in honey
> gold, flat painterly fill.
>
> A subtle background halo of golden dust particles rises from the base.
> No neon, no gradient meters.
>
> Alternative composition for DEFEAT: the banner is torn at the top-right
> corner, colour is a muted brick red, "DEFEAT" text, laurel replaced by
> broken swords, reward panel greyed out and reads "—".

**Layout numbers:**

- Modal frame: 1200×720, BG rgba(240, 230, 210, 0.98) with soft outer
  drop shadow
- Banner: x=100 y=40, size 1000×360
- Title 150px Cinzel Bold #4E2A16
- Region subtitle: y=280, 42px
- Two panels: y=440, each 480×220, gap 24
- CONTINUE button: y=680, centred, 320×64, #D9A24A

---

## 4. Army figurine (map marker)

**Prompt:**

> A tiny top-down icon for a fantasy world map, 128×128 transparent PNG,
> painterly style. Three overlapping oval helm silhouettes seen from above,
> arranged like an arrowhead, with a single vertical flag pole rising in
> the centre bearing a small honey-gold pennant with a black crown crest.
> Warm parchment palette, soft ink outline. No neon, no gradients, isolated
> against transparent background.
>
> Provide three colour variants of the pennant, one per unit type — militia
> wine red, ranger moss green, knight steel blue.

---

## 5. Unit portraits & icons

**Icon prompts (512×512, isolated):**

- **Militia icon** — > Circular hemp-rope frame around a rustic wax portrait
  of a bearded peasant in a simple linen tabard holding a wooden pitchfork.
  Warm parchment palette, painterly. Behind the portrait a faint wheat-sheaf
  motif. No neon, no gradients, transparent background.

- **Ranger icon** — > Circular hemp-rope frame around a hooded scout with a
  short longbow. Face partly shadowed by the hood, a single leaf pin on
  the shoulder. Moss-green ranger tint on the cloak. Painterly.
  Transparent background.

- **Knight icon** — > Circular hemp-rope frame around a helmed knight in
  polished steel plate, kite shield bearing a golden lion crest. Warm
  honey-gold pauldrons. Painterly parchment palette, isolated. Transparent
  background.

**Portrait prompts (768×1024):**

- **Militia portrait** — > Half-body medieval peasant militiaman, weathered
  linen tabard, straw-blonde beard, worn wooden pitchfork over shoulder,
  small dented iron pot as helmet, standing under warm afternoon sunlight
  against a soft parchment cross-hatched background. Painterly, muted
  earthy palette with wine-red accents on his belt. Cinematic composition.

- **Ranger portrait** — > Half-body medieval scout, dark green hooded
  cloak, leather bracers, longbow held slack, quiver of dark-fletched
  arrows visible, wolfhound at heel (optional), moss-green palette with
  warm parchment background. Painterly, soft sunlight.

- **Knight portrait** — > Half-body medieval knight in polished steel
  plate, kite shield with a golden lion crest, longsword sheathed at hip,
  visored helm tucked under the arm revealing weathered noble face.
  Honey-gold and steel-blue palette, warm parchment background. Painterly,
  heroic composition.

---

## 6. Tactic sigils (192×192 each, isolated)

- **Ambush** — a stylised wolf head silhouette in dark moss green, filled
  with subtle bramble motifs, on a hemp-linen circle.
- **Assault** — two crossed longswords in bronze, on a hemp-linen circle
  with a small honey-gold sunburst behind.
- **Siege** — a stylised trebuchet counterweight in dark grey iron, on a
  hemp-linen circle with faint stone-wall pattern.

---

## 7. Risk band badges (200×64, one per state)

Small horizontal banner ribbons carrying the risk label. Text in Cinzel Bold
24px.

- **OVERWHELMING** — soft mint green ribbon, laurel accent on left.
- **FAVOURABLE** — honey gold ribbon.
- **EVEN** — muted parchment ribbon.
- **RISKY** — burnt orange ribbon.
- **SUICIDAL** — brick red ribbon with a small skull motif on the left.

All ribbons flat painterly fill, no neon.

---

Copy any of these into Figma AI, Midjourney, Ideogram or your generator of
choice. For a coherent look, run all prompts in a single seed / session so
the palette stays consistent across assets.
