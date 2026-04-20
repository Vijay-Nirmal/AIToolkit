# WorkbookDoc Language Specification

`WorkbookDoc` is a compact plain-text language for Excel and Google Sheets workbooks.

This version is intentionally **row-first**:

1. normal sheet data is written as anchored row lines,
2. cells in a row are separated with `|`,
3. explicit `[cell ...]` lines are only for exceptions that are too detailed for the compact row form.

That keeps workbook text short enough for agent prompts while still preserving coordinates, gaps, merges, formulas, and common workbook features.

## At a Glance

| Item | Value |
| --- | --- |
| Canonical extension | `.wbdoc` |
| Encoding | UTF-8 |
| Workbook start | `= Workbook Name` |
| Sheet start | `== Sheet Name` |
| Main sheet body | anchored row lines such as `@B5 | a | b | c` |
| Compact row default | row lines |
| Detailed override | `[cell ...]` |
| Shared built-ins | `[style ...]`, `[name ...]` |
| Sheet built-ins | `[state ...]`, `[view ...]`, `[used ...]` |
| Other built-ins | `[merge ...]`, `[type ...]`, `[fmt ...]`, `[cf ...]`, `[table ...]`, `[pivot ...]`, `[chart ...]`, `[spark ...]` |
| Not built in | tab color, column widths, row heights, page layout, filter state, filter views, images, protection |
| Extension form | `[x ...]` |

## Core Model

A `WorkbookDoc` file is made of:

1. one workbook title,
2. zero or more workbook attributes,
3. zero or more shared directives,
4. one or more sheets,
5. zero or more sheet directives per sheet,
6. zero or more anchored row lines per sheet,
7. zero or more explicit cell overrides per sheet,
8. zero or more non-cell directives per sheet.

The compact row line is the canonical default. Use explicit `[cell ...]` only when the compact row line would become awkward or lossy.

Emit literal WorkbookDoc characters, not JSON unicode escapes such as `\u0022`, `\u0027`, `\u003E`, or `\u003C`.

## Quick Example

```text
= Project Management Data
:wbdoc: 4
:locale: en-US
:date-system: 1900
:active: Project Management Data

[style title bold fg=#FFFFFF bg=#4472C4 align=center/middle]
[style hdr bold bg=#D9E2F3]

== Project Management Data
[view freeze=5,2 zoom=90 grid]
[used B3:H50]

@B3 [.title] | "Project Management Data"
[merge B3:H3 align=center/middle]
@B5 [.hdr] | Project Name | Task Name | Assigned to | Start Date | Days Required | End Date | Progress
@B6 | Marketing | Market Research | Alice | date("2024-01-01") | 13 | date("2024-01-14") | 0.78
@B7 | Marketing | Content Creation | Bob | date("2024-01-14") | 14 | date("2024-01-28") | 1
@B8 | Marketing | Social Media Planning | Charlie | date("2024-01-28") | 22 | date("2024-02-19") | 0.45

[cf H6:H50 when cell < 0.5 fill=#FFF2CC]
[table ProjectTasks B5:H50 filter banded]
```

## Workbook Header

Every file starts with one workbook title:

```text
= Workbook Name
```

Workbook-level settings use AsciiDoc-style attribute lines.

## Built-in Workbook Attributes

| Attribute | Meaning | Example |
| --- | --- | --- |
| `:wbdoc:` | Language version | `:wbdoc: 4` |
| `:locale:` | Locale hint | `:locale: en-US` |
| `:date-system:` | Serial date system | `:date-system: 1900` |
| `:recalc:` | Recalculation mode | `:recalc: auto` |
| `:active:` | Initially active sheet | `:active: Summary` |

Rules:

- `:wbdoc:` is required for canonical files.
- `:date-system:` may be `1900` or `1904`.
- `:recalc:` may be `auto` or `manual`.
- `:active:` must match a sheet name when present.

## Shared Built-in Directives

Shared directives normally appear before the first sheet.

### `[style ...]`

Defines a reusable cell style.

```text
[style <name> <style-entry>...]
```

Examples:

```text
[style title bold fg=#FFFFFF bg=#4472C4 align=center/middle]
[style currency fmt="$#,##0.00" align=right/middle]
```

Built-in style entries:

| Entry | Meaning |
| --- | --- |
| `fmt="..."` | Number format |
| `fg=#RRGGBB` | Font color |
| `bg=#RRGGBB` | Fill color |
| `bold` | Bold text |
| `italic` | Italic text |
| `underline` | Underline |
| `strike` | Strikethrough |
| `font="Name"` | Font family |
| `size=<n>` | Font size in points |
| `align=horizontal/vertical` | Alignment |
| `wrap` / `clip` / `overflow` | Text flow |
| `border=side:line:color` | Border shorthand |
| `locked` | Locked cell |
| `hide-formula` | Hide formula text |

### `[name ...]`

Defines a named range or named formula.

```text
[name <name> = <reference-or-formula>]
```

Examples:

```text
[name TaxRate = Inputs!B2]
[name ActiveSales = =FILTER(Data!A2:F200,Data!G2:G200="Active")]
```

## Sheets

Each sheet starts with:

```text
== Sheet Name
```

## Built-in Sheet Directives

Sheet directives usually appear below the sheet heading and before the first row line.

The built-in sheet surface is intentionally small. Only directives that commonly affect how a sheet is interpreted or opened are built in. Layout cosmetics such as tab color, exact column widths, exact row heights, and print-page setup are not built in.

### `[state ...]`

What it is: a portable sheet visibility directive. Use it when a sheet exists in the workbook but should not be shown by default.

```text
[state hidden]
```

Built-in value:

| Value | Meaning |
| --- | --- |
| `hidden` | Sheet is hidden |

### `[view ...]`

What it is: a lightweight viewing hint for the initial sheet experience. Use it for common first-open behavior such as frozen headers or visible gridlines.

```text
[view <entry>...]
```

Examples:

```text
[view freeze=1,1 zoom=90 grid]
[view freeze=5,2]
```

Built-in entries:

| Entry | Meaning |
| --- | --- |
| `freeze=<rows>,<cols>` | Freeze panes |
| `zoom=<n>` | Zoom percentage |
| `grid` | Show gridlines |

### `[used ...]`

What it is: the intended occupied rectangle of the sheet. Use it when the meaningful worksheet starts away from `A1`, when outer blank framing is intentionally omitted, or when a consumer should keep stable preview bounds.

```text
[used <range>]
```

Examples:

```text
[used B3:H50]
[used A1:K200]
```

This is important when leading rows or columns are blank, when the author wants stable sheet bounds, or when a compact export intentionally omits outer blank rows.

## What Is Not Built In

The following are intentionally **not** part of the WorkbookDoc built-in surface:

- tab color,
- exact column widths,
- exact row heights,
- page and print layout,
- active filter state,
- saved filter views,
- images,
- protection rules,
- provider-specific theme palettes,
- provider-specific chart extras,
- macros and scripting.

Those details may still matter in a host application, but they are less useful for agent-facing spreadsheet text than semantic sheet content, formulas, merges, conditional formatting, and charts. If a workflow needs them, use `[x ...]`.

Examples:

```text
[x excel tab-color=#4472C4]
[x layout cols="B:C=18ch, D=24ch"]
[x layout rows="3=24pt"]
[x print print=B3:H50 repeat-rows=5:5 orient=landscape]
[x filter B5:H50 where B eq "Marketing" sort F asc]
[x filter-view "Open Items" B5:H50 where H lt 0.5]
[x image "./assets/logo.png" at=B2 size=160x48px]
[x protect sheet select-locked]
```

## Addressing and Ranges

WorkbookDoc uses A1 notation everywhere.

| Form | Example |
| --- | --- |
| Single cell | `B3` |
| Rectangle | `B5:H50` |
| Whole column | `B:B` |
| Whole row | `5:5` |
| Quoted sheet name | `'North America'!B2` |
| Absolute reference | `$B$5:$H$50` |

## Anchored Row Lines

Anchored row lines are the compact default for normal sheet data.

Syntax:

```text
@<address> [<row-attrlist>] | <cell> | <cell> | <cell> ...
```

Examples:

```text
@B5 [.hdr] | Project Name | Task Name | Assigned to | Start Date | Days Required | End Date | Progress
@B6 | Marketing | Market Research | Alice | date("2024-01-01") | 13 | date("2024-01-14") | 0.78
@B7 | Marketing | Content Creation | Bob | date("2024-01-14") | 14 | date("2024-01-28") | 1
```

Rules:

1. The address is the first cell of the row.
2. Every later cell address is inferred by position.
3. Use `blank` for internal gaps when later cells in the same row still matter.
4. Trailing blank cells may be omitted.
5. Fully blank rows should usually be omitted; later row anchors preserve vertical gaps naturally.

### Row Attrlist

The optional attrlist after the row anchor applies defaults to every cell in that row.

```text
@B5 [.hdr] | Project Name | Task Name | Assigned to
```

That is shorthand for applying `.hdr` to each cell in the row unless an individual cell overrides it.

## Cell Tokens

Each cell token in a row may have an optional compact attrlist prefix:

```text
[.currency fmt="$#,##0"] 154200
[fmt="0.0%"] 0.78
[link="https://example.com"] Open
```

Attrlist shape:

```text
[.style-name key=value flag] cell-content
```

Built-in cell attrlist entries:

| Entry | Meaning |
| --- | --- |
| `.style-name` | Apply a named style |
| `fmt="..."` | Number format |
| `fg=#RRGGBB` | Font color |
| `bg=#RRGGBB` | Fill color |
| `bold` / `italic` / `underline` / `strike` | Text styling flags |
| `font="Aptos"` | Font family |
| `size=<n>` | Font size |
| `align=horizontal/vertical` | Alignment |
| `wrap` / `clip` / `overflow` | Text flow |
| `border=side:line:color` | Border shorthand |
| `link="..."` | Hyperlink |
| `result=<literal>` | Optional cached displayed value for a formula cell |
| `span=<n>` | Horizontal merge span |
| `locked` | Locked cell |
| `hide-formula` | Hide formula text |

The attrlist is intentionally small. If a workbook needs much more detail than this on many cells, explicit cell overrides are usually clearer.

Writers should omit style entries that only restate defaults. If `fg`, `bg`, alignment, or emphasis is not written, readers should assume the workbook's normal default formatting.

In particular, exporters should omit default alignment such as `align=left/middle`.

## Cell Content Forms

Cell tokens and explicit cell overrides share the same content forms.

| Source form | Meaning |
| --- | --- |
| `1250` | Number |
| `true` / `false` | Boolean |
| `blank` | Explicit blank |
| `=SUM(B2:B10)` | Formula |
| `date("2026-04-18")` | Date literal |
| `time("09:30:00")` | Time literal |
| `datetime("2026-04-18T09:30:00Z")` | Date-time literal |
| `#DIV/0!` | Error literal |
| `January` | Plain text |
| `"  January  "` | Quoted text |
| `rich("Status: [b]At Risk[/b]")` | Rich text |

### Optional Formula Cache Form

For imports that need to preserve both a formula and its last displayed result, the cell content stays the formula and an optional `result=...` attr stores the cached displayed value.

Examples:

```text
[result=1250] =SUM(B2:B10)
[cell C8 result=154200] =SUM(Data!B2:B5)
```

This form is optional. It is mainly useful for Excel to WorkbookDoc fidelity. WorkbookDoc to Excel writers may ignore the cached result and write only the formula.

### Quoted Strings

Use double quotes when text needs:

- leading or trailing spaces,
- a literal `|`,
- a leading `=`,
- a reserved literal such as `blank`,
- line breaks,
- or text that would otherwise parse as another literal kind.

Supported escapes:

| Escape | Meaning |
| --- | --- |
| `\"` | Literal quote |
| `\\` | Literal backslash |
| `\n` | Line break |
| `\t` | Tab |
| `\|` | Literal pipe |

## Explicit Cell Overrides

Use `[cell ...]` only when a single cell needs more detail than the compact row form should carry.

Syntax:

```text
[cell <address> <entry>...] [literal]
```

Examples:

```text
[cell C8 result=154200 style=currency] =SUM(Data!B2:B5)
[cell D8 rich="Status: [b][color=#C00000]At Risk[/color][/b]"]
[cell E8 link="https://example.com"] "Open"
```

Recommended uses:

1. a formula with cached result that should both survive,
2. rich text that is awkward inline,
3. a blank cell with meaningful metadata,
4. an exact per-cell override in an otherwise compact row-based sheet.

## Merges

### Inline horizontal span

For short horizontal merges inside a row:

```text
@B3 [.title span=7] | "Project Management Data"
```

Covered cells are omitted from the row line.

### `[merge ...]`

For any merge, especially multi-row merges:

```text
[merge <range> [align=horizontal[/vertical]]]
```

The top-left cell owns the value and formula.

Use `align=` on the merge when the merge itself implies presentation, such as Excel's merge-and-center behavior.

Example:

```text
[merge B3:H3 align=center/middle]
```

## Built-in Non-cell Features

### `[type ...]`

What it is: a shared data kind over a range. Use it when adjacent cells repeat the same explicit data kind and writing that kind inline on every cell would be noisy.

```text
[type <range> <kind>]
```

Examples:

```text
[type E6:E50 date]
[type F6:F50 datetime]
```

Built-in kinds:

| Kind | What it means |
| --- | --- |
| `date` | Cells in the range should be read as date values |
| `time` | Cells in the range should be read as time values |
| `datetime` | Cells in the range should be read as date-time values |

Rules:

1. Prefer `[type ...]` only when it makes repeated adjacent typed cells shorter.
2. When `[type ...]` is present, cells in that range should usually use raw values such as `2026-04-18` instead of repeating `date("2026-04-18")` on every cell.
3. Inline typed literals such as `date("2026-04-18")` are still valid for one-off cells.
4. Explicit `[cell ...]` content overrides `[type ...]` when needed.

### `[fmt ...]`

What it is: a shared non-default cell format over a range. Use it when repeating the same inline cell attrlist across adjacent cells would be noisy.

```text
[fmt <range> <style-entry>...]
```

Examples:

```text
[fmt B5:H5 bold bg=#D9E2F3]
[fmt H6:H50 fmt="0.0%" align=right/middle]
```

Rules:

1. `[fmt ...]` uses the same built-in style entries as cell attrlists.
2. It applies defaults to every cell in the target range.
3. Inline cell attrs and explicit `[cell ...]` details override `[fmt ...]`.
4. Prefer row attrlists for full-row patterns such as header rows; use `[fmt ...]` when the shared formatting is better expressed as a range.

### `[cf ...]`

What it is: conditional formatting. Use it when formatting depends on cell values, formulas, scales, bars, or icon sets.

```text
[cf <range> <rule> <format-entry>...]
```

Examples:

```text
[cf H6:H50 when cell < 0.5 fill=#FFF2CC]
[cf H6:H50 data-bar(color=#5B9BD5)]
```

Built-in conditional-format rule forms:

| Rule form | What it means | Example |
| --- | --- | --- |
| `when cell <value-op> value` | Format depends on the cell's own value compared against a threshold | `when cell > 0.15` |
| `when text contains "..."` | Format depends on whether cell text contains a substring | `when text contains "Risk"` |
| `when formula(...)` | Format depends on a formula evaluated relative to the top-left cell of the target range | `when formula(=H6<0.5)` |
| `scale(...)` | Apply a color scale across the whole range | `scale(min:#F8696B,50%:#FFEB84,max:#63BE7B)` |
| `data-bar(...)` | Show an in-cell bar whose length reflects the value | `data-bar(color=#5B9BD5)` |
| `icon-set(...)` | Show an icon chosen from thresholds or buckets | `icon-set(3-arrows)` |

Built-in conditional-format entries:

| Entry | What it means |
| --- | --- |
| `priority=<n>` | Lower numbers run first |
| `stop` | Later rules stop after this one matches |
| `fill=#RRGGBB` | Fill color to apply |
| `fg=#RRGGBB` | Font color to apply |
| `bold` / `italic` / `underline` | Text styling to apply |

Providers may approximate rule families they cannot render natively, but the WorkbookDoc rule text stays the same.

### `[table ...]`

What it is: structured-table behavior over a range. Use it when a normal cell rectangle should behave like an Excel or Sheets data table with filters, totals, or banding.

```text
[table <name> <range> <entry>...]
```

Built-in table entries:

| Entry | What it means |
| --- | --- |
| `filter` | The table exposes filter controls |
| `totals` | The table includes a totals row |
| `banded` | The table uses alternating row banding |
| `style=<name>` | The table uses a named table-style token |

### `[pivot ...]`

What it is: a pivot table definition. Use it when a sheet contains an aggregated pivot view derived from a source range or table.

```text
[pivot <name> source=<source> at=<cell>]
- row <field>
- col <field>
- val <field> <agg> [as "<label>"]
[end]
```

`source=` must point to a table name or to a range whose first row is the header row used by the pivot field names.

Built-in pivot line kinds:

| Line kind | What it means | Example |
| --- | --- | --- |
| `row <field>` | Add a row grouping field | `- row Region` |
| `col <field>` | Add a column grouping field | `- col Quarter` |
| `val <field> <agg> [as "<label>"]` | Add an aggregated value field | `- val Revenue sum as "Sum of Revenue"` |
| `filter <field>` | Add a pivot filter field | `- filter Status` |

### `[chart ...]`

What it is: a chart definition. Use it when the workbook includes a chart whose series, placement, and type should survive round-tripping.

```text
[chart "<name>" type=<type> at=<cell> size=<width>x<height><unit>]
- series <type> "<label>" cat=<range> val=<range> [size=<range>] [axis=primary|secondary] [color=#RRGGBB] [labels]
[end]
```

Built-in chart types:

| Type | What it means | Required series shape | Notes |
| --- | --- | --- | --- |
| `column` | Vertical bars comparing values across categories | `cat` + `val` | Best for part-by-part comparison |
| `bar` | Horizontal bars comparing values across categories | `cat` + `val` | Same data model as `column`, rotated |
| `line` | Connected points showing change across an ordered sequence | `cat` + `val` | Best when category order matters |
| `area` | A filled line chart emphasizing magnitude over a sequence | `cat` + `val` | Same data model as `line` |
| `pie` | Part-to-whole slices from one series | `cat` + `val` | Portable built-in case is one series |
| `doughnut` | Ring-style part-to-whole chart | `cat` + `val` | Portable built-in case is one series |
| `scatter` | Numeric x/y point chart | `cat` + `val` | `cat` is treated as x-values |
| `bubble` | Scatter chart with a third size dimension | `cat` + `val` + `size` | `size` is required for bubble series |
| `combo` | Mixed chart made from per-series types | per-series type decides shape | Usually mixes `column` and `line` |
| `radar` | Categories placed around a radial axis | `cat` + `val` | Best for profile or score comparisons |

Built-in chart series types:

| Series type | Where it may appear | What it means |
| --- | --- | --- |
| `column` | `column`, `combo` | Draw the series as vertical bars |
| `bar` | `bar`, `combo` | Draw the series as horizontal bars |
| `line` | `line`, `area`, `combo`, `radar` | Draw the series as a connected line |
| `area` | `area`, `combo` | Draw the series as a filled line/area |
| `pie` | `pie`, `doughnut` | Draw the series as slices |
| `scatter` | `scatter` | Draw the series as numeric x/y points |
| `bubble` | `bubble` | Draw the series as sized numeric x/y points |

Bubble example:

```text
[chart "Risk Matrix" type=bubble at=J2 size=480x280px]
- series bubble "Risks" cat=B6:B20 val=C6:C20 size=D6:D20 color=#4472C4
[end]
```

### `[spark ...]`

What it is: a sparkline definition. Use it for tiny in-cell trend graphics driven by a source range.

```text
[spark <cell> source=<range> type=<type> [color=#RRGGBB] [negative=#RRGGBB]]
```

Built-in sparkline types:

| Type | What it means | Notes |
| --- | --- | --- |
| `line` | A tiny line chart showing movement across the source values | Best for trends |
| `column` | A tiny bar chart with one vertical bar per source value | Best for compact magnitude comparison |
| `winloss` | A tiny positive/negative outcome chart | Positive values are wins, negative values are losses, zero is neutral |

## Length and Size Units

Built-in length units:

- `px`
- `pt`
- `in`
- `cm`
- `mm`

## Extensions

Provider-specific or intentionally non-built-in details should use `[x ...]`.

Examples:

```text
[x excel very-hidden]
[x google-sheets]
- chart-type geo
- region world
[end]
```

Unknown extensions must round-trip unchanged.

## Canonical Serialization Rules

To keep diffs stable:

1. write workbook attributes first,
2. then shared directives in this order: `style`, `name`,
3. then sheets in workbook order,
4. inside a sheet, emit sheet directives in this order when present: `state`, `view`, `used`,
5. then emit shared range directives in this order when present: `type`, `fmt`,
6. then emit anchored row lines in row order,
7. then emit explicit `[cell ...]` overrides only when needed,
8. then emit standalone non-cell directives in this order: `merge`, `validate`, `cf`, `table`, `pivot`, `chart`, `spark`, `x`.

Compact export rules:

- trim outer fully blank rows and columns when that does not lose intended meaning,
- keep internal blanks with `blank`,
- omit formatting that only restates defaults,
- omit default alignment such as `align=left/middle`,
- hoist repeated non-default formatting into row attrlists or `[fmt ...]` ranges when that makes the text shorter,
- hoist repeated adjacent explicit data kinds into `[type ...]` ranges when that makes the text shorter,
- prefer row lines over `[cell ...]`,
- prefer inline formulas over verbose cell records,
- keep style metadata only when it is likely useful to downstream agents or required for fidelity.

## Complete Compact Example

```text
= Quarterly Operations
:wbdoc: 4
:locale: en-US
:date-system: 1900
:recalc: auto
:active: Summary

[style title bold fg=#FFFFFF bg=#4472C4 align=center/middle]
[style hdr bold bg=#D9E2F3]
[style currency fmt="$#,##0" align=right/middle]

== Summary
[view freeze=1,1 zoom=90 grid]
[used A1:D6]

@A1 [.title] | "Quarterly Operations"
[merge A1:D1]
@A3 [.hdr] | Month | Revenue | Margin % | Trend
@A4 | January | [.currency] =SUM(Data!B2:B5) | [fmt="0.0%"] =B4/SUM(Data!C2:C5) | blank
@A5 | February | [.currency] =SUM(Data!B6:B9) | [fmt="0.0%"] =B5/SUM(Data!C6:C9) | blank
@A6 | March | [.currency] =SUM(Data!B10:B13) | [fmt="0.0%"] =B6/SUM(Data!C10:C13) | blank

[spark D4 source=B4:C4 type=line color=#4472C4]
[spark D5 source=B5:C5 type=line color=#4472C4]
[spark D6 source=B6:C6 type=line color=#4472C4]
[chart "Revenue Trend" type=combo at=F2 size=720x360px]
- series column "Revenue" cat=A4:A6 val=B4:B6 color=#4472C4
- series line "Margin %" cat=A4:A6 val=C4:C6 axis=secondary color=#C00000 labels
[end]
```

The intended direction is now simple: **make the common case short and the built-in surface small**. A normal sheet should mostly be row lines, while explicit cell records and richer directives appear only when they preserve meaning that agents or workbook consumers actually need.
