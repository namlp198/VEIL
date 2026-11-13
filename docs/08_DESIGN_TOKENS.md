# 08 — Theme, Style and Design Tokens

Status: implemented for the Windows overlay and Window Selector; Avalonia resources generated and loaded in a headless check, not yet used by a real application window (Phase 6). Extends `04_UI_UX.md` section 6 ("Visual tokens"), which gave directions only.

## 1. Where the style comes from

The six images in `resources/images/` are mood references (third-party pages: HUD/sci-fi interface art, a "Command Tab" switcher concept, a neon synthwave admin screen, profile cards, a Pomodoro timer, a purple battle-station). **They are inspiration only: the files are not shipped or redistributed, and nothing was traced from them.** Colours were sampled programmatically from the artwork area of each image (dominant buckets and the most saturated bright accents), then adjusted for contrast.

| Reference | What it contributed |
|---|---|
| Main Focus Screen | Charcoal/blue-grey HUD panels (`#1D232F`, `#202633`), violet glow (`#7A31D6`, `#9235EC`, `#AB46EE`), concentric rings with ticks, tiny technical labels. Product note: "calm, few elements". |
| Window Selector | Deep navy base (`#050913`), cyan accents (`#31F1F4`, `#48D6E8`), layered rounded cards. The in-doc "VEIL / WINDOW CONTROL" list (title, subtitle, `ALLOWED`/`HIDDEN` pills) defined the row layout. |
| Settings / Admin | Near-black violet (`#13051A`, `#1B0929`), magenta neon (`#ED75F2`, `#D463DC`), cyan frame lines, perspective grid. |
| Workspace Profiles | Cool blue/teal and violet tints in rounded 12 px cards with a title and one-line subtitle. |
| Focus Timer | Indigo canvas (`#080817`), lilac ring and button (`#BBA8F4`, `#8971CE`), light lavender digits (`#DDD7F1`), pill tabs. |
| Multi-Monitor | Violet room light (`#9542BD`, `#2C1646`), deep purple shadows. |

## 2. Tokens

Source of truth: `assets/theme/veil-tokens.json` (embedded in `VEIL.Domain` as `VeilTheme.Dark`). Everything else is derived from it.

### Colour

| Token | Value | Role | Origin |
|---|---|---|---|
| `bg.canvas` | `#080817` | Focus Canvas background | sampled (timer) |
| `bg.base` | `#07060D` | App window base | sampled, darkened |
| `bg.surface` | `#100B1A` | Panels, selector body | sampled (multi-monitor) |
| `bg.raised` | `#1B1030` | Inputs, selected rows | sampled (admin) |
| `bg.panel` | `#1D232F` | HUD frames | sampled (main screen) |
| `border.subtle` | `#2E2D46` | Dividers, outlines | sampled |
| `border.strong` | `#4A3A7A` | Panel borders | chosen |
| `border.focus` | `#31F1F4` | Keyboard focus ring | sampled (cyan) |
| `text.primary` | `#E6E1F5` | Body text | sampled, lightened |
| `text.secondary` | `#B3ADCC` | Supporting text | chosen |
| `text.muted` | `#918AAE` | Captions, technical metadata | chosen |
| `text.link` | `#B58CF5` | Links, violet *text* | chosen |
| `text.onAccent` | `#FFFFFF` | Label on violet fills | — |
| `accent.primary` | `#7A31D6` | Button fill | sampled |
| `accent.primaryHover` | `#9235EC` | Hover, HUD ring, glow | sampled |
| `accent.primaryActive` | `#6132A6` | Pressed | sampled |
| `accent.glow` | `#AB46EE` | Glow/shadow tint | sampled |
| `accent.lilac` | `#BBA8F4` | Wordmark, timer ring | sampled |
| `accent.cyan` / `cyanDim` | `#31F1F4` / `#48D6E8` | Focus indicator, ticks, technical text | sampled |
| `accent.magenta` | `#ED75F2` | Secondary neon (admin) | sampled |
| `accent.blue` | `#4A8DBF` | Neutral info accent | sampled |
| `status.success` / `Bg` | `#35D07F` / `#0E2A1D` | Allowed | chosen |
| `status.warning` / `Bg` | `#F2B84B` / `#2E2210` | Minimized, caution | chosen |
| `status.error` / `Bg` | `#FF6B86` / `#34121C` | Errors, denied | chosen |
| `status.info` / `Bg` | `#31F1F4` / `#0B2A33` | Visible | chosen |
| `status.neutralBg` | `#221E30` | Hidden, Not allowed | chosen |

Semantic status colours are **chosen**, not sampled, because the images contain no consistent success/error palette.

### Contrast rules (enforced by unit tests)

| Pair | Ratio on `bg.base` | Rule |
|---|---|---|
| `text.primary` | 15.8 | AAA body text (>= 7) on every background |
| `text.secondary` | 9.4 | AA (>= 4.5) |
| `text.muted` | >= 4.5 on all five backgrounds | AA; was `#8B84A8` (4.49 on `bg.panel`), lightened |
| `text.link` (violet text) | 7.7 | the only violet allowed for text |
| `accent.primaryHover` `#9235EC` | 3.8 | **fills, borders, glow, decoration only**; below 4.5 so never body text |
| white on `accent.primary` | 6.5 | button labels |
| `border.focus` | >= 3 | UI-component threshold |
| pill text on its pill background | >= 4.5 | all five pill kinds |

Status is always written as a word (`Allowed`, `Not allowed`, `Hidden`, `Visible`, `Minimized`), never colour alone (UI/UX section 8).

### Typography

Family `Segoe UI` (Windows), fallback Inter/Roboto; monospace `Cascadia Mono`/Consolas **only for technical metadata** (UI/UX section 6). Sizes (pt → px at 96 dpi): caption 9 → 12, body 10.5 → 14, bodyLarge 12 → 16, title 16 → 21.3, display 28 → 37.3, timer 56 → 74.7. Weights 400 / 500 / 600.

### Spacing, radius, motion, components

- Spacing (dp): 4, 8, 12, 16, 24, 32. Radius: 4, 8, 12, pill. Selector row height 52, pill height 20, focus ring 2.
- Motion: 120 / 200 / 400 ms, `cubic-bezier(0.2, 0, 0, 1)`, a 4 s pulse. Motion tokens must be ignored when the reduced-motion preference is on (`FocusPreferences.ReducedMotion`, UI/UX section 2). Nothing animates in the current build.
- All sizes are device-independent and multiplied by the display scale factor (see `07_TRACEABILITY.md`, DPI section).

## 3. Component styles

- **Focus Canvas:** `bg.canvas` field; a restrained HUD ring (`accent.primaryHover` 2 px with a faint `accent.glow` halo, a `border.strong` ring with 10-degree ticks and cyan quarter ticks, a dotted `border.subtle` outer ring); wordmark `VEIL` in `accent.lilac`; `F O C U S   A C T I V E` in `accent.cyan`; recovery shortcut in `text.muted`. No clock, system gauges or feeds yet (the reference's gauges were deliberately left out: "do not put too much on screen").
- **Window Selector:** `bg.surface` window, `bg.raised` text box, rows 52 dp with a 26 dp number chip (shortcut 1-9), title (semibold), application name (`text.muted`), and two right-aligned pills: policy (`Allowed` green, `Not allowed` neutral) and state (`Visible` cyan, `Hidden` neutral, `Minimized` amber). Selected row: `bg.raised` with a 3 dp bar, cyan when the list has keyboard focus, violet otherwise.
- **Buttons:** pill-shaped, violet fill, white semibold label, hover/pressed from the primary scale, 2 px cyan focus ring.
- **Inputs:** `bg.raised`, `border.subtle`, cyan caret and focus border.
- **Panels / HUD:** `bg.surface` + 12 dp radius; HUD variant `bg.panel` + `border.strong` + a violet glow shadow.
- **Admin Settings (built):** 272 px `bg.surface` sidebar with the lilac `VEIL` wordmark, cyan `SETTINGS` caption, nav rows with a cyan selection bar and `Planned` pills, a scope pill ("Applies to: Whole machine / This user"), 12 dp `rule-row` cards, mono cyan technical text for paths and identifiers, violet pill buttons, a pinned action bar with success/error/info status text, a warning card for the "not a security boundary" statement, and a `hud` card with a violet glow for the access-denied state.
- **Not yet styled** (Phase 6 screens): Workspace Profile cards, Focus Timer ring, tab pills. Their tokens exist (lilac ring, 12 dp cards, `timer` font size) but no view uses them.

## 4. Using the tokens

| Consumer | How |
|---|---|
| C# | `VeilTheme.Dark.Color("accent.cyan")`, `.Spacing`, `.FontSizesPt`, `ThemeColor.ContrastWith` |
| Windows adapter | `Palette` (internal): COLORREFs, cached brushes, DPI-scaled fonts; used by `WindowsOverlayHost` and `WindowsSelectorView` |
| Avalonia | `assets/theme/VeilTokens.axaml` (resources: `Color.*`, `Brush.*`, `Space.*`, `Pad.*`, `Radius.*`, `Font.*`, `Motion.*`, `Component.*`) and `VeilStyles.axaml` (classes `panel`, `hud`, `pill` + `allowed`/`visible`/`minimized`/`error`, `selector-row`, `title`, `display`, `caption`, `technical`, `secondary`) |
| Changing a value | Edit `veil-tokens.json`, run `python tools/gen_axaml.py`; `ThemeTests` fail if the AXAML drifts |

## 5. Verification and limits

- 46 theme unit tests: colour parsing, contrast thresholds above, required tokens, malformed/oversized theme rejection, AXAML-to-JSON sync (214 tests total).
- `tools/AxamlCheck` loads both AXAML files into a headless Avalonia 12.1.5 app and checks resolved resources and class-based styles (12 checks, all pass). It found a real bug the compiler did not: a `double` assigned to `Padding` (now `Pad.*` thickness resources). It is **not** part of `VEIL.slnx` or CI (it needs the Avalonia packages); run `dotnet run --project tools/AxamlCheck`.
- Windows rendering checked by screenshot on two displays at 100% and at a forced 2x scale, including every pill kind (`Allowed`, `Not allowed`, `Hidden`, `Visible`, `Minimized`) and both selection-bar colours (violet with the text box focused, cyan with the list focused). Not checked: real high-DPI hardware, Windows 10 (the dark caption and rounded corners use Windows 11 DWM attributes and are ignored elsewhere), a light or high-contrast system theme, and a colour-blindness simulation.
- Admin Settings was rendered headless with Skia (`tools/AdminShots`) and reviewed by screenshot; it is not yet launched by the host.
- Known gaps: the selector checkbox glyph and the caption close button are drawn by Windows and stay light/red; no light theme exists; the "windows count / displays / clock" status items of UI/UX 3.1 are not rendered; the owner-drawn list does not expose its pills to screen readers (the row's plain-text form is the accessible name, which includes the policy and state words).
