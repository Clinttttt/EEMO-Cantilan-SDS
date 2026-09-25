# StallTrack Interface Design System

**Status:** Canonical presentation guidance for the Web Office portal and shared interface work.

**Scope:** Visual hierarchy, interaction consistency, page composition, states, accessibility, and UI implementation boundaries.
**Does not override:** business rules, authorization, source-domain authority, financial calculations, or the information architecture.

## 1. Design intent

StallTrack is an operational LGU system, not a marketing site. Interfaces should feel calm, professional, credible, and fast to scan during repetitive office work.

The design system prioritizes:

1. orientation before decoration;
2. dense but readable operational information;
3. restrained semantic emphasis;
4. consistent context, filters, tables, and actions;
5. accessibility and keyboard/focus clarity;
6. tenant-derived names and labels;
7. stable reusable patterns without flattening specialized domain workflows.

Avoid visual novelty that makes routine work harder.

## 2. Canonical page anatomy

A normal office page should follow this order when the concepts apply:

1. workspace / page identity;
2. breadcrumb or contextual hierarchy;
3. active facility / account / period / as-of context;
4. filters and scope selectors;
5. primary working surface;
6. contextual actions;
7. supporting history or explanation.

Do not add a hero section, KPI mosaic, or summary card strip merely because space exists.

Use the shared composition vocabulary defined in `INFORMATION_ARCHITECTURE.md`: `WorkspaceHeader`, `Breadcrumbs`, `ScopeBar`, `FacilitySwitcher`, `FilterBar`, `ActionToolbar`, `TableShell`, `SummaryStrip`, `MoneyDisplay`, `StatusBadge`, and standard loading/empty/error/confirmation states.
## 3. Existing visual foundation

Current global tokens live in `EEMOCantilanSDS.Client/wwwroot/app.css`. Reuse semantic tokens instead of introducing isolated raw colors.

Current token families include:

- navy / navy variants for primary structure and text;
- gold / gold-light for restrained brand emphasis and active context;
- neutral page/card/background/border tokens;
- muted/subtle text tokens;
- green and red semantic state tokens;
- shared sidebar and topbar dimensions.

Current Web typography uses DM Sans for routine interface text and EB Garamond selectively for established display/title treatment.

These implementation details may evolve, but a feature page must consume the shared system rather than invent a competing palette or typography stack.

## 4. Color and emphasis

Use color to communicate hierarchy or state, not decoration.

- Neutral surfaces are the default.
- Gold is an accent, not a large background treatment.
- Green indicates positive/success/settled states only where the domain meaning supports it.
- Red indicates destructive/error/critical states only where the underlying state supports it.
- Attention and warning states should remain restrained and readable; do not paint entire tables red.
- Never infer a financial status from a color alone.
- Text labels must carry the meaning that color reinforces.

Do not introduce gradients, decorative glow, glassmorphism, arbitrary bright accents, or per-page color schemes into routine office UI.

## 5. Typography and density

Use typography to establish operational hierarchy:

- page/workspace title: strongest page identity;
- section title: identifies a bounded working area;
- table/filter labels: compact and direct;
- metadata/context: visibly secondary;
- helper text: short and behavioral, not promotional.

Prefer utility copy. A user scanning only headings, labels, dates, amounts, and statuses should understand the page.

Avoid oversized marketing headlines, aspirational copy, slogans, and repeated explanatory prose.
## 6. Containers and cards

Cards are not the default layout primitive.

Use a bordered/elevated container only when it establishes a real semantic or interaction boundary, such as:

- a self-contained form;
- a modal;
- a distinct attention group;
- a reusable summary unit;
- a panel whose controls act on that panel only.

Prefer sections, tables, rows, separators, whitespace, and clear alignment for ordinary administrative content.

Do not build dashboard-style card mosaics for data that belongs in one list, table, or summary strip.

## 7. Tables and operational lists

Tables and lists should optimize scanning:

- put the entity/context first;
- keep money, status, period, document evidence, and recorder fields visually distinct;
- align repeated numeric/date values consistently;
- keep row actions predictable and contextual;
- avoid hiding essential meaning behind icon-only controls;
- preserve source-specific terminology where one generic label would be false.

If a table mixes financial concepts, the page must state the basis and period clearly.

Do not calculate or reinterpret financial values in markup to make columns look uniform.

## 8. Filters and scope

Filters describe what is being viewed; they do not silently change business meaning.

- Facility, period, as-of date, status, and reason filters should use consistent placement and control styles.
- Keep the current selected scope visible after filtering.
- Do not label a mixed-source date filter as a universal “Collection Date” when sources use different date semantics.
- Horizontal chip/tab patterns may scroll when needed; preserve keyboard and touch usability.
- A filter that is not actually consumed by the destination page must not be presented as though it is active.

## 8.1 Charts and data visualization

Choose the simplest presentation that preserves the meaning of the data.

- Use tables when exact values and source evidence matter more than visual trend recognition.
- Use line charts for change over time and bar charts for comparisons between facilities, categories, or periods.
- Use stacked bars only when both the total and its component parts are meaningful.
- Use a calendar heatmap only for genuinely daily activity where the calendar pattern helps operations.
- Use KPI cards only for a small number of high-level summaries, not as the default page layout.
- Avoid radar charts, Sankey diagrams, network graphs, word clouds, and other complex forms unless the question genuinely requires them.

Charts must state their period, scope, money basis, and source. Do not use a chart to combine unlike financial concepts or hide unresolved/unavailable values.

## 9. Actions

Use one clear primary action per local task when possible.

- Primary: the next normal action.
- Secondary: supporting or navigational actions.
- Destructive: visually distinct and confirmation-gated when current behavior requires it.
- Sensitive actions must preserve existing role/API guards.
- Route placement never grants permission.

Action text must describe what actually happens. Do not label a read-only receipt modal “Record Payment,” or a navigation link as a mutation.
## 10. Status and terminology

Status, reason, age, urgency, and lifecycle are separate concepts.

Examples:

- Delinquent = financial/account status under the approved rule.
- Normal / Critical = follow-up urgency.
- Ended Occupancy Balance = historical relationship balance.
- Expiring = lifecycle attention, not debt.
- Missing OR = evidence/document attention, not unpaid status.

Do not collapse these concepts into one badge or one color.

Canonical actor terms:

- Vendor: temporary/TPM seller or participant where appropriate.
- Occupant: permanent rental-space holder.
- Payor: person/organization responsible for or making payment.
- Transporter: TRM actor.
- Client: slaughterhouse actor.
- Stallholder: established office/report terminology where applicable.

## 11. Money and dates

Every money view must make its basis understandable.

Where applicable, expose:

- facility;
- occupancy/term scope;
- obligation period;
- collection business date;
- as-of date;
- document issue date;
- source context.

Use Philippine business-date semantics where the source does.

Presentation code must not duplicate NPM settlement, monthly obligation, utility, delinquency, historical-rate, or other domain arithmetic.

Use the authoritative returned value and explain the basis if the figure could otherwise be misunderstood.

## 12. Standard states

Every asynchronous page should intentionally handle:

- loading;
- success with data;
- success with no data;
- recoverable failure;
- retry where appropriate;
- unavailable/unsupported data when the source can truthfully return it.

Empty state wording should use the page’s canonical terminology.

Do not invent fallback tenant data when an authoritative tenant source fails.
## 13. Confirmation and correction UX

Preserve current confirmation behavior for existing sensitive mutations.

A confirmation should state:

- what record/context is affected;
- what will change;
- whether the action is reversible under current behavior.

Do not invent a free-text correction reason if current policy does not require one.

Future AccountableDocument void/replacement rules may require stricter evidence; current OR-field corrections must not be presented as that future lifecycle.

## 14. Responsive behavior

Desktop is the main office environment, but pages must remain usable at narrow widths.

- Keep the primary working surface readable before preserving decorative spacing.
- Allow controlled horizontal scrolling for wide data where collapsing columns would destroy meaning.
- Do not hide financial basis, status, or action meaning solely to fit a narrow layout.
- Ensure tap targets remain usable on touch devices.
- Shared sidebar/drawer behavior belongs to the shared shell owner.

## 15. Accessibility

Minimum expectations:

- semantic headings in logical order;
- associated labels for inputs;
- keyboard-accessible controls;
- visible focus states;
- accessible names for icon-only actions;
- sufficient contrast;
- status meaning not dependent on color alone;
- `aria-busy` / live-region semantics where existing patterns support them;
- confirmation/error messages understandable without visual position alone.

Do not remove browser focus indication without replacing it with an equal or stronger visible focus style.

## 16. CSS and component rules

- Prefer component-scoped `.razor.css` for feature styles.
- Keep shared design tokens and genuinely global raw-SVG fixes in `wwwroot/app.css`.
- Do not add inline styles for routine component design.
- Scoped CSS must remain brace-balanced; a broken isolated stylesheet can corrupt the generated bundle.
- Reuse shared tokens; avoid feature-local copies of the same semantic color/spacing meaning.
- Visual similarity alone does not justify a shared component. Share components only when semantics are stable.

## 17. Review checklist

Before approving UI work, verify:

- Does the page belong to the correct workspace?
- Is facility/account/period context explicit?
- Are terminology and status semantics correct?
- Are current and historical states separated?
- Are financial figures source-backed rather than presentation-calculated?
- Are authorization boundaries unchanged?
- Are loading/empty/error states covered?
- Does the page reuse shared visual rules without becoming generic?
- Is the layout responsive and keyboard/focus accessible?
- Are future/hidden capabilities still hidden?
- Did the change avoid shared-shell files unless that ownership was explicit?

For substantial interface review, use `.agents/skills/stalltrack-interface-review/SKILL.md`.
