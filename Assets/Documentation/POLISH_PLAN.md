# Polish plan — snapshot 2026-07-12

Prioritised by "visible impact / effort". Each item is roughly 1 commit.
Pick what fits the session; skip anything that doesn't feel important.

---

## 🟢 Quick wins (30 min – 1 hour each)

### UX friction

1. **Recall army button on the world-map region** — hovering an in-flight
   region shows a "Recall" prompt; refunds 50% units, cancels campaign.
   Currently the player is locked in for 2-6 min with no escape.
2. **Persistent "Active Campaigns" strip on MapCanvas** — small list at
   the top-right showing every in-flight army with region name + remaining
   time. Right now the player has to hover regions one by one.
3. **Somber notification on permadeath** — separate toast when N units die
   (not folded into the "Defeat" line), plus a short mourning bell SFX.
   Right now permadeath is silent apart from the count.
4. **Barracks visual level indicator** — even without a full model tier
   swap, add a level badge / flag colour that changes at Lv 2/3/4/5.
   Currently the same shed for all 5 levels.
5. **Diamond count animates** on gain/loss (tween the number, not just
   swap it). Small hit of juice on every earn.

### Audio

6. **Distant drum ambience while campaigns are in flight** — plays only
   when marker is on-map, loops quietly. Sells "war is happening".
7. **Deploy war-drum swell** on the deploy click — one-shot low tom.
8. **Camp ambient loop layers** — light fire crackle, distant crows,
   rustling leaves. Right now camp is nearly silent between actions.

### Localization gaps still remaining

9. Level1_QuestManager objectives + subtitle debug text (I saw them
   during audit).
10. Death cinematic quotes.
11. Boss telegraph / roar labels.
12. Achievement descriptions (if any exist).

---

## 🟡 Mid-effort polish (2-4 hours each)

### Merc mechanic depth

13. **Cinematic BattleResultPanel** — swap the flat popup for a proper
    result flow: fade-in banner, animated laurels for victory, torn banner
    for defeat, per-unit casualty list scrolls in with a slow reveal, then
    the diamonds tally counts up.
14. **March visuals on map** — figurine bobs / rotates naturally as it
    walks, small dust trail sprite behind, small pause-and-strike animation
    during the Fighting phase.
15. **Region siege overlay** — right now it's just a text toggle. Add a
    pulsing red aura around the region node while under siege, plus a
    small crossed-swords icon overlay.
16. **Barracks-upgrade tier flags** — add a small flag / banner near the
    barracks door that changes colour every 2 levels (levy → banner →
    heraldic → royal).

### Camp life

17. **NPC schedules** — Elias walks the perimeter at dusk, workers idle
    around fire at night. Currently they wander randomly all the time.
18. **Weather variation** — random light rain, dust motes in sunlight,
    snow flurries seasonally. There's already a SmartSeasonManager to
    hook into.
19. **Camp voice barks** — one-liners from Elias / workers when the
    player passes ("The forge grows cold, friend"). Adds warmth without
    needing full dialogue trees.

### Combat feel

20. **Enemy hit reactions** — flinch pose or knockback on hit.
21. **Death anim variety** — currently many enemies play the same fall.
22. **Loot pickup magnetic pull** — small XP / resource pickups drift
    toward the player when in range instead of standing still.
23. **Camera micro-shake** on landing hits.

### UI system

24. **AutoLocalize component sweep** — some scene UI still has hardcoded
    English text baked into TMP components (not runtime-set). An
    AutoLocalize pass fixes those without touching code.
25. **Font atlas rebuild** — the `➔`, `◆` warnings mean the
    default TMP font doesn't have arrows / diamonds. Add a fallback font
    or extend the atlas.
26. **Panel open/close motion** — every panel currently pops in instantly.
    A small ease-in scale (0.9→1.0 over 0.15s) gives them weight.

---

## 🔴 Bigger overhauls (multi-session)

### Systemic gaps

27. **19 hand-crafted regions instead of auto-battle** — would take the
    "content mass" from 5 → 24. Weeks of work; the merc system was the
    workaround for exactly this.
28. **Random events during merc campaigns** — sometimes a scout returns
    early with news ("Bandits sighted, +2 casualties expected"). Adds
    variance and creates decisions to abort or push.
29. **Boss variety** — 3-4 unique bosses instead of shared skeleton king.
30. **Player build variety** — more weapon types, armor sets, meta-perk
    trees. Current shop is minimal.
31. **Meta-progression persistence** — Aethelgard-wide upgrades that
    persist across runs (unlock via diamonds sink).

### Technical debt

32. **Object pooling for popups + damage numbers** — currently
    Instantiate/Destroy per hit. Fine for now, will matter at scale.
33. **FindObjectsByType audit** — several scripts poll FindFirst per
    frame. Cache references where possible.
34. **Save-file versioning** — right now PlayerPrefs is unversioned;
    a future refactor could break saves. Add version key + migration
    stubs.

---

## Recommended next 3 sessions

If you want a suggested path:

**Session A — 3 quick wins that gain most-visible impact:**
- Recall army button (#1)
- Persistent Active Campaigns strip (#2)
- Diamond count animation (#5)

**Session B — merc mechanic finish:**
- Cinematic BattleResultPanel (#13)
- March visuals on map (#14)
- Region siege overlay (#15)

**Session C — camp warmth:**
- Ambient audio layers (#8)
- Camp voice barks (#19)
- NPC schedules (#17)

---

## Not on this list on purpose

- Anything that requires major new art (unit portraits, region
  illustrations, boss models) — those are on the designer/artist track,
  not a code polish sprint.
- Balance retunes — the numbers I have now are a starting point; hold
  further changes until real playtest telemetry says something.
- Refactoring for its own sake — the codebase is fine, don't fix what
  isn't broken.
