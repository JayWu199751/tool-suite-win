# Apple (España) — Style Reference
> black theater with luminous hardware

**Theme:** dark

Source measurements are normalized; roles and recommendations are interpreted. Font summary lists are independent, not paired by position. HTML examples are reconstructions, not source components.

_Palette calibrated to ClipFlow HUD's dark token block (`wpf-rewrite-kit/03-prototype/assets/theme.css`, `:root`). Every color value below is one of its tokens; typography, spacing, and shape values are unchanged._

Apple's MacBook Pro page operates as a cinematic dark canvas: near-black backgrounds (#0B0B0E) swallow everything except product photography, oversized type, and one precise indigo accent. The visual grammar is restraint at extreme scale — headlines up to 80px in SF Pro Display weight 600 with negative tracking, body copy at 17px in SF Pro Text, and generous vertical breathing between sections. Color is rationed: the low-saturation indigo (#7B77E0) appears only as tint, stroke, and glyph — a 13% wash on selected surfaces and search hits, a 45% stroke on selected borders and focus rings, never a solid fill; action labels and links adopt the lighter indigo (#A7A4F0) as functional punctuation; everything else is the neutral ladder (#F2F2F5 → #A0A0AC → #7E7E8A → #55555F) on near-black. Surfaces are defined by a 28px corner radius and zero shadows — elevation is achieved through photographic content and tonal contrast, not depth effects. The page reads like a product film: dark voids, luminous hardware, typographic calm.

## Tokens — Colors

| Name | Value | Token | Role |
|------|-------|-------|------|
| Canvas | `#0B0B0E` | `--bg-app` | Window canvas, search header and footer bands, full-page surface |
| Card | `#141419` | `--bg-card` | Clipboard cards, promo strip, section dividers |
| Input | `#15151A` | `--bg-input` | Search well, key-name chips, toast body |
| Active Overlay | `rgba(255,255,255,0.09)` | `--bg-active` | Hover fill on icon buttons and text actions |
| Selected Fill | `rgba(123,119,224,0.13)` | `--bg-selected` | Selected card fill; tinted primary action fill |
| Hairline | `rgba(255,255,255,0.06)` | `--border-subtle` | Card borders, dividers, thumbnail outline |
| Strong Border | `rgba(255,255,255,0.12)` | `--border-strong` | Search well, key chips, toast outline, card hover border |
| Selected Border | `rgba(123,119,224,0.45)` | `--border-selected` | Selected card border, focus stroke |
| Window Ring | `#757575` | `--window-ring` | 2px window frame — identical in both themes |
| Text Primary | `#F2F2F5` | `--text-primary` | Headings, body copy, input value |
| Text Secondary | `#A0A0AC` | `--text-secondary` | Meta lines, secondary labels, icon glyphs |
| Text Tertiary | `#7E7E8A` | `--text-tertiary` | Timestamps, hints, placeholders, footer labels |
| Text Disabled | `#55555F` | `--text-disabled` | Unavailable controls and inert labels |
| Accent | `#7B77E0` | `--accent` | Selection tint base, pin, focus ring, hit highlight |
| Accent Text | `#A7A4F0` | `--accent-text` | Action labels, inline links, focused action border |
| Accent Soft | `rgba(123,119,224,0.13)` | `--accent-soft` | Search-hit highlight fill under text runs |
| Ring Soft | `rgba(123,119,224,0.12)` | `--ring-soft` | 3px soft halo outside the focus stroke |
| Success | `#57C08A` | `--success` | Copy confirmation, toast check glyph |
| Error | `#E05A52` | `--error` | Deletion confirmation, toast cross glyph, inline error |
| Popover | `rgba(26,26,33,0.95)` | `--pop-bg` | Floating action bar over content |
| Popover Border | `rgba(255,255,255,0.1)` | `--pop-border` | Hairline on floating surfaces |
| Selection Text | `#FFFFFF` | `--selection-text` | Text-selection foreground only — never body text |
| Checker | `rgba(255,255,255,0.03)` | `--checker` | Image thumbnail checkerboard light squares |
| Thumb Gradient | `linear-gradient(135deg, #2A2A40, #1A1A24)` | `--gradient-thumb` | Image thumbnail placeholder base — the only gradient |

## Tokens — Typography

### SF Pro Display — Display and headline typography — hero h1, section openers, product names. Weight 600 with aggressive negative tracking (-0.015em at 80px down to -0.003em) gives the type a compressed, architectural presence. The whisper-thin tracking on the largest sizes is signature: 80px headlines feel monolithic rather than decorative. · `--font-sf-pro-display`
- **Substitute:** Inter
- **Weights:** 600
- **Sizes:** 40px, 56px, 64px, 80px
- **Line height:** 1.10, 1.07, 1.06, 1.05
- **Letter spacing:** -0.015em at 80px, -0.009em at 64px, -0.005em at 56px, 0 at 40px
- **OpenType features:** `"numr" on`
- **Role:** Display and headline typography — hero h1, section openers, product names. Weight 600 with aggressive negative tracking (-0.015em at 80px down to -0.003em) gives the type a compressed, architectural presence. The whisper-thin tracking on the largest sizes is signature: 80px headlines feel monolithic rather than decorative.

### SF Pro Display — Subheadings, card titles, eyebrow labels — transitions from display to functional type. Tracking shifts to slightly positive values (+0.007em at 28px, +0.012em at 19px) as size decreases, compensating for optical tightness in shorter strings. · `--font-sf-pro-display`
- **Substitute:** Inter
- **Weights:** 600
- **Sizes:** 19px, 21px, 28px, 32px
- **Line height:** 1.21, 1.19, 1.14, 1.13
- **Letter spacing:** 0.004em at 32px, 0.007em at 28px, 0.011em at 21px, 0.012em at 19px
- **OpenType features:** `"numr" on`
- **Role:** Subheadings, card titles, eyebrow labels — transitions from display to functional type. Tracking shifts to slightly positive values (+0.007em at 28px, +0.012em at 19px) as size decreases, compensating for optical tightness in shorter strings.

### SF Pro Text — Body copy and primary paragraph text — the 17px/1.47 combination with -0.022em tracking is the workhorse of the system. Used everywhere from hero subtext to footer disclaimers. · `--font-sf-pro-text`
- **Substitute:** Inter
- **Weights:** 400
- **Sizes:** 17px
- **Line height:** 1.47
- **Letter spacing:** -0.022em
- **OpenType features:** `"numr"`
- **Role:** Body copy and primary paragraph text — the 17px/1.47 combination with -0.022em tracking is the workhorse of the system. Used everywhere from hero subtext to footer disclaimers.

### SF Pro Text — Small UI text — nav links, footnotes, legal copy, micro-labels. Tracking goes more negative (-0.037em at 10px) at the smallest sizes to maintain readability despite size. · `--font-sf-pro-text`
- **Substitute:** Inter
- **Weights:** 400
- **Sizes:** 12px, 14px, 20px
- **Line height:** 1.33, 1.29, 1.33
- **Letter spacing:** -0.010em at 12px, -0.006em at 20px
- **OpenType features:** `"numr"`
- **Role:** Small UI text — nav links, footnotes, legal copy, micro-labels. Tracking goes more negative (-0.037em at 10px) at the smallest sizes to maintain readability despite size.

### SF Pro Text — Logo and brand mark rendering size in the global nav — weight 400 not 600, because the wordmark itself carries the form. Line-height 1.00 keeps it visually compact. · `--font-sf-pro-text`
- **Substitute:** Inter
- **Weights:** 400
- **Sizes:** 44px
- **Line height:** 1.00
- **Letter spacing:** -0.0370em, -0.0270em, -0.0220em, -0.0190em, -0.0160em, -0.0100em, -0.0060em, -0.0030em
- **OpenType features:** `"numr"`
- **Role:** Logo and brand mark rendering size in the global nav — weight 400 not 600, because the wordmark itself carries the form. Line-height 1.00 keeps it visually compact.

### Type Scale

| Role | Family | Weight | Size | Line Height | Letter Spacing | Token |
|------|--------|--------|------|-------------|----------------|-------|
| caption | — | — | 12px | 1.33 | -0.12px | `--text-caption` |
| body-sm | — | — | 17px | 1.47 | -0.374px | `--text-body-sm` |
| body | — | — | 21px | 1.19 | 0.231px | `--text-body` |
| subheading | — | — | 28px | 1.14 | 0.196px | `--text-subheading` |
| heading-sm | — | — | 40px | 1.1 | 0px | `--text-heading-sm` |
| heading | — | — | 56px | 1.07 | -0.28px | `--text-heading` |
| heading-lg | — | — | 64px | 1.06 | -0.576px | `--text-heading-lg` |
| display | — | — | 80px | 1.05 | -1.2px | `--text-display` |

## Tokens — Spacing & Shapes

**Base unit:** 4px

**Density:** comfortable

### Spacing Scale

| Name | Value | Token |
|------|-------|-------|
| 4 | 4px | `--spacing-4` |
| 8 | 8px | `--spacing-8` |
| 12 | 12px | `--spacing-12` |
| 16 | 16px | `--spacing-16` |
| 20 | 20px | `--spacing-20` |
| 24 | 24px | `--spacing-24` |
| 28 | 28px | `--spacing-28` |
| 32 | 32px | `--spacing-32` |
| 40 | 40px | `--spacing-40` |
| 44 | 44px | `--spacing-44` |
| 48 | 48px | `--spacing-48` |
| 60 | 60px | `--spacing-60` |
| 80 | 80px | `--spacing-80` |
| 104 | 104px | `--spacing-104` |
| 144 | 144px | `--spacing-144` |
| 208 | 208px | `--spacing-208` |

### Border Radius

| Element | Value |
|---------|-------|
| nav | 980px |
| cards | 28px |
| links | 10px |
| badges | 20% |
| inputs | 210px |
| buttons | 9999px |

### Layout

- **Page max-width:** 1440px
- **Section gap:** 80px
- **Card padding:** 28px
- **Element gap:** 10px

## Components

### Primary CTA Pill Button
**Role:** The single dominant purchase action on the page

Background rgba(123,119,224,0.13) (`--bg-selected`), text #A7A4F0 (`--accent-text`), border 1px solid rgba(123,119,224,0.45) (`--border-selected`), border-radius 9999px, padding 11px 22px. This is the only chromatic action in the system — and it is tinted, never filled: the accent appears as a 13% wash plus a 45% stroke. Weight 400 at 17px SF Pro Text, letter-spacing -0.022em. Used exclusively for "Comprar" (Buy). Hover advances the border to #7B77E0 (`--accent`); the fill does not change.

### Ghost Pill Button
**Role:** Secondary actions and navigation controls

Background transparent, text #A0A0AC (`--text-secondary`), border-radius 9999px, no visible border. Padding 11px 22px. Weight 400 at 17px. Hover fills with rgba(255,255,255,0.09) (`--bg-active`) and raises the text to #F2F2F5 (`--text-primary`). Used for "Ver el vídeo" (Watch the video) and feature-explorer controls. Borderless by default — relies on text-only affordance.

### Floating Popover Bar
**Role:** Price + Buy composite floating on hero

Background rgba(26,26,33,0.95) (`--pop-bg`), border 1px solid rgba(255,255,255,0.1) (`--pop-border`), price text #A0A0AC (`--text-secondary`), value text #F2F2F5 (`--text-primary`), border-radius 36px (or 980px for extreme rounding), padding 11px 22px. This is the near-opaque popover anchored at hero bottom-right. The 95% near-black fill and the 10% white hairline separate it from the canvas — no frost, no glow, no shadow.

### Text Link with Arrow
**Role:** Inline learn-more navigation

Color #A7A4F0 (`--accent-text`), weight 400 at 17px, no underline by default, includes a ◊ or → glyph. The arrow glyph is part of the link, not a separate element. Tracking matches body copy at -0.022em. Hover: underline; the color stays #A7A4F0.

### Dark Card Surface
**Role:** Feature card on dark backgrounds

Background #141419 (`--bg-card`), border 1px solid rgba(255,255,255,0.06) (`--border-subtle`), border-radius 28px, no shadow, padding 28px. Hover border rises to rgba(255,255,255,0.12) (`--border-strong`). Used in the "Lo principal." feature grid. The 28px radius is the system's signature softness — applied to every card-like container including product cards.

### Selected Card Surface
**Role:** Selected or focused feature card

Background rgba(123,119,224,0.13) (`--bg-selected`), border 1px solid rgba(123,119,224,0.45) (`--border-selected`), border-radius 28px, no shadow, padding 28px. Text stays #F2F2F5 (`--text-primary`). This is the only selected treatment in the system: a 13% indigo tint plus a 45% indigo stroke. The radius stays constant at 28px in every state.

### Section Header
**Role:** Major section opener (Lo principal., Más de cerca., etc.)

Left-aligned heading at 40px SF Pro Display weight 600, letter-spacing 0, color #F2F2F5 (`--text-primary`). Optional right-aligned "Ver el vídeo" link in #A7A4F0 (`--accent-text`). Vertical padding 40-60px above the section content.

### Chip Badge with Neutral Gradient
**Role:** Product family identifier (M5, M5 Pro, M5 Max)

Square badge approximately 200px, border-radius 20% (creating subtle rounded square), background uses the thumbnail gradient linear-gradient(135deg, #2A2A40, #1A1A24), border 1px solid rgba(255,255,255,0.06) (`--border-subtle`), with the product glyph and chip name in #F2F2F5 (`--text-primary`). The gradient is a near-black tonal wash, not a color spectrum — it is the only gradient the system permits.

### Nav Link
**Role:** Top-level navigation item

Color #A0A0AC (`--text-secondary`), weight 400 at 12px SF Pro Text, letter-spacing -0.010em, no underline. Padding 10px. Hover raises to #F2F2F5 (`--text-primary`). Secondary text stays one step below primary — full-white nav text would feel aggressive against the near-black bar.

### Search Input (Global)
**Role:** Top-right search field

Background #15151A (`--bg-input`), text #F2F2F5 (`--text-primary`), placeholder #7E7E8A (`--text-tertiary`), border 1px solid rgba(255,255,255,0.12) (`--border-strong`), border-radius 210px (extreme pill shape), padding 0 22px left and 0 42px right (icon gutter). Focus: border rgba(123,119,224,0.45) (`--border-selected`) with a 3px rgba(123,119,224,0.12) (`--ring-soft`) halo. The 210px radius on a 36px-tall input creates a pure pill silhouette.

### Eyebrow Product Label
**Role:** Pre-headline product identifier

Weight 400 at 17px SF Pro Text, color #F2F2F5 (`--text-primary`), letter-spacing -0.022em. Sits directly above the hero h1 ("MacBook Pro" above "La velocidad viene de familia."). The weight 400 at this size creates quiet product identification rather than screaming.

### Finishes Swatch
**Role:** Product color/finish picker item

Circular or rounded square swatches at ~40px, filled from the neutral ladder: #F2F2F5 (`--text-primary`), #A0A0AC (`--text-secondary`), #7E7E8A (`--text-tertiary`), #55555F (`--text-disabled`). No border by default, border-radius 20% or 999px. These are product selectors, not decorative; no off-palette finish colors are permitted.

### Promo Banner Bar
**Role:** Secondary global message strip below nav

Background #141419 (`--bg-card`), text #F2F2F5 (`--text-primary`), border-top and border-bottom 1px solid rgba(255,255,255,0.06) (`--border-subtle`), weight 400 at 14px, centered or left-aligned, includes inline "Comprar" link in #A7A4F0 (`--accent-text`). Height ~52px (96px combined with nav). The thin strip communicates promotional urgency without competing with the hero.

## Do's and Don'ts

### Do
- Use the indigo tint-plus-stroke treatment — rgba(123,119,224,0.13) fill, 1px rgba(123,119,224,0.45) border, #A7A4F0 action text, 9999px radius — for the primary Buy action, and the same tint and stroke without the action text for selected surfaces; never introduce a second chromatic button color or a solid accent fill
- Set display type at 56-80px in SF Pro Display weight 600 with -0.015em to -0.005em letter-spacing for all hero and section openers
- Apply 28px border-radius to every card, product viewer, and raised surface container — this single radius defines the system's softness
- Compose body copy at 17px SF Pro Text weight 400 with 1.47 line-height and -0.022em tracking as the universal paragraph spec
- Maintain #0B0B0E as the page canvas with no gradient or texture — let product photography provide all visual warmth
- Use rgba(26,26,33,0.95) with a 1px rgba(255,255,255,0.1) hairline for any floating UI element that must layer over photography
- Keep nav and meta text at #A0A0AC and tertiary hints at #7E7E8A — full-white secondary text would overweight the hierarchy
- Reserve #57C08A for copy confirmation and #E05A52 for deletion or error — never for decoration

### Don't
- Never use a second accent hue beyond the indigo family (#7B77E0 / #A7A4F0) for actions — the family is rationed, do not dilute it with green, orange, or pink CTAs
- Never fill a surface with solid #7B77E0 — accent appears only as a 13% tint or a 45% stroke; #A7A4F0 is text, not a fill
- Never add box-shadows to cards or buttons — the system defines elevation through color and radius alone, shadows would feel cheap
- Never use font-weight 700 anywhere — the system maxes at 600 because 700 reads as desperate on the negative tracking
- Never set headings below 28px — the scale starts at subheading 28px and goes up; small bold text is not in the vocabulary
- Never introduce a new radius below 10px for interactive elements — pills (9999px), cards (28px), and links (10px) are the only curves
- Never use #FFFFFF as body text — it is reserved for text selection; the ceiling is #F2F2F5
- Never add background gradients to text blocks or content sections — the thumbnail gradient (#2A2A40 → #1A1A24) is the only gradient in the system

## Surfaces

| Level | Name | Value | Purpose |
|-------|------|-------|---------|
| 0 | Canvas | `#0B0B0E` | Full-page background, hero, nav backdrop |
| 1 | Card Surface | `#141419` | Cards, promo banner, section dividers |
| 2 | Input Surface | `#15151A` | Search well, key chips, toast body |
| 3 | Popover Surface | `rgba(26,26,33,0.95)` | Floating UI over photography, price/buy bar |

## Elevation

The system rejects shadow-based elevation entirely. Depth is communicated through three mechanisms only: (1) tonal contrast — #141419 cards on the #0B0B0E canvas, with a 6% white hairline at the edge, (2) a near-opaque popover fill rgba(26,26,33,0.95) plus a 10% white hairline for elements that float over photography, and (3) the 28px border-radius which optically lifts card edges from the background. Adding box-shadow would violate the flat, cinematic aesthetic.

## Imagery

Photography is the primary visual content and dominates the page surface. Treatment: product hero shot on the #0B0B0E canvas with dramatic side-lighting revealing the laptop's metallic edge — no environmental context, no lifestyle staging, the object exists in a void. Secondary product imagery follows the same isolation principle: devices on #0B0B0E or against the neutral thumbnail gradient (#2A2A40 → #1A1A24). Chip badges use the same neutral gradient as subtle texture on otherwise minimal cards. Iconography is monochrome outlined glyphs in #A0A0AC at the SF Symbols stroke weight. No illustration, no 3D rendering, no abstract graphics — photography and typographic scale carry the entire visual weight.

## Layout

Full-bleed dark canvas with centered max-width ~1440px content container. Hero is a single-screen viewport with oversized headline left-aligned at ~20% from left edge, product image centered-right with dramatic negative space, and a floating popover price/buy bar anchored bottom-right. Section rhythm alternates between full-bleed #0B0B0E bands and full-bleed #141419 bands (section openers like 'Lo principal.', 'Más de cerca.', 'Batería', 'macOS Tahoe'), separated by 80px vertical padding. Feature grids use 3-column card layouts at 28px radius. Content arrangement is consistently text-first: left-aligned headline, optional right-aligned video link, then media grid below. Navigation is a sticky 44px top bar + optional 52px promo strip, both fully opaque. No sidebar, no mega-menu — navigation is horizontal text links only with extreme minimalism.

## Agent Prompt Guide

primary action: #A7A4F0 on rgba(123,119,224,0.13) with a 1px rgba(123,119,224,0.45) border (tinted, never filled)
Create a Primary Action Button: rgba(123,119,224,0.13) background, #A7A4F0 text, 1px solid rgba(123,119,224,0.45) border, 9999px radius, compact pill padding. Use this tint-plus-stroke treatment for the main CTA; solid accent fills are prohibited.
## Quick Color Reference
- Primary text on dark: #F2F2F5
- Secondary text on dark: #A0A0AC
- Canvas/background: #0B0B0E
- Hairline borders: rgba(255,255,255,0.06), rising to rgba(255,255,255,0.12) on hover
- Accent (selection, focus, actions): #7B77E0 at 13% / 45% alpha; #A7A4F0 for text
- Status: #57C08A (success) / #E05A52 (error)
- Window ring: #757575

## Example Component Prompts

1. **Hero section**: Full-bleed #0B0B0E background. Eyebrow "MacBook Pro" at 17px SF Pro Text weight 400, #F2F2F5, letter-spacing -0.374px. Headline at 80px SF Pro Display weight 600, #F2F2F5, letter-spacing -1.2px, line-height 1.05. Subtitle at 21px weight 600, #F2F2F5. Product image floating right with no border.

2. **Feature card**: 28px border-radius, #141419 background, 1px solid rgba(255,255,255,0.06) border, 28px padding all sides. Heading at 28px SF Pro Display weight 600, #F2F2F5, letter-spacing 0.196px. Body at 17px weight 400, #A0A0AC. No shadow.

3. **Primary Buy button**: Background rgba(123,119,224,0.13), text #A7A4F0, border 1px solid rgba(123,119,224,0.45), border-radius 9999px, padding 11px 22px, font 17px SF Pro Text weight 400, letter-spacing -0.374px. Hover: border-color #7B77E0. No solid fill.

4. **Floating popover bar**: Background rgba(26,26,33,0.95), border 1px solid rgba(255,255,255,0.1), border-radius 36px, padding 11px 22px. Price "Desde 2.229 €" in #A0A0AC, value in #F2F2F5, paired with the inline tinted Buy button.

5. **Section opener**: Left-aligned heading "Lo principal." at 40px SF Pro Display weight 600, #F2F2F5, letter-spacing 0px. Right-aligned link "Ver el vídeo ▶" at 17px weight 400, #A7A4F0. 60px bottom padding before grid.

## Color Rationing System

ClipFlow HUD's dark-mode color discipline: chromatic color is rationed to one low-saturation indigo family — #7B77E0 for selection tint, stroke, pin, and focus ring, #A7A4F0 for action labels and inline links — plus two status colors: #57C08A for copy confirmation and #E05A52 for deletion or error. Everything else is the neutral ladder (#F2F2F5 / #A0A0AC / #7E7E8A / #55555F) on a #0B0B0E canvas. The accent never fills a surface: selection is a 13% tint plus a 45% stroke, focus is a 45% stroke plus a 12% halo. Introducing cyan, magenta, pink, orange, or any additional chromatic accent violates the system. #FFFFFF is reserved for text selection; body text never goes above #F2F2F5. Maintain this rationing on any new page built with this system: near-black canvas, neutral ladder text, one indigo family for selection/focus/actions, green and red for status only.

## Typography Tracking Philosophy

Letter-spacing follows an inverse-size relationship: the larger the type, the more negative the tracking. At 80px display size, tracking is -0.015em (-1.2px); at 17px body size, it is -0.022em (-0.374px); at 12px caption, it is -0.010em. This is because SF Pro's geometric forms need compression at large sizes to feel architectural, and expansion at small sizes to remain legible. The system never uses positive tracking above 28px — large type always pulls inward. Subheadings (19-28px) use slightly positive tracking (+0.007em to +0.012em) because shorter strings at smaller sizes need optical loosening to avoid feeling cramped.

## Similar Brands

- **Apple (apple.com)** — Same SF Pro Display weight 600 with negative tracking, same near-black hero canvas discipline, same single-accent rationing (rendered here in indigo #7B77E0), same 28px card radius
- **Tesla (tesla.com)** — Same full-bleed dark hero with product photography on a near-black void, same oversized minimal headline typography, same flat card surfaces, same rationed single-accent strategy
- **Bang & Olufsen (bang-olufsen.com)** — Same near-black-canvas product photography with dramatic side-lighting, same 28px corner radius on all containers, same premium restraint in color palette — monochrome with minimal chromatic punctuation
- **Nothing (nothing.tech)** — Same dark-mode-first aesthetic with near-black backgrounds, same typographic confidence at large display sizes, same minimal interactive color budget — single accent for CTAs, monochrome everywhere else

## Quick Start

### CSS Custom Properties

```css
:root {
  /* Colors — Surfaces */
  --bg-app: #0B0B0E;
  --bg-card: #141419;
  --bg-input: #15151A;
  --bg-active: rgba(255, 255, 255, 0.09);
  --bg-selected: rgba(123, 119, 224, 0.13);
  --pop-bg: rgba(26, 26, 33, 0.95);
  --pop-border: rgba(255, 255, 255, 0.1);

  /* Colors — Borders */
  --border-subtle: rgba(255, 255, 255, 0.06);
  --border-strong: rgba(255, 255, 255, 0.12);
  --border-selected: rgba(123, 119, 224, 0.45);
  --window-ring: #757575;

  /* Colors — Text */
  --text-primary: #F2F2F5;
  --text-secondary: #A0A0AC;
  --text-tertiary: #7E7E8A;
  --text-disabled: #55555F;
  --selection-bg: rgba(123, 119, 224, 0.35);
  --selection-text: #FFFFFF;

  /* Colors — Accent & Status */
  --accent: #7B77E0;
  --accent-text: #A7A4F0;
  --accent-soft: rgba(123, 119, 224, 0.13);
  --ring-soft: rgba(123, 119, 224, 0.12);
  --success: #57C08A;
  --error: #E05A52;

  /* Colors — Media */
  --checker: rgba(255, 255, 255, 0.03);
  --gradient-thumb: linear-gradient(135deg, #2A2A40, #1A1A24);

  /* Typography — Font Families */
  --font-sf-pro-display: 'SF Pro Display', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-sf-pro-text: 'SF Pro Text', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;

  /* Typography — Scale */
  --text-caption: 12px;
  --leading-caption: 1.33;
  --tracking-caption: -0.12px;
  --text-body-sm: 17px;
  --leading-body-sm: 1.47;
  --tracking-body-sm: -0.374px;
  --text-body: 21px;
  --leading-body: 1.19;
  --tracking-body: 0.231px;
  --text-subheading: 28px;
  --leading-subheading: 1.14;
  --tracking-subheading: 0.196px;
  --text-heading-sm: 40px;
  --leading-heading-sm: 1.1;
  --tracking-heading-sm: 0px;
  --text-heading: 56px;
  --leading-heading: 1.07;
  --tracking-heading: -0.28px;
  --text-heading-lg: 64px;
  --leading-heading-lg: 1.06;
  --tracking-heading-lg: -0.576px;
  --text-display: 80px;
  --leading-display: 1.05;
  --tracking-display: -1.2px;

  /* Typography — Weights */
  --font-weight-regular: 400;
  --font-weight-semibold: 600;

  /* Spacing */
  --spacing-unit: 4px;
  --spacing-4: 4px;
  --spacing-8: 8px;
  --spacing-12: 12px;
  --spacing-16: 16px;
  --spacing-20: 20px;
  --spacing-24: 24px;
  --spacing-28: 28px;
  --spacing-32: 32px;
  --spacing-40: 40px;
  --spacing-44: 44px;
  --spacing-48: 48px;
  --spacing-60: 60px;
  --spacing-80: 80px;
  --spacing-104: 104px;
  --spacing-144: 144px;
  --spacing-208: 208px;

  /* Layout */
  --page-max-width: 1440px;
  --section-gap: 80px;
  --card-padding: 28px;
  --element-gap: 10px;

  /* Border Radius */
  --radius-lg: 10px;
  --radius-2xl: 18px;
  --radius-3xl: 28px;
  --radius-3xl-2: 32px;
  --radius-3xl-3: 36px;
  --radius-full: 210px;
  --radius-full-2: 980px;
  --radius-full-3: 999px;
  --radius-full-4: 9999px;

  /* Named Radii */
  --radius-nav: 980px;
  --radius-cards: 28px;
  --radius-links: 10px;
  --radius-badges: 20%;
  --radius-inputs: 210px;
  --radius-buttons: 9999px;

  /* Surfaces (aliases onto the tokens above) */
  --surface-canvas: var(--bg-app);
  --surface-card: var(--bg-card);
  --surface-input: var(--bg-input);
  --surface-popover: var(--pop-bg);
}
```

### Tailwind v4

```css
@theme {
  /* Colors */
  --color-canvas: #0B0B0E;
  --color-card: #141419;
  --color-input: #15151A;
  --color-active: rgba(255, 255, 255, 0.09);
  --color-selected: rgba(123, 119, 224, 0.13);
  --color-popover: rgba(26, 26, 33, 0.95);
  --color-popover-border: rgba(255, 255, 255, 0.1);
  --color-hairline: rgba(255, 255, 255, 0.06);
  --color-border-strong: rgba(255, 255, 255, 0.12);
  --color-border-selected: rgba(123, 119, 224, 0.45);
  --color-window-ring: #757575;
  --color-text-primary: #F2F2F5;
  --color-text-secondary: #A0A0AC;
  --color-text-tertiary: #7E7E8A;
  --color-text-disabled: #55555F;
  --color-accent: #7B77E0;
  --color-accent-text: #A7A4F0;
  --color-accent-soft: rgba(123, 119, 224, 0.13);
  --color-ring-soft: rgba(123, 119, 224, 0.12);
  --color-success: #57C08A;
  --color-error: #E05A52;
  --color-checker: rgba(255, 255, 255, 0.03);

  /* Typography */
  --font-sf-pro-display: 'SF Pro Display', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-sf-pro-text: 'SF Pro Text', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;

  /* Typography — Scale */
  --text-caption: 12px;
  --leading-caption: 1.33;
  --tracking-caption: -0.12px;
  --text-body-sm: 17px;
  --leading-body-sm: 1.47;
  --tracking-body-sm: -0.374px;
  --text-body: 21px;
  --leading-body: 1.19;
  --tracking-body: 0.231px;
  --text-subheading: 28px;
  --leading-subheading: 1.14;
  --tracking-subheading: 0.196px;
  --text-heading-sm: 40px;
  --leading-heading-sm: 1.1;
  --tracking-heading-sm: 0px;
  --text-heading: 56px;
  --leading-heading: 1.07;
  --tracking-heading: -0.28px;
  --text-heading-lg: 64px;
  --leading-heading-lg: 1.06;
  --tracking-heading-lg: -0.576px;
  --text-display: 80px;
  --leading-display: 1.05;
  --tracking-display: -1.2px;

  /* Spacing */
  --spacing-4: 4px;
  --spacing-8: 8px;
  --spacing-12: 12px;
  --spacing-16: 16px;
  --spacing-20: 20px;
  --spacing-24: 24px;
  --spacing-28: 28px;
  --spacing-32: 32px;
  --spacing-40: 40px;
  --spacing-44: 44px;
  --spacing-48: 48px;
  --spacing-60: 60px;
  --spacing-80: 80px;
  --spacing-104: 104px;
  --spacing-144: 144px;
  --spacing-208: 208px;

  /* Border Radius */
  --radius-lg: 10px;
  --radius-2xl: 18px;
  --radius-3xl: 28px;
  --radius-3xl-2: 32px;
  --radius-3xl-3: 36px;
  --radius-full: 210px;
  --radius-full-2: 980px;
  --radius-full-3: 999px;
  --radius-full-4: 9999px;
}
```
