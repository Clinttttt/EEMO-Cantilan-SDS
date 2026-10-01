# Web Admin V3 visual system

Applies to the Head/Admin Blazor Client. Presentation authority remains `STALLTRACK_UI_V3_DIRECTION.md`; this note records
the consistency pass of 2026-10-01 so later pages follow one grammar.

## Colour philosophy

- Mostly calm: canvas (`--canvas`) and white work surfaces (`--surface`) carry roughly three quarters of every page.
- Blue supplies structure and identity, never decoration: civic blue (`--accent`, `--accent-soft`, `--accent-ring`) for
  interaction, selection and focus; `--civic-panel` / `--civic-panel-deep` for identity surfaces (the login panel, the
  dashboard identity band accent, icon tiles); `--surface-blue-subtle` for context bands, section headers and information
  panels on white pages.
- Navy (`--navy`) for identity text and key figures. Gold (`--gold`) only as a tiny municipal accent (a 32px rule).
- Semantic colours (`--green`, `--red`, amber) only on the status they describe, never on a whole card.
- Saturated blue never sits behind a data table.

## Surface hierarchy

1. Canvas — the page.
2. White surface — registers, tables, forms.
3. Blue-subtle band — section headers, context cells, information/status panels.
4. Civic-blue identity — login panel, small icon tiles, primary actions.

At most two elevation levels; hairline borders; radius 8–10px.

## Component grammar

- Buttons: one primary (civic blue) per local task; secondary on white with a hairline border; 40–44px tall.
- Inputs/selects: neutral white, `--border-strong`, civic-blue focus ring; no blue fill inside inputs.
- Segmented and grouped controls: one outer border and radius, internal dividers only, no gaps between segments, the
  active segment in `--accent-soft` / `--accent`, keyboard focus visible (Financial Reports Monthly | Annual and the
  Year · Month · Facility / Source Analysis group).
- Registers over card mosaics; cards only for bounded summaries and actions (Settings cards).
- Empty states: small icon, one title, one sentence, a link to the place to act.
- Typography: DM Sans, weights 400/500/600/700 only (in-between weights such as 650/730/750/800 were removed where touched).

## Page widths

Operational, reporting and configuration pages use the workspace width with sensible gutters (Revenue Setup no longer
caps at 1600px). Simple forms (login) are not stretched.

## Rejected (AI-slop) patterns

Gradient heroes, glass, blobs, glows, neon, 24px-radius cards everywhere, decorative shadows, an icon box on every line,
every KPI in a coloured tile, marketing layouts, purposeless charts, random blue shades, excessive badges, motion for
routine admin work, over-spaced empty layouts.

## Pages touched in this pass

Login (civic-blue identity panel, civic Sign In, accent focus), Dashboard (identity accent, every revenue source from the
shared projection, facility figures labelled as such, facility cards as a secondary snapshot), Settings (Backups & Restore
and Storage Usage swapped, one civic icon tile), Revenue Setup (full width, V3 tokens), WCF (tinted context cell, channel
panel and section bands, real empty state, no meter wording), Financial Reports (joined scope controls, sticky section
strip, All sources default), Collection Activity (scope stated truthfully).

## Accountable Forms exclusion

Accountable Forms is accepted as is and was not restyled. The pass added new tokens only; no shared rule that Accountable
Forms reads was changed, and the shared `SummaryStrip` was refined only through page-scoped rules (WCF).
