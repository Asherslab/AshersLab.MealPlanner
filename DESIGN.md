---
name: AshersLab Meal Planner
description: The household's week as the planner pad on the fridge door, in the kitchen bench's colours.
colors:
  bottle: "#2F5D50"
  bottle-deep: "#264C41"
  laminate: "#DDE9DF"
  enamel: "#FBFBF6"
  ink: "#1D2B27"
  ink-soft: "#4E5F59"
  chrome: "#B7C1BD"
  rule: "#D6DED9"
  tomato: "#D9533C"
  tomato-ink: "#B8412C"
  butter: "#F2C14E"
  sky: "#7FB6CF"
  sage: "#6FA27A"
  info-blue: "#2F6E8A"
  success-green: "#3F7A4E"
typography:
  display:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.625rem"
    fontWeight: 700
    lineHeight: 1.1
    letterSpacing: "-0.01em"
  headline:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.75rem"
    fontWeight: 700
    lineHeight: 1
    letterSpacing: "-0.02em"
    fontFeature: "tnum"
  marker:
    fontFamily: "Kalam, Atkinson Hyperlegible, cursive"
    fontSize: "1.375rem"
    fontWeight: 700
    lineHeight: 1.15
  title:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 700
    lineHeight: 1.3
  body:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 700
    lineHeight: 1.25
  caption:
    fontFamily: "Atkinson Hyperlegible, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.4
rounded:
  pad-top: "4px"
  tick: "6px"
  entry: "8px"
  control: "12px"
  pad: "14px"
  pill: "15px"
  magnet: "50%"
spacing:
  xs: "4px"
  sm: "6px"
  md: "10px"
  lg: "14px"
  xl: "18px"
components:
  pad-sheet:
    backgroundColor: "{colors.enamel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.pad}"
  pad-head:
    backgroundColor: "{colors.bottle}"
    textColor: "{colors.enamel}"
    typography: "{typography.display}"
    rounded: "{rounded.pad-top}"
    padding: "16px 18px 14px"
  pad-row:
    backgroundColor: "{colors.enamel}"
    textColor: "{colors.ink}"
    height: "64px"
  pad-label:
    textColor: "{colors.bottle}"
    typography: "{typography.label}"
    width: "5.5rem"
    padding: "12px 10px 12px 14px"
  marker-entry:
    textColor: "{colors.ink}"
    typography: "{typography.marker}"
  week-tile:
    backgroundColor: "{colors.enamel}"
    textColor: "{colors.ink}"
    typography: "{typography.headline}"
    rounded: "{rounded.pad}"
    padding: "14px 16px"
  magnet:
    rounded: "{rounded.magnet}"
    size: "18px"
  button-primary:
    backgroundColor: "{colors.bottle}"
    textColor: "{colors.enamel}"
    rounded: "{rounded.control}"
  button-on-band:
    textColor: "{colors.enamel}"
    rounded: "{rounded.control}"
  app-bar:
    backgroundColor: "{colors.bottle}"
    textColor: "{colors.enamel}"
    height: "56px"
  bottom-nav-tab:
    backgroundColor: "{colors.enamel}"
    textColor: "{colors.ink-soft}"
    height: "58px"
  bottom-nav-tab-active:
    textColor: "{colors.tomato-ink}"
    rounded: "{rounded.pill}"
  shop-tick:
    backgroundColor: "{colors.enamel}"
    textColor: "{colors.bottle}"
    rounded: "{rounded.tick}"
    size: "26px"
  shop-tick-bought:
    backgroundColor: "{colors.bottle}"
    textColor: "{colors.enamel}"
---

# Design System: AshersLab Meal Planner

## Overview

**Creative North Star: "The Fridge-Door Pad"**

The week is the planner pad stuck to the fridge: a printed grid the household writes in, with magnets marking who. Every screen is one or more sheets of that pad lying on a sage laminate bench. The sheet's printed parts (the head band, the slot labels, the rules) are bottle-green ink; what the household wrote (meal names, extra shopping lines) is in a marker hand. Done things are struck through in tomato marker and stay where they were.

The world refuses the category default of identical grey cards in a list. Content sits on ruled paper, divided by hairlines, not boxed. Density is phone-first and thumb-sized: rows are at least 64px tall, controls at least 44px, and on a wide screen a day or a list keeps a phone-ish 640px column, centred, while only the week grid spreads out.

The app is light only; it is used in a lit kitchen and a supermarket aisle. Controls stay standard MudBlazor components, re-themed into this world through `Design/AppTheme.cs`; the pad is never costumed with a fake paper "interface".

**Key Characteristics:**
- White-enamel sheets on a sage laminate ground, with a perforated tear along the top sheet.
- A bottle-green printed head band on each sheet; slot labels ruled down the left in small green print.
- Meal names in Kalam marker, everything else in Atkinson Hyperlegible.
- Round person magnets with a bottom lip mark who an entry is for, only when it is not everyone.
- Struck, not removed: ticked and skipped things keep their place under a tomato line.
- Fonts are self-hosted in `wwwroot/fonts` because the app works offline.

## Colors

A muted kitchen-bench palette: two greens and two whites carry the page; the canister set (tomato, butter, sky, sage) does the marker jobs.

### Primary
- **Bottle Green** (bottle): the pad's printed ink. App bar, sheet head bands, slot and category labels, the shop tick box, focus outlines, the primary filled button, link text on a sheet. It is MudBlazor's `Primary`.
- **Bottle Deep** (bottle-deep): a bottle-green control pressed or hovered where MudBlazor does not do it for us (the
  file-pick labels on Receipts and Import). Never a surface.

### Secondary
- **Tomato** (tomato): the marker colour. The strike-through line, the circle around today's date, the person magnet it belongs to. Used as a fill or a 2px stroke, never as body text (3.85:1 on enamel).
- **Tomato Ink** (tomato-ink): tomato darkened for text. The active tab and active drawer link, "Today" in the week grid, attention text ("4 unpriced" totals, "only 1 of 3 serves", errors). It is MudBlazor's `Secondary`, `Warning` and `Error` alike: attention is one colour app-wide.

### Tertiary
- **Butter, Sky, Sage** (butter, sky, sage) with tomato: the canister set, used for person magnets. Butter at 55% is also the text selection colour.
- **Info Blue** and **Success Green** (info-blue, success-green): MudBlazor-only roles for alerts and snackbars. They have no CSS custom property and appear on no pad surface.

### Neutral
- **Laminate** (laminate): the page ground behind every sheet, and the "have it" fill on a shop line.
- **Enamel** (enamel): the pad paper; sheets, week tiles, bottom bar, drawer, dialogs, and the text colour on bottle green.
- **Ink** (ink): what the household wrote and all primary text.
- **Ink Soft** (ink-soft): secondary text, metadata under an entry, struck text, inactive tabs, the unsent dashed edge.
- **Chrome** (chrome): structural hairlines at the edges of chrome: input outlines, week-tile border, bottom-bar top edge, drawer edge, chevrons at rest.
- **Rule** (rule): the lighter hairline that rules rows and columns inside a sheet.

Tints are bottle at 5-7% for row hover and today's column, bottle at 10% for press, tomato at 14% for the active tab's pill and the active drawer link. Enamel at 62% outlines a button on the head band, at 86% sets the band's subtitle.

### Named Rules
**The Two Places Rule.** Every colour lives in both `wwwroot/css/app.css` (custom properties on `:root`) and `Design/AppTheme.cs` (the MudBlazor palette). Change one, change both.

**The One Attention Colour Rule.** Anything that needs seeing to (unpriced, short of serves, running low, an error) reads in tomato ink. There is no amber warning and no separate error red.

**The Struck-in-Tomato Rule.** A done or skipped thing is struck through with a 2px tomato line and its text drops to ink soft. It is never removed, hidden or greyed into a separate colour.

## Typography

**Display Font:** Atkinson Hyperlegible (with system-ui, -apple-system, Segoe UI, sans-serif), weights 400 and 700, self-hosted.
**Marker Font:** Kalam (with Atkinson Hyperlegible, cursive), weight 700, self-hosted.

**Character:** A plain, very legible print face for everything the pad prints, and a quick felt-tip hand for only the things the household wrote. The contrast between the two is the pad's whole story.

### Hierarchy
- **Display** (700, 1.625rem, 1.1, -0.01em): a sheet's head band title: the day, the week, the date range. A list's group band drops to 1.0625rem; Shop's head drops to 1.375rem.
- **Headline** (700, 1.75rem, 1, -0.02em, tabular figures): the big number in a week tile.
- **Marker** (Kalam 700, 1.375rem, 1.15): meal names and extra shopping lines. 1.25rem inside the desktop week grid's narrow cells.
- **Title** (700, 1.0625rem, 1.3): a list row's name (a More destination, a shop line).
- **Body** (400, 1rem, 1.45): running text, notes on a sheet. Asides beside a sheet run at 0.9375rem.
- **Label** (700, 0.8125rem, 1.25, bottle green): printed slot labels, store categories, small headings in a sheet. Sentence case, never uppercase. Tab labels are 0.75rem 700.
- **Caption** (400, 0.8125rem, 1.4, ink soft): the meta line under an entry ("2 unpriced", "makes 4 serves"). 0.75rem in the week grid.

MudBlazor's H1-H6 scale (2.5rem down to 1.125rem, all 700) and its Button style (0.9375rem 700, no text transform) carry the same face.

### Named Rules
**The Marker Rule.** Kalam only for names the household wrote, and only at 20px and up. Never for labels, numbers, prices or controls.

**The Sentence-Case Rule.** No uppercase labels, overlines or buttons anywhere; MudBlazor's button and overline transforms are switched off in the theme. "Grams (g)", not "GRAMS (G)".

**The Tabular Figures Rule.** Tables, `.num`, week-tile numbers, prices and day numbers use tabular figures so money and dates line up.

## Layout

A phone is the primary device. Below 960px (MudBlazor's Md breakpoint) the shell is a 56px bottle-green app bar, the page, and a fixed bottom bar with five tabs (Today, Plan, Shop, Meals, More); a page reached through More or Settings gets a back arrow in the app bar instead of a drawer. From 960px up the bottom bar disappears and an enamel drawer sits open beside the page with the four daily pages, a Kitchen group and Settings. Which shell shows is plain CSS, so the first paint is already right.

Content sits in a MudBlazor medium container. A day (Today), a list (Shop, Meals) and a group of destinations (More) keep a centred column at most 640px wide on every screen. Only the desktop week grid takes the page's width, with a 760px minimum and sideways scrolling rather than squeezing a day; on a phone the week becomes a seven-day strip above one day's ruled slots.

A sheet's interior is a two-column grid: a 5.5rem printed label column ruled off on its right, then the entry. Shop lines swap the label column for a 3.5rem tick-box column. Rows are at least 64px. Sheets stack with 18px of laminate showing between them; week tiles sit 12-16px under the sheet in a two-up grid with a 10px gap.

Spacing runs in small steps (4, 6, 10, 14, 18px). Interior padding is 10-14px vertically and 14-18px horizontally. The page body pads its foot by 56px plus the safe-area inset so nothing hides under the bottom bar.

## Elevation & Depth

Almost flat. Depth comes from paper on laminate: the enamel sheet against the sage ground does the work, and a sheet carries only the pad's slight lift. There is no elevation ramp; MudBlazor app bar and drawer elevation are zero.

### Shadow Vocabulary
- **Pad lift** (`box-shadow: 0 1px 0 rgba(29,43,39,0.10), 0 2px 6px -2px rgba(29,43,39,0.12)`): every pad sheet. The sheet resting on the bench.
- **Lifted sheet** (`box-shadow: 0 1px 0 rgba(29,43,39,0.10), 0 12px 32px -8px rgba(29,43,39,0.30)`): dialogs, a sheet picked up off the pad.
- **Magnet lip** (`box-shadow: inset 0 -2px 0 rgba(0,0,0,0.22), 0 1px 1px rgba(29,43,39,0.25)`): person magnets; the colour swatches in the People editor use the same lip at 3px.

### Named Rules
**The Paper-Not-Cards Rule.** No shadow beyond the pad lift on anything that rests on the page. Depth is enamel on laminate; a hover is a bottle tint, not a lift.

## Shapes

The sheet's silhouette is the signature: square-ish top corners (4px) where it was torn off the block, softly rounded foot (14px), and a perforation along the top edge drawn as laminate-coloured 3px holes every 14px. Only the first sheet on a page is torn; sheets that follow keep a clean top so the perforation never becomes wallpaper.

Inside a sheet everything is ruled with 1px hairlines rather than boxed. Controls (inputs, buttons, selects) take MudBlazor's 12px default radius; tappable rows and entries take an 8px radius on focus and hover tints; the shop tick box is 6px; the bottom bar's active pill is a 15px-radius capsule. Magnets and the today circle are true circles.

## Components

### Pad Sheet
The app's main container, in place of a card.
- **Shape:** 4px top corners, 14px foot, perforated top on the first sheet of a page.
- **Background:** enamel, with the pad lift.
- **Head band:** bottle green, enamel text; title in Display, subtitle 0.875rem at 86% enamel. Anything else the band carries (a stepper, the household's magnets) sits in a right-hand column; a full-width action line can run under it.
- **Rows:** ruled with the rule hairline, first row without a top rule; label column in bottle-green Label type.

### Buttons
- **Shape:** gently rounded (12px).
- **Primary:** bottle green filled, enamel text, 0.9375rem 700 sentence case; at least 44px tall in dialogs and thumb positions, 56px beside a tall input.
- **On a head band:** enamel outline at 62%, enamel text, a 12% enamel tint on hover.
- **Text / icon:** bottle green on enamel; icon buttons are 44px squares where thumbs reach.

### Week Tiles
The week at a glance, from the kitchen bench: two tiles side by side, each a big number over what it counts ("12 to get this week", "$146 this week · 1 unpriced").
- **Style:** enamel, 1px chrome border, 14px radius, 14px 16px padding; the number in Headline, the label in 0.8125rem ink soft.
- **Incomplete cost:** the unpriced count sits right under the number, never the number alone; with nothing priced at all the number is "$?", not "$0.00". On Shop, a "from" prefix and a tomato-ink "+ N unpriced" line mark an incomplete total.
- **Hover:** border turns bottle green.

### Person Magnets
The signature move. Round, coloured, with a darker lip along the bottom.
- **Size:** 18px; 14px beside a name; 34px as a colour choice, the chosen one ringed in a 3px bottle-green outline.
- **Placement:** the household's magnets sit once on a day's or week's head band. An entry carries its own only when it is for some of the household, so a magnet on a row means "not everyone".
- **Colour:** a person's colour is theirs everywhere. A person is shown as a magnet, never as a coloured text background.

### Struck and Unsent
- **Struck:** 2px tomato line-through, text to ink soft, row kept in place.
- **Unsent** (written locally, not yet on the server): a 1.5px dashed ink-soft outline inset 3px with an 8px radius. Never a new colour.

### Shop Lines
A ruled list line, worked with a thumb in an aisle.
- **Tick box:** 26px, 2px bottle-green border, 6px radius, in a ruled 3.5rem margin column. Bought fills bottle green with an enamel tick; "have it" fills laminate.
- **Line:** title-weight name (or marker for an extra item), detail in 0.875rem ink soft, cost right-aligned in tabular 700; an unpriced cost drops to 400 ink soft.
- **Store band:** a bottle-green band (1.375rem 700) opens each store's sheet; categories are bottle-green Label print.

### Inputs / Fields
- **Style:** MudBlazor outlined fields on enamel, 1px chrome outline, 12px radius, at least 48px tall. Native `<select>` and date inputs are dressed to match so a phone opens its own picker.
- **Focus:** a 2px bottle-green outline. The caret is bottle green.

### Navigation
- **App bar:** flat bottle green, 56px, the page or app name in 700 enamel, the sync chip on the right.
- **Bottom bar (phone):** enamel with a chrome top edge, five equal tabs at least 58px tall, icon over a 0.75rem 700 label in ink soft. The active tab turns tomato ink and its icon sits in a 14% tomato pill (52 by 30px); the pill fades in over 160ms `cubic-bezier(0.22, 1, 0.36, 1)`, and not at all under reduced motion.
- **Drawer (desktop):** enamel, chrome right edge, links with a 10px radius. The current page is tomato ink on a 14% tomato tint, 700, with MudBlazor's side stripe removed.

### Destination Rows
Ruled links on a sheet (More, Settings): an icon, a title over a 0.875rem ink-soft description, a chrome chevron that turns bottle green on hover. At least 68px tall; hover tints bottle at 5%, press at 10%.

### Dialogs
A sheet lifted off the pad: enamel, 14px radius, the lifted-sheet shadow, title in bottle green 700, sentence-case actions at least 44px tall.

## Do's and Don'ts

### Do:
- **Do** put a day, a list or a set of entries on a pad sheet (enamel on laminate, bottle-green head band, ruled rows).
- **Do** keep a day or list to a centred 640px column on wide screens.
- **Do** show a cost that cannot be computed as incomplete: "from $X + N unpriced", or "$?" when nothing is priced. Never as $0.00.
- **Do** strike done and skipped things through in tomato and leave them where they were.
- **Do** mark unsent edits with the dashed ink-soft edge.
- **Do** change `wwwroot/css/app.css` and `Design/AppTheme.cs` together.
- **Do** self-host any new font in `wwwroot/fonts` with its licence beside it; the app works offline.
- **Do** keep touch targets at least 44px and list rows at least 64px.

### Don't:
- **Don't** use identical grey cards in a list; rule rows on a sheet instead.
- **Don't** set labels, numbers, prices or controls in Kalam, or Kalam below 20px.
- **Don't** add drop shadows beyond the pad lift to anything resting on the page.
- **Don't** use plain tomato for text; use tomato ink.
- **Don't** show a person as a coloured text background; show their magnet.
- **Don't** repeat the household's magnets on every row; they sit once on the band, and a row carries magnets only when it is not for everyone.
- **Don't** uppercase labels, overlines or buttons.
- **Don't** add a dark theme without a decision; the world is light only.
