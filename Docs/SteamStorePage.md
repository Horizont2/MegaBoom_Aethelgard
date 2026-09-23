# Hollow Siege — Steam store page

Everything the Steamworks store page asks for, ready to paste.

Every claim below was read out of this repository, not imagined. Where a number
is an estimate rather than a measurement it says so in the line itself. Two
sections are marked **MEASURE** and **VERIFY** — those are the only ones that
cannot be finished from the code alone.

---

## 1. Basic info

| Field | Value | Where it comes from |
|---|---|---|
| Name | `Hollow Siege` | `ProjectSettings.asset` → `productName` |
| Developer | `Horizon Games` | `ProjectSettings.asset` → `companyName` |
| Publisher | `Horizon Games` | self-published |
| Bundle identifier | `com.HorizonGames.HollowSiege` | already set |
| Current build version | `0.2.0` | `bundleVersion` |
| Franchise | leave empty | first title |
| Website | — | optional, can be added later |

---

## 2. Short description

Steam caps this at **300 characters**. It is the single most-read piece of text
on the page: it appears under the capsule, in search, in wishlists and in every
email Steam sends about the game.

### English (231 characters)

> Carve a kingdom back out of the dark, one region at a time. Fight through
> procedurally built lands, break the totems that hold them, and haul the spoils
> home to a camp you rebuild — then send mercenaries to take what your blade
> cannot reach.

### Ukrainian

> Відвойовуй королівство з темряви — регіон за регіоном. Пробивайся крізь
> процедурно згенеровані землі, ламай тотеми, що їх тримають, і неси здобич до
> табору, який відбудовуєш. А туди, куди не дістає твій клинок, відправляй
> найманців.

---

## 3. About This Game

Steam's long description. BBCode is supported: `[h2]`, `[b]`, `[list]`, `[img]`.

### English

```
[h2]A kingdom of twenty-four regions, and every one of them is lost[/h2]

Aethelgard has fallen. From the Old Lumberyard to the Throne Room, twenty-four
regions stand under the dead, each one held by a totem that has to be broken by
hand. Take them back in whatever order you dare — the map does not care where
you start, only whether you survive what you walked into.

[h2]The land is built for you, and never twice the same[/h2]

Every region generates from a seed the moment you enter it: terrain, roads,
rivers, forests, ruins and the places worth finding. Three biomes — forest,
desert and winter — decide what grows there and what hunts you in it. A region
you have conquered keeps its seed, so the land you fought over stays the land
you come back to.

[h2]Fight close, and fight honestly[/h2]

No cover, no waiting. You block, you parry on a timing cue, you dodge through
what you cannot answer, and you finish what you have broken. Kill fast enough
and the Stack builds — fifteen enemies in the air at once and everything is
worth double. Fifteen upgrade lines wait at every level: crit, lifesteal,
thorns, dodge, greed, and the plain ones that just keep you alive.

[h2]Bring it home[/h2]

Wood, stone, food and diamonds come out of the field and go into a camp of six
buildings: the Lumberjack's Hut and the Hunter's Cabin to feed it, the Storage
Vault to hold what your workers carry in, the Blacksmith's Forge, the Scout's
Lodge, and the Barracks. Every region you hold pays tribute while you play.

[h2]Send someone else[/h2]

Some ground is not worth your own blood. Hire mercenaries, fill a roster of
five, choose a tactic — ambush, assault or siege — and read the risk before you
commit. They march, they fight without you, and they come back fewer than they
left.

[h2]A world that keeps moving[/h2]

Day turns to night and the dead get faster in it. Weather runs from clear to
storm. Six seasons cycle over the whole campaign. Reliquaries, altars of fate,
ritual monoliths and caged allies are out there to be found, and five lore
scrolls tell you what happened here — if you care to look.

[h2]At the end of it[/h2]

The Citadel Outskirts. The Gates of Ruin. The Throne Room, and the Overlord
sitting in it. Twenty achievements mark the road.
```

### Ukrainian

```
[h2]Королівство з двадцяти чотирьох регіонів, і всі втрачені[/h2]

Етельгард упав. Від Старої лісопилки до Тронної зали двадцять чотири регіони
під владою мертвих, і кожен тримає тотем, який доведеться зламати власноруч.
Відвойовуй у будь-якому порядку — мапі байдуже, звідки ти почав, важливо лише,
чи виживеш там, куди зайшов.

[h2]Земля будується під тебе і ніколи не повторюється[/h2]

Кожен регіон генерується із сіда в мить, коли ти в нього заходиш: рельєф,
дороги, річки, ліси, руїни і те, що варто знайти. Три біоми — ліс, пустеля і
зима — вирішують, що там росте і хто на тебе полює. Захоплений регіон зберігає
свій сід, тож земля, за яку ти бився, лишається тією самою.

[h2]Бий зблизька і чесно[/h2]

Ніяких укриттів і очікування. Блок, парирування за індикатором таймінгу, ухил
від того, на що немає відповіді, і добивання того, що вже зламав. Убивай
швидко — і росте Стак: п'ятнадцять ворогів одночасно, і все коштує вдвічі
більше. На кожному рівні чекає п'ятнадцять ліній прокачки: крит, вампіризм,
шипи, ухил, жадібність — і прості, які просто тримають тебе живим.

[h2]Неси додому[/h2]

Дерево, камінь, їжа і діаманти йдуть з поля в табір із шести будівель: хатина
лісоруба і хижа мисливця годують його, сховище тримає те, що зносять
робітники, далі кузня, дім розвідника і казарми. Кожен утриманий регіон платить
данину, поки ти граєш.

[h2]Відправ когось іншого[/h2]

За деяку землю не варто платити власною кров'ю. Наймай найманців, збирай загін
із п'ятьох, обирай тактику — засідка, штурм або облога — і зважуй ризик, перш
ніж підписатися. Вони йдуть, б'ються без тебе і повертаються не всі.

[h2]Світ, який не стоїть[/h2]

День змінюється ніччю, і вночі мертві швидші. Погода йде від ясної до грози.
Шість сезонів проходять за кампанію. Реліквії, вівтарі долі, ритуальні моноліти
й полонені союзники чекають, щоб їх знайшли, а п'ять сувоїв розкажуть, що тут
сталося — якщо тобі цікаво.

[h2]А в кінці[/h2]

Передмістя цитаделі. Брама руїн. Тронна зала і Володар у ній. Двадцять
досягнень позначають дорогу.
```

**The other five languages** (RU, ES, DE, FR, PL) translate from the English
once you are happy with it. No point translating copy that is still moving.

---

## 4. Tags

Order matters — Steam weights the first few most heavily, and they drive which
discovery queues the game appears in. These are all real Steam tags.

**The five that do the work:**

1. Action Roguelike
2. Hack and Slash
3. Base Building
4. Procedural Generation
5. Dark Fantasy

**Then:**

Third Person · Singleplayer · Medieval · Fantasy · RPG · Exploration ·
Resource Management · Atmospheric · Survival · Combat · Strategy ·
Difficult · Open World · Character Customization

Skip: Souls-like (invites a comparison the combat does not want), Early Access
as a tag (that is a release state, not a tag), and anything about multiplayer.

**Genres** (separate field from tags): Action, RPG, Indie, Strategy.

---

## 5. Languages

Seven, confirmed from `LocalizationManager.Add7` — `en, uk, ru, es, de, fr, pl`.

| Language | Interface | Full audio | Subtitles |
|---|---|---|---|
| English | ✔ | — | ✔ |
| Ukrainian | ✔ | — | ✔ |
| Russian | ✔ | — | ✔ |
| Spanish - Spain | ✔ | — | ✔ |
| German | ✔ | — | ✔ |
| French | ✔ | — | ✔ |
| Polish | ✔ | — | ✔ |

Leave **Full audio** empty everywhere — there is no voice acting, so ticking it
is a claim you cannot support and a refund reason.

---

## 6. System requirements — **MEASURE**

Unity 6.3 on URP 17.3, with procedural terrain generation, volumetric fog and a
900 m far plane. The numbers below are a reasoned starting point, **not a
measurement**. Before the page goes live, run the game on the oldest machine you
can find and correct them — overstated requirements lose sales, understated ones
earn refunds and bad reviews.

### Minimum

| | |
|---|---|
| OS | Windows 10 64-bit |
| Processor | Intel Core i5-8400 / AMD Ryzen 5 2600 |
| Memory | 8 GB RAM |
| Graphics | NVIDIA GTX 1060 6 GB / AMD RX 580 8 GB |
| DirectX | Version 12 |
| Storage | *fill in from the actual build size* |
| Additional | SSD recommended — the world generates on entry |

### Recommended

| | |
|---|---|
| OS | Windows 11 64-bit |
| Processor | Intel Core i5-12400F / AMD Ryzen 5 5600 |
| Memory | 16 GB RAM |
| Graphics | NVIDIA RTX 3060 / AMD RX 6600 XT |
| DirectX | Version 12 |
| Storage | SSD |

What to actually measure: frame time in a generated region at 1080p on the
lowest and highest quality presets, peak RAM during generation, and the final
build folder size.

---

## 7. Controller support — **VERIFY**

`InputCompat.cs` and `GamepadGlyphs.cs` exist, so gamepad input is at least
partly wired. Before ticking anything on the page, play one full region on a
pad, including every menu, the shop, the region map and the barracks.

Steam's options are *Full controller support*, *Partial* and none. Tick **Full**
only if every menu is reachable without a mouse. Otherwise **Partial** — it is
not a weaker claim, it is an honest one, and it is the difference between a
review that says "controller works" and one that says "controller broken".

---

## 8. Graphical assets

Valve moved to 2x uploads; the smaller numbers still quoted around the internet
are the legacy 1x set.

| Asset | Upload size | Notes |
|---|---|---|
| Header capsule | 920 × 430 | The one people actually see. Logo must read at half this size |
| Small capsule | 462 × 174 | Shrinks to as little as 120 × 45 in search — logo only, no scene |
| Main capsule | 1232 × 706 | Top of the store page and the front-page carousels |
| Vertical capsule | 748 × 896 | Seasonal sales and some browse pages |
| Page background | 1438 × 810 | Optional; Steam blurs it heavily |
| Library capsule | 600 × 900 | Portrait, in the player's own library |
| Library header | 920 × 430 | |
| Library hero | 3840 × 1240 | Wide banner behind the library page. Keep the centre clear — the logo sits on it |
| Library logo | up to 1280 × 720 | **PNG with transparency.** Renders over the hero |
| Community icon | 184 × 184 | |
| Client icon | 32 × 32 | |

Confirm each against the Graphical Assets page in Steamworks when it unlocks —
Valve changes these, and this table is from a third party, not from Valve.

**The one rule that matters more than the sizes:** the small capsule is shown at
120 × 45 in search results. Anything that is not the logo disappears at that
size. Design the small capsule as a logo on a colour, not as a shrunken header.

---

## 9. Screenshots and trailer

Ten screenshots, all taken. Order for the page:

1. The castle in the fog — the strongest single image
2. Horde fight — this is what the game *is*
3. The camp at evening — the meta layer, in one picture
4. Totem and its anchors — the loop's objective, HUD visible
5. Tundra vista — biome variety
6. A reliquary at first light — what is worth finding
7. Region map — the campaign
8. Barracks — mercenaries
9. Shop — gear
10. Boss

Trailers: upload **MP4 and WebM** of both cuts. The cinematic trailer goes
first, gameplay second.

Thumbnail: Steam auto-generates one and you can replace it with your own
1920 × 1080 JPG or PNG — but Valve's rule is that it must be a frame from the
video, so the custom cover has to appear in the cut itself.

---

## 10. Order of operations

1. Identity verification finishes → onboarding completes → the app can be created
2. Pay the Steam Direct fee for the app
3. Store page: name, description, tags, languages, requirements
4. Upload capsules — the long pole, start them now
5. Screenshots and both trailers
6. Set the release date (or "coming soon")
7. Submit for review — Valve takes a few business days
8. After approval the page can go live; Steam requires two weeks between the
   page going live and release, so wishlists have time to gather

Steps 3–6 can all be prepared offline while verification runs. Step 4 is the one
that will actually delay you.
