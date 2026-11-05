# Admin style

The admin is built on shadcn/ui (`src/components/ui`) with its own theme: the tokens under
`.admin` in `src/index.css`, switched on by `AdminLayout` (it puts `admin` on `<html>`). The
public site keeps its own look; nothing here applies to it.

Aim: focused and friendly. An operator should see what matters at a glance.

## Colour: each colour has one job

| Colour | Token / class | Used for |
|---|---|---|
| Black | `primary` (`<Button>` default) | The one main action in an area. At most one per card or header. |
| Brand blue | `text-link`, `ring` | Links (client names, record links), selection, focus. Never a button fill. |
| Red / amber / indigo / green / grey | `destructive`, `warning`, `info`, `success`, `muted` | Status only (see `_status.tsx`), never decoration. |
| Greys | `foreground`, `muted-foreground`, `border`, `muted`, `card`, `background` | Everything else. |

Never write raw palette colours (`zinc-*`, `gray-*`, `brand-*`, `red-*`, hex values) in admin pages.
Use the tokens: `text-foreground`, `text-muted-foreground`, `bg-card`, `bg-muted`, `border` (the
default border colour is already the theme's), `text-destructive`, `bg-success/10` and so on.

## Words

- Sentence case everywhere. No uppercase, no mono "kicker" labels, no letter-spacing.
- One title and, at most, one short line under it. No explanatory paragraphs on cards; put the
  explanation in a `title` hover, in a Tooltip, or on the record page.
- Rows: one line saying what happened (naming the client), one line of facts (when · how much · due).
- Prefer friendly words: "All clear", "Nothing here yet", "3 things need you".

## Components (use these; don't hand-roll)

- Surfaces: `Card` (+ `CardHeader`, `CardTitle`, `CardDescription`, `CardAction`, `CardContent`).
  Lists of rows sit in `Card className="gap-0 py-0"` or the `Queue` from `_rows.tsx`.
- Buttons: `Button`. Main action `variant="default"` (black); everything else `variant="outline"`;
  quiet links in a header `variant="ghost"`. Sizes: page toolbar `size="sm"`, row actions `size="sm"`,
  icon buttons `size="icon-sm"`. Links that look like buttons: `<Button asChild><Link …/></Button>`.
- Status: `<Status kind=… status=…/>` from `_status.tsx` (soft Badge with a dot). Other small
  labels: `Badge` (`secondary`, `outline`).
- Tables: `DataTable` (`src/components/data-table.tsx`) or `Table` from `components/ui/table`.
- Tabs and filters: `Tabs`, `ToggleGroup`, `FacetedFilter`. Forms: `Input`, `Textarea`, `Select`,
  `Checkbox`, `Label` (`Field` in `_ui.tsx` pairs a Label with an input).
- Messages: `Alert` for a problem on the page; `sileo` toasts for results of an action.
- Loading: `Skeleton`. Empty: `EmptyState` from `_ui.tsx`.
- Record pages: `RecordCrumbs`, `RecordHeader`, `NextStep`, `StoryStrip`, `TechnicalDetail` from `_record.tsx`.
- List pages: `PageHeader`, `SectionTitle`, `StatTile`, `ViewToggle`, `RefreshButton` from `_ui.tsx`.
- Use `cn()` from `@/lib/utils` to combine classes.

## Type and spacing

- Page title `text-2xl font-semibold tracking-tight`; section/card title `text-base font-semibold`
  (or `CardTitle`); body `text-sm`; facts and hints `text-xs text-muted-foreground`.
- `font-mono` only for identifiers people copy or compare: ABNs, keys, references, ids, amounts in tables.
- No arbitrary values (`text-[13px]`, `tracking-[0.14em]`, `w-[7rem]`) when a scale step exists.
- Page container: `mx-auto max-w-5xl` (or `max-w-7xl` for wide tables) `px-4 py-8 sm:px-6 lg:px-8`.
