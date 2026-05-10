# DeckDoc Language Specification

`DeckDoc` is a compact plain-text language for PowerPoint and Google Slides presentations.

This version is intentionally **slide-first**:

1. normal slide content is written as compact object lines,
2. shared theme, style, asset, motion, and reusable layout definitions live in the same document,
3. layouts compress repeated branded structure so slides stay short,
4. explicit `[obj ...]` lines are only for exceptions that are too detailed for the compact line form,
5. dense editorial layouts should compress into named layout targets such as areas, splits, grids, and stacks instead of dozens of raw coordinates.

That keeps presentation text short enough for agent prompts while still preserving layout, typography, reusable layout systems, notes, charts, tables, transitions, and common slide features.

## At a Glance

| Item | Value |
| --- | --- |
| Canonical extension | `.deckdoc` |
| Encoding | UTF-8 |
| Document start | `= Presentation Name` |
| Reusable layout block | `[layout Cover]` ... `[end]` |
| Slide start | `== Slide Title` |
| Main slide body | compact object lines such as `@B2 24x2 [.title] | Quarterly Operations` |
| Named target form | target lines such as `@title | Quarterly Operations` or `@cards[2] [image asset=hero]` |
| Layout built-ins | `[area ...]`, `[split ...]`, `[grid ...]`, `[stack ...]` |
| Layout slot form | slot lines such as `@title = B2 24x2 [.title]` or `@title = head [.title]` |
| Layout fixed object form | fixed layout lines such as `!A1 32x18 [shape rect fill=$surface layer=back]` or `!right [shape rect fill=#E2E8F0]` |
| Compact default | shared directives, optional layouts, then slides |
| Detailed override | `[obj ...]` |
| Shared built-ins | `[theme ...]`, `[style ...]`, `[asset ...]`, `[motion ...]`, `[layout ...]` |
| Slide built-ins | `[use ...]`, `[background ...]`, `[notes ...]`, `[transition ...]`, `[area ...]`, `[split ...]`, `[grid ...]`, `[stack ...]`, `[animate ...]` |
| Other built-ins | `[group ...]`, `[table ...]`, `[chart ...]` |
| Not built in | comments, review state, exact master XML, SmartArt internals, 3D scene data, presenter view state, ink, macros |
| Extension form | `[x ...]` |

## Core Model

A DeckDoc file is made of:

1. one presentation title,
2. zero or more document attributes,
3. zero or more shared directives,
4. zero or more reusable layout blocks,
5. one or more slides,
6. zero or more slide directives per slide,
7. zero or more layout directives per slide,
8. zero or more compact object lines per slide,
9. zero or more explicit object overrides per slide,
10. zero or more non-object directives per slide.

The compact object line is the canonical default for slide content. Use explicit `[obj ...]` only when the compact line would become awkward or lossy.

DeckDoc keeps reusable structure in the same file rather than in a separate template document. Shared theme tokens, typography, recurring shells, and reusable layouts are defined once near the top of the file, then slides opt into them with `[use ...]`.

Emit literal DeckDoc characters, not JSON unicode escapes such as `\u0022`, `\u0027`, `\u003E`, or `\u003C`.

## Built-in Feature Coverage

The built-in DeckDoc surface is intended to cover the most common presentation features that matter to agents and to everyday authors.

### Text and Typography

- titles,
- subtitles,
- body text,
- captions,
- bullets and numbered lists,
- font family,
- font size,
- bold, italic, underline, strike,
- text color,
- paragraph alignment,
- vertical alignment,
- line spacing,
- text wrapping and shrink-to-fit,
- hyperlinks,
- simple rich emphasis via explicit overrides.

### Layout and Visual Structure

- slide size and logical grid,
- reusable layout blocks,
- anchored positioning and spans,
- named areas,
- split regions,
- repeated grids and stacks,
- padding and gutters,
- named slots and placeholder filling,
- indexed targets such as card walls and image mosaics,
- background fill and image,
- shape fill and stroke,
- corner radius,
- opacity,
- rotation,
- z-order and layer intent,
- grouping,
- connectors and simple lines,
- image fit and crop,
- reusable brand shells.

### Data and Media

- images,
- tables,
- charts,
- icons treated as images or shapes,
- speaker notes.

### Motion and Presentation Behavior

- slide transitions,
- object entrance, emphasis, exit, and motion animation families,
- animation ordering,
- duration,
- delay,
- trigger hints,
- hidden slides,
- section names.

### Shared Theme And Layout Reuse

- theme tokens,
- brand colors,
- typography sets,
- reusable layouts,
- reusable slots,
- reusable layout targets,
- recurring fixed decorations,
- footer/header zones,
- default transitions,
- extraction of reusable branded structure into shared directives and layout blocks within the same document.

## Quick Example

```text
= FY2026 Operating Plan
:deckdoc: 1
:locale: en-US
:size: wide
:grid: 32x18

[theme primary=#0B5FFF accent=#F97316 ink=#0F172A muted=#5B6472 surface=#F8FAFC display="Aptos Display" body="Aptos"]
[style title font=$display size=28 bold fg=$ink]
[style subtitle font=$body size=14 fg=$muted]
[style body font=$body size=16 fg=$ink]
[asset hero "./hero-team.png"]
[asset chartshot "./growth-chart.png"]

[layout cover]
[background fill=$surface]
!A1 32x18 [shape rect fill=$surface layer=back]
!A1 32x2 [shape rect fill=$primary layer=back]
@title = B3 20x2 [.title]
@subtitle = B5 18x1 [.subtitle]
@hero = W3 8x10 [image fit=cover radius=0.2]
[end]

[layout evidence]
[split B2:Y14 rows=(2,10) gap=1 as=head,body]
[split body cols=(14,11) gap=1 as=copy,visual]
!A18 32x1 [shape rect fill=$primary layer=back]
@title = head [.title]
@body = copy [list .body bullet=disc]
@visual = visual [image fit=contain]
[end]

== Opening
[use cover]
[transition fade dur=0.45s]
@title | FY2026 Operating Plan
@subtitle | Priorities, risks, and next-quarter actions
@hero [image asset=hero fit=cover alt="Leadership workshop"]

== Proof Points
[use evidence]
[transition push dir=left dur=0.35s]
@title | What changed this quarter
@body [list .body bullet=disc] | Cycle time down 18% | Renewal rate up 6 points | Support backlog down 23%
@visual [image asset=chartshot fit=contain alt="Quarterly growth chart"]
[notes "Lead with churn, then connect the chart to renewal quality."]
```

## Document Header

Every file starts with one presentation title:

```text
= Presentation Name
```

Document-level settings use AsciiDoc-style attribute lines.

## Built-in Document Attributes

| Attribute | Meaning | Example |
| --- | --- | --- |
| `:deckdoc:` | Language version | `:deckdoc: 1` |
| `:locale:` | Locale hint | `:locale: en-US` |
| `:size:` | Slide size preset | `:size: wide` |
| `:grid:` | Logical layout grid | `:grid: 32x18` |

Rules:

- `:deckdoc:` is required for canonical DeckDoc files.
- `:size:` may be `wide`, `standard`, or a custom `<width>x<height><unit>` value such as `13.333x7.5in`.
- `:grid:` defaults to `32x18` when omitted.
- Less-used open-state metadata such as a custom start slide or a document direction hint is intentionally not built in. Use `[x ...]` when that fidelity matters.

## Shared Built-in Directives

Shared directives normally appear before the first layout block or before the first slide.

### `[theme ...]`

Defines reusable color and font tokens.

```text
[theme <token>=<value>...]
```

Examples:

```text
[theme primary=#0B5FFF accent=#F97316 ink=#0F172A surface=#F8FAFC]
[theme display="Aptos Display" body="Aptos"]
```

Values may later be referenced with `$token` in other directives and attrlists.

### `[style ...]`

Defines a reusable object style.

```text
[style <name> <style-entry>...]
```

Examples:

```text
[style title font=$display size=28 bold fg=$ink]
[style body font=$body size=16 fg=$ink align=left/top]
```

Built-in style entries:

| Entry | Meaning |
| --- | --- |
| `font=...` | Font family |
| `size=<n>` | Font size in points |
| `fg=#RRGGBB` or `$token` | Text or line color |
| `fill=#RRGGBB` or `$token` | Fill color |
| `stroke=#RRGGBB` or `$token` | Stroke color |
| `weight=<n>` | Stroke width in points |
| `bold` / `italic` / `underline` / `strike` | Text emphasis |
| `align=horizontal/vertical` | Text alignment |
| `line=<n>` | Line spacing multiplier |
| `pad=<n>` | Internal padding in points |
| `wrap` / `fit=shrink` / `fit=resize` | Text fit behavior |
| `opacity=<0..1>` | Object opacity |
| `radius=<n><unit>` | Corner radius |
| `rotate=<deg>` | Rotation |
| `layer=back|front|<n>` | Z-order intent |

### `[asset ...]`

Defines a reusable media reference.

```text
[asset <name> <reference>]
```

Examples:

```text
[asset hero "./hero-team.png"]
[asset logo "brand://logo"]
```

### `[motion ...]`

Defines a reusable animation preset.

```text
[motion <name> <entry>...]
```

Examples:

```text
[motion fade-in enter=fade dur=0.35s]
[motion lift-up motion=from-bottom dur=0.45s ease=out]
```

## Layout Blocks

Reusable layouts are defined inline inside the same DeckDoc file.

Syntax:

```text
[layout <name>]
<layout-lines>
[end]
```

Example:

```text
[layout cover]
[background fill=$surface]
!A1 32x18 [shape rect fill=$surface layer=back]
@title = B3 20x2 [.title]
@hero = W3 8x10 [image fit=cover]
[end]
```

Use layout blocks for recurring branded shells, placeholder geometry, repeated split compositions, gallery structures, and other slide scaffolds that several slides reuse.

## Slides

Each slide starts with:

```text
== Slide Title
```

Slide titles are logical identifiers. They do not need to match visible on-slide title text.

## Built-in Slide Directives

Slide directives usually appear below the slide heading and before the first object line.

The built-in slide surface is intentionally small. Only directives that commonly affect meaning, first-open behavior, or common author intent are built in.

### `[use ...]`

Applies a named layout block to the current slide.

```text
[use <layout-name>]
```

Example:

```text
[use cover]
```

When a slide uses a layout, named slot lines such as `@title | ...` bind to that layout's slot definitions.

### `[background ...]`

Defines a slide background when it differs from the layout default or from the document's normal theme.

```text
[background <entry>...]
```

Examples:

```text
[background fill=#0F172A]
[background asset=hero fit=cover]
```

### `[notes ...]`

Stores speaker notes.

```text
[notes <quoted-text>]
```

Example:

```text
[notes "Lead with renewal quality, then show the proof chart."]
```

### `[transition ...]`

Defines the slide transition.

```text
[transition <type> <entry>...]
```

Examples:

```text
[transition fade dur=0.35s]
[transition push dir=left dur=0.3s]
```

Built-in transition types:

| Type | Meaning |
| --- | --- |
| `none` | No transition |
| `cut` | Immediate cut |
| `fade` | Cross-fade transition |
| `push` | Slide enters from a direction |
| `wipe` | Wipe reveal from a direction |
| `morph` | Cross-slide continuity hint when the provider supports it |

Built-in transition entries:

| Entry | Meaning |
| --- | --- |
| `dur=<time>` | Duration such as `0.35s` |
| `dir=left|right|up|down` | Direction when the transition type uses one |
| `advance=click|after(<time>)` | Advance behavior |

### `[section ...]`

Associates the slide with a logical section.

```text
[section <name>]
```

Example:

```text
[section Highlights]
```

### `[state ...]`

Controls slide visibility.

```text
[state hidden]
```

Example:

```text
[state hidden]
```

## Built-in Layout Block Directives

These directives usually appear near the top of a `[layout ...]` block, before fixed lines or slot lines.

### `[background ...]`

Defines the default background for the layout.

Example:

```text
[background fill=$surface]
```

### `[grid ...]`

Overrides the logical layout grid for the current layout block.

```text
[grid <cols>x<rows>]
```

Example:

```text
[grid 40x22]
```

### `[transition ...]`

Defines the layout's default slide transition hint.

Example:

```text
[transition fade dur=0.25s]
```

## Built-in Layout Directives

Layout directives may appear inside slides and inside layout blocks. They create named targets that later object lines can reference with `@name` or `@name[index]`.

A dense editorial slide should usually be written as a few layout directives plus target-bound objects, not as a raw dump of every absolute coordinate.

### `[area ...]`

Defines a named rectangular target.

```text
[area <name> <range> [<entry>...]]
```

Examples:

```text
[area rail B2:H14]
[area hero P3:Y12 image fit=cover]
```

Use `[area ...]` for one-off named regions such as a title zone, left rail, image well, footer band, or quote box.

### `[split ...]`

Splits a parent range or target into named child targets.

```text
[split <range-or-target> cols=(<part>,<part>,...) [gap=<n>] as=<name>,<name>,...]
[split <range-or-target> rows=(<part>,<part>,...) [gap=<n>] as=<name>,<name>,...]
[split cols=(<part>,<part>,...) [gap=<n>] as=<name>,<name>,...]
[split rows=(<part>,<part>,...) [gap=<n>] as=<name>,<name>,...]
```

Examples:

```text
[split B2:Y14 cols=(7,17) gap=1 as=rail,stage]
[split stage rows=(3,11) gap=0.8 as=head,body]
[split compare cols=(1fr,1fr) gap=1 as=before,after]
[split cols=(16,24) gap=1 as=left,right]
```

Part values may be:

- raw grid counts such as `7`,
- percentages such as `30%`,
- fractions such as `1fr`.

Use `[split ...]` for two-column, two-row, before/after, rail/body, or head/body layouts.

If the source is omitted, the split uses the current layout grid.

### `[grid ...]`

Creates a repeated indexed target grid.

```text
[grid <name> in=<range-or-target> cols=<n> [rows=<n>] [gap=<x>[x<y>]] [order=row|col] [<entry>...]]
```

Examples:

```text
[grid cards in=body cols=3 rows=2 gap=0.5x0.5]
[grid gallery in=hero cols=4 gap=0.4 image fit=cover]
```

Named grid cells are referenced as `@cards[1]`, `@cards[2]`, or `@cards[r2c1]`.

Use `[grid ...]` for card walls, editorial mosaics, image galleries, icon rows, and metric clusters.

### `[stack ...]`

Creates a repeated one-dimensional target stack.

```text
[stack <name> in=<range-or-target> dir=down|up|left|right count=<n> [gap=<n>] [<entry>...]]
```

Examples:

```text
[stack steps in=stage dir=down count=4 gap=0.5]
[stack chips in=rail dir=down count=3 gap=0.25]
```

Named stack items are referenced as `@steps[1]`, `@steps[2]`, and so on.

Use `[stack ...]` for repeated bullets-as-cards, phase ladders, KPI chips, or vertical callout groups.

Rules:

1. Layout targets may be nested by using the output of one layout directive as the input to another.
2. Targets defined by `[area ...]`, `[split ...]`, `[grid ...]`, and `[stack ...]` share the same namespace as layout slots within the current slide or layout.
3. Later object lines that target a named region fill that target by default unless their own attrlist changes fit or other placement behavior.
4. Prefer layout targets when several objects form an obvious repeated or split composition.

## Addressing and Geometry

DeckDoc uses slide-grid notation for compact positioning.

The logical grid defaults to `32x18`, which fits common 16:9 decks while remaining short enough for human editing.

| Form | Example |
| --- | --- |
| Grid anchor | `B2` |
| Grid span | `24x2` |
| Grid range | `B2:Y10` |
| Named target reference | `@title` |
| Indexed target reference | `@cards[2]` |
| Split parts | `cols=(7,17)` or `rows=(1fr,1fr)` |
| Fixed layout line anchor | `!A1 32x18 ...` |
| Custom physical size | `13.333x7.5in` |

Rules:

1. Grid anchors are 1-based, like spreadsheet addresses.
2. Grid spans are width-by-height counts on the logical grid.
3. Physical units are only needed when an attribute requires exact fidelity.
4. Layout slots and layout targets are named and later referenced from a slide or layout without repeating geometry.

## Compact Object Lines

Compact object lines are the canonical default for ordinary slide content.

### Geometry Form

```text
@<anchor> <span> [<object-attrlist>] | <payload>
```

Examples:

```text
@B2 24x2 [.title] | Quarterly Operations
@B5 12x7 [list .body bullet=disc] | Shorten cycle time | Raise attach rate | Simplify rollout
@P4 10x8 [image asset=hero fit=cover alt="Workshop photo"]
@A1 32x18 [shape rect fill=#F8FAFC layer=back]
```

### Named Target Form

When a slide uses a layout block, or when a slide or layout defines named areas, splits, grids, or stacks, the target name may replace explicit geometry:

```text
@<target-name> [<object-attrlist>] | <payload>
@<name> <target-name> [<object-attrlist>] | <payload>
```

Examples:

```text
@title | Quarterly Operations
@body [list .body bullet=disc] | Lower churn | Raise renewal quality | Simplify rollout
@hero [image asset=hero fit=cover]
@cards[2] [image asset=shot-b fit=cover]
@metric_card c1 [shape roundrect fill=$ink stroke=$primary]
```

Rules:

1. The anchor or target reference identifies where the object belongs.
2. The optional attrlist applies style and object metadata.
3. The payload is object-kind specific.
4. Omit attrlist entries that only restate defaults.
5. A target may come from a layout slot, an area, a split output, a grid cell, or a stack item.
6. When a target-bound object needs a stable identifier for grouping or animation, use `@<name> <target-name> [...]`; it normalizes to the same target placement plus `name=<name>`.
7. A named target is a single rectangle. If one card, sidebar, or panel needs several independent text, list, icon, or line elements, subdivide that target first with nested `[split ...]`, `[grid ...]`, `[stack ...]`, or explicit geometry; otherwise the objects share the same rectangle and overlap.
8. The same rectangle rule applies to layout slot lines. Avoid binding multiple slots such as `@subtitle = left` and `@body = left` to the same source target unless `left` is first subdivided into separate child targets.
9. Geometry prefers the canonical anchor-plus-span form such as `B4 29x3`, but the tolerant shorthand `B4:AD3` is also accepted and means anchor `B4` spanning through column `AD` for `3` rows.

## Layout Slot Lines

Layout slot lines define reusable placeholder geometry and defaults.

Syntax:

```text
@<slot-name> = <anchor> <span> [<slot-attrlist>]
@<slot-name> = <target-name> [<slot-attrlist>]
```

Examples:

```text
@title = B2 24x2 [.title]
@body = B5 14x9 [list .body bullet=disc]
@hero = Q4 10x9 [image fit=cover radius=0.2]
@title = head [.title]
```

Rules:

1. The slot name is the stable identifier used by slides.
2. The layout may provide geometry directly or bind the slot to a named layout target.
3. A slide may still override style or object entries when needed.

## Layout Fixed Lines

Layout fixed lines define recurring layout objects that are not content slots.

Syntax:

```text
!<anchor> <span> [<object-attrlist>]
!<range> [<object-attrlist>]
!<name> <range-or-geometry> [<object-attrlist>]
!<target-name> [<object-attrlist>]
```

Examples:

```text
!A1 32x18 [shape rect fill=$surface layer=back]
!A1 32x2 [shape rect fill=$primary layer=back]
!X16 4x2 [image asset=logo fit=contain]
!B2:Y22 [shape rect fill=$dark]
!bg B2:Y22 [shape rect fill=$dark]
!right [shape rect fill=#E2E8F0 stroke=#94A3B8]
```

Use fixed lines for recurring brand bars, corner accents, watermarks, footer shells, and similar layout-owned objects.

## Object Attrlists

Attrlist shape:

```text
[.style-name key=value flag]
```

Built-in attrlist entries:

| Entry | Meaning |
| --- | --- |
| `.style-name` | Apply a named style |
| `text` | Explicit text object |
| `list` | Bulleted or numbered list |
| `image` | Image object |
| `shape` | Generic shape object |
| `line` | Connector or rule |
| `icon` | Icon object |
| `asset=<name>` | Asset name for an image or icon |
| `ref=<reference>` | Direct media reference when no shared asset name is used |
| `font=...` | Font family |
| `size=<n>` | Font size |
| `fg=...` | Text or stroke color |
| `fill=...` | Fill color |
| `stroke=...` | Stroke color |
| `weight=<n>` | Stroke width |
| `bold` / `italic` / `underline` / `strike` | Text emphasis |
| `align=horizontal/vertical` | Text alignment |
| `line=<n>` | Line spacing |
| `pad=<n><unit>` | Internal padding |
| `bullet=disc|dash|check|number` | List bullet style |
| `start=<n>` | Starting number for numbered lists |
| `wrap` | Preserve text wrapping |
| `fit=contain|cover|stretch|shrink|resize` | Image or text fit mode |
| `crop=l,t,r,b` | Image crop percentages |
| `opacity=<0..1>` | Object opacity |
| `radius=<n><unit>` | Corner radius |
| `rotate=<deg>` | Rotation |
| `layer=back|front|<n>` | Z-order intent |
| `link="..."` | Hyperlink |
| `alt="..."` | Alternative text |
| `name=<id>` | Stable object name |
| `hidden` | Object hidden by default |
| `locked` | Editing lock hint |

For shape objects, the payload or attrlist should include a built-in shape kind such as `rect`, `roundrect`, `ellipse`, `diamond`, `triangle`, `line`, or `arrow`.

## Payload Forms

Compact object lines share a small set of payload forms.

| Form | Meaning |
| --- | --- |
| `| Quarterly Operations` | Plain text payload |
| `| "  Keep spaces  "` | Quoted text payload |
| `| A | B | C` | List payload with one item per segment |
| no payload | Object is fully described by attributes, such as a shape or image |

## Common Compact Patterns

The following forms cover the most common authoring cases. These are not separate object kinds beyond what the attrlist already defines; they are the canonical short forms authors should usually prefer.

### Title Text

Syntax:

```text
@<anchor-or-target> [text .title] | <title-text>
```

Example:

```text
@title [.title] | Quarterly Operations Review
```

### Subtitle Text

Syntax:

```text
@<anchor-or-target> [text .subtitle] | <subtitle-text>
```

Example:

```text
@subtitle [.subtitle] | Q4 highlights, risks, and next-quarter actions
```

### Body Text

Syntax:

```text
@<anchor-or-target> [text .body] | <body-text>
```

Example:

```text
@copy [.body] | Cycle time fell 18% after the routing change.
```

### Caption Text

Syntax:

```text
@<anchor-or-target> [text .caption] | <caption-text>
```

Example:

```text
@caption [.caption] | Source: FY2026 operating review
```

### Bulleted List

Syntax:

```text
@<anchor-or-target> [list bullet=disc] | <item> | <item> | <item>
```

Example:

```text
@body [list .body bullet=disc] | Lower churn | Raise renewal quality | Simplify rollout
```

### Dash List

Syntax:

```text
@<anchor-or-target> [list bullet=dash] | <item> | <item>
```

Example:

```text
@risks [list .body bullet=dash] | Vendor delay | Scope creep
```

### Check List

Syntax:

```text
@<anchor-or-target> [list bullet=check] | <item> | <item>
```

Example:

```text
@done [list .body bullet=check] | Security review complete | Demo approved
```

### Numbered List

Syntax:

```text
@<anchor-or-target> [list bullet=number [start=<n>]] | <step> | <step> | <step>
```

Examples:

```text
@steps [list .body bullet=number] | Discover | Design | Deliver
@steps [list .body bullet=number start=4] | Launch | Measure | Iterate
```

### Image

Syntax:

```text
@<anchor-or-target> [image asset=<name> fit=contain|cover|stretch [crop=l,t,r,b] [alt="..."]]
```

Example:

```text
@hero [image asset=hero fit=cover alt="Leadership workshop"]
```

### Icon

Syntax:

```text
@<anchor-or-target> [icon asset=<name> fit=contain [fg=#RRGGBB]]
```

Example:

```text
@badge [icon asset=rocket fit=contain fg=$primary]
```

### Shape

Syntax:

```text
@<anchor-or-target> [shape <shape-kind> [fill=#RRGGBB] [stroke=#RRGGBB] [weight=<n>] [radius=<n><unit>]] [| <text>]
```

Examples:

```text
@card [shape roundrect fill=#F8FAFC stroke=#CBD5E1 radius=0.18in]
@pill [shape roundrect fill=$accent fg=#FFFFFF] | On Track
```

### Line Or Connector

Syntax:

```text
@<anchor-or-target> [line stroke=#RRGGBB [weight=<n>] [arrow]]
```

Example:

```text
@rule [line stroke=#CBD5E1 weight=1]
```

### Quoted Strings

Use double quotes when text needs:

- leading or trailing spaces,
- a literal `|`,
- a leading `[` or `@`,
- line breaks,
- or text that would otherwise parse as another token.

Supported escapes:

| Escape | Meaning |
| --- | --- |
| `\"` | Literal quote |
| `\\` | Literal backslash |
| `\n` | Line break |
| `\t` | Tab |
| `\|` | Literal pipe |

## Explicit Object Overrides

Use `[obj ...]` only when a single object needs more detail than the compact line should carry.

Syntax:

```text
[obj <anchor-or-target> <entry>...] [payload]
```

Examples:

```text
[obj title rich="Quarterly [b]Operations[/b]"]
[obj B6 runs="[{text:'FY2026',bold:true},{text:' plan'}]"]
[obj hero alt="Workshop photo taken in Berlin"]
[obj title at=U7:AL11]
```

Recommended uses:

1. rich text that is awkward inline,
2. exact run-level overrides,
3. hidden metadata on an otherwise simple object,
4. relocating or restyling an existing named or targeted object with entries such as `at=...`, `size=...`, or `fg=...`,
5. a one-off animation or accessibility detail.

## Grouping

Use `[group ...]` when multiple objects should move or animate together.

```text
[group <name> <member>...]
```

Example:

```text
[group metrics title card-a card-b card-c]
```

## Tables

Use `[table ...]` when a slide contains a real presentation table.

Syntax:

```text
[table <name> at=<anchor> size=<span> [<entry>...]]
[table <name> at=<target-name> [<entry>...]]
| cell | cell | cell |
| --- | --- | --- |
| cell | cell | cell |
[end]
```

Example:

```text
[table Risks at=B8 size=18x6 .body]
| Risk | Owner | Status |
| --- | --- | --- |
| Vendor delay | Ops | Open |
| Scope creep | Product | Mitigated |
[end]

[table Costs at=visual .body banded]
| Month | Compute | Storage |
| April | 120k | 45k |
| May | 145k | 52k |
[end]
```

Built-in table entries:

| Entry | Meaning |
| --- | --- |
| `.style-name` | Apply a named style |
| `header` | First row is a header row |
| `banded` | Alternating row fill hint |
| `merge=<range>` | Cell merge hint inside the table grid |
| `align=...` | Default text alignment |

Table rules:

1. Table content must stay visible as table content in the DeckDoc text.
2. A markdown-style separator row such as `| --- | --- | --- |` is allowed after the header row.
3. Table cells may use quoted text so embedded punctuation and spacing stay intact.
4. When a cell needs non-default formatting, writers may fall back to an explicit `[obj ...]` or `[x ...]` detail rather than making the compact table form unreadable.
5. Table placement may use either concrete geometry or a named target such as `at=visual` or `at=cards[2]`.

## Charts

Use `[chart ...]` when a slide contains a chart whose type, placement, and series should survive round-tripping.

Syntax:

```text
[chart "<name>" type=<type> at=<anchor> size=<span>]
[chart "<name>" type=<type> at=<target-name>]
- series <type> "<label>" cat=(...) val=(...) [axis=primary|secondary] [color=#RRGGBB] [labels]
[end]
```

Example:

```text
[chart "Revenue Trend" type=combo at=P5 size=12x7]
- series column "Revenue" cat=(Q1,Q2,Q3,Q4) val=(12,14,17,19) color=$primary
- series line "Margin %" cat=(Q1,Q2,Q3,Q4) val=(0.22,0.24,0.27,0.29) axis=secondary color=$accent labels
[end]

[chart "Cost Trend" type=combo at=visual]
- series column "Revenue" cat=(Q4,Q1,Q2) val=(8,10,12) color=$primary
- series line "Infra Cost" cat=(Q4,Q1,Q2) val=(4,6,9) axis=secondary color=$accent labels
[end]
```

Built-in chart types:

| Type | Meaning |
| --- | --- |
| `column` | Vertical bars over categories |
| `bar` | Horizontal bars over categories |
| `line` | Connected points over categories |
| `area` | Filled line chart |
| `pie` | One-series part-to-whole chart |
| `doughnut` | Ring-style part-to-whole chart |
| `scatter` | Numeric x/y points |
| `bubble` | Numeric x/y points with size |
| `combo` | Mixed per-series chart types |

Chart placement may use either concrete geometry or a named target such as `at=visual` or `at=cards[2]`.

## Animation

Use `[animate ...]` for object-level animation.

Syntax:

```text
[animate <target> <entry>...]
```

Examples:

```text
[animate title enter=fade dur=0.35s order=1]
[animate hero motion=from-right dur=0.45s delay=0.1s order=2]
[animate metrics emphasis=pulse dur=0.25s on=click]
```

Built-in animation entries:

| Entry | Meaning |
| --- | --- |
| `enter=appear|fade|wipe|zoom` | Entrance family |
| `emphasis=pulse|grow|spin|color` | Emphasis family |
| `exit=fade|wipe|zoom` | Exit family |
| `motion=from-left|from-right|from-top|from-bottom|path(...)` | Motion family |
| `dur=<time>` | Duration |
| `delay=<time>` | Delay before start |
| `order=<n>` | Sequence order |
| `on=auto|click|after(<target>)` | Trigger hint |
| `preset=<motion-name>` | Reuse a shared `[motion ...]` preset |

Providers may approximate families they cannot render natively, but the DeckDoc animation text stays the same.

## Compact Layout Example

The following slide is representative of dense editorial decks that would otherwise need dozens of raw object coordinates.

```text
== Phase 1
[background fill=#F8FAFC]
[split B2:Y14 cols=(7,17) gap=1 as=rail,stage]
[split stage rows=(2,12) gap=0.8 as=head,body]
[grid cards in=body cols=3 rows=2 gap=0.5x0.5]

@rail [.subtitle] | Phase 1
@head [.title] | Requirement Engineering
@cards[1] [shape roundrect fill=#E2E8F0]
@cards[2] [shape roundrect fill=#E2E8F0]
@cards[3] [shape roundrect fill=#E2E8F0]
@cards[4] [.body] | BAs generate executable docs via agents
@cards[5] [.body] | Version-controlled living documentation
@cards[6] [.body] | One document per feature
```

This form stays readable because the layout is declared once, then the slide content binds into named targets.

## Layout Extraction

When a branded presentation is converted into DeckDoc, repeated brand structure should be hoisted into shared directives and layout blocks inside the same file.

1. reusable theme tokens go into shared directives,
2. reusable layouts and slots go into `[layout ...]` blocks,
3. recurring fixed decorations go into layout fixed lines,
4. ordinary slide-by-slide content stays inside slide sections,
5. one-off visual exceptions stay on the individual slide that needs them,
6. unsupported provider-specific master details may be preserved as `[x ...]`.

Good extraction candidates:

- brand colors,
- display and body fonts,
- recurring backgrounds,
- recurring layout areas and splits,
- recurring card and gallery grids,
- layout placeholders,
- recurring footer/header zones,
- repeated logos or bars,
- default transitions,
- reusable image wells,
- recurring table or card shells.

Poor extraction candidates:

- one-off chart annotations,
- one slide's custom photo crop,
- ad hoc callouts,
- speaker-specific notes,
- comments and review state,
- exact provider master XML that has no portable meaning.

## What Is Not Built In

The following are intentionally **not** part of the DeckDoc built-in surface:

- comments and review threads,
- presenter view state,
- exact guide coordinates,
- ink and freehand strokes,
- SmartArt internal structure,
- 3D scene and model settings,
- exact master-slide XML internals,
- macros and scripting,
- selection state,
- provider-specific AI designer suggestions,
- custom start-slide metadata,
- document direction metadata,
- exact video playback internals beyond common placeholders.

Those details may still matter in a host application, but they are less useful for agent-facing presentation text than slide content, layout intent, reusable layouts, charts, tables, and common transitions. If a workflow needs them, use `[x ...]`.

Examples:

```text
[x powerpoint smartart layout="hierarchy-1"]
[x powerpoint video autoplay loop trim=0:02-0:17]
[x view start="Proof Points"]
[x document direction=rtl]
```

## Length and Time Units

Built-in length units:

- `px`
- `pt`
- `in`
- `cm`
- `mm`

Built-in time units:

- `ms`
- `s`

## Extensions

Provider-specific or intentionally non-built-in details should use `[x ...]`.

Examples:

```text
[x powerpoint section-collapsed]
[x google-slides speaker-id="abc123"]
[x document direction=rtl]
```

Unknown extensions must round-trip unchanged.

## Canonical Serialization Rules

To keep diffs stable:

1. write document attributes first,
2. then shared directives in this order: `theme`, `style`, `asset`, `motion`,
3. then layout blocks in document order,
4. inside a layout block, emit layout-block directives in this order when present: grid size form, `background`, `transition`, `area`, `split`, named grid form, `stack`,
5. then emit fixed layout lines in visual order,
6. then emit layout slot lines in reading order,
7. then emit layout extensions,
8. then emit `[end]`,
9. then slides in document order,
10. inside a slide, emit slide directives in this order when present: `use`, `section`, `state`, `background`, `notes`, `transition`,
11. then emit slide-local layout directives in this order when present: `area`, `split`, `grid`, `stack`,
12. then emit compact object lines in visual order,
13. then emit explicit `[obj ...]` overrides only when needed,
14. then emit standalone non-object directives in this order: `group`, `table`, `chart`, `animate`, `x`.

Compact export rules:

- omit formatting that only restates defaults,
- keep shared branded shells in layout blocks rather than repeating them on every slide,
- keep slides focused on actual slide content,
- prefer slot form over repeated geometry when a slide uses a layout,
- prefer layout directives such as `split`, `grid`, and `stack` when several objects form an obvious split, ladder, card wall, or image mosaic,
- prefer compact object lines over `[obj ...]`,
- prefer shared styles over repeated inline style attrs,
- prefer shared motions over repeated animation entry lists when that makes text shorter,
- keep style and theme metadata only when it is likely useful to downstream agents or required for fidelity,
- keep less-used open-state metadata in `[x ...]` rather than in the compact built-in header.

## Complete Compact Example

```text
= Quarterly Operations Review
:deckdoc: 1
:locale: en-US
:size: wide
:grid: 32x18

[theme primary=#14532D accent=#F59E0B ink=#0F172A muted=#4B5563 surface=#F8FAFC display="Aptos Display" body="Aptos"]
[style title font=$display size=30 bold fg=$ink]
[style subtitle font=$body size=14 fg=$muted]
[style body font=$body size=16 fg=$ink]
[asset hero "./operations-team.png"]
[asset chartshot "./q4-performance.png"]

[layout cover]
[background fill=$surface]
!A1 32x18 [shape rect fill=$surface layer=back]
!A17 32x2 [shape rect fill=$primary layer=back]
@title = B3 22x2 [.title]
@subtitle = B5 18x1 [.subtitle]
@hero = W3 8x10 [image fit=cover radius=0.18]
[end]

[layout summary]
[background fill=#FFFFFF]
[split B2:Y14 rows=(2,10) gap=1 as=head,body]
[split body cols=(13,11) gap=1 as=copy,visual]
!A1 32x18 [shape rect fill=#FFFFFF layer=back]
!A1 32x1 [shape rect fill=$primary layer=back]
@title = head [.title]
@body = copy [list .body bullet=disc]
@visual = visual [image fit=contain]
[end]

== Opening
[use cover]
[transition fade dur=0.4s]
@title | Quarterly Operations Review
@subtitle | Q4 highlights, risks, and next-quarter actions
@hero [image asset=hero fit=cover alt="Operations team in workshop"]

== Summary
[use summary]
[section Highlights]
[transition push dir=left dur=0.35s]
@title | What improved this quarter
@body [list bullet=disc] | Cycle time down 18% | Renewal rate up 6 points | Support backlog down 23%
@visual [image asset=chartshot fit=contain alt="Q4 performance chart"]
[notes "Start with cycle time. Use the chart only after the three summary bullets."]
```

The intended direction is simple: **make the common case short, keep shared structure inline, and keep the built-in surface small**. A normal DeckDoc should mostly be shared directives, optional layout blocks, slide headings, and compact object lines, while explicit overrides and extensions appear only when they preserve meaning that agents or presentation consumers actually need.