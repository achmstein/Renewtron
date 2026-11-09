import { type ReactNode } from 'react'
import {
  createSortedRowModel,
  flexRender,
  rowSortingFeature,
  sortFns,
  tableFeatures,
  useTable,
  type CellData,
  type ColumnDef,
  type Row,
  type RowData,
  type TableFeatures,
} from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ChevronsUpDown } from 'lucide-react'
import { Skeletonizer } from '@/components/skeletonizer'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'

/**
 * TanStack Table v9: features are tree-shaken and registered explicitly.
 * Sorting is client-side within the fetched page; filtering stays server-side
 * (facets and counts come from the API).
 */
const features = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns,
})

export type DataTableFeatures = typeof features
export type DataTableColumn<TData extends RowData> = ColumnDef<DataTableFeatures, TData>
export type DataTableRow<TData extends RowData> = Row<DataTableFeatures, TData>

/**
 * Per-column extras carried in ColumnDef.meta:
 *  - sticky: pins the column to the left edge while the table scrolls horizontally
 *    (give it to the one column that identifies the row — name, ABN, contact).
 *  - className / headerClassName: alignment and responsive visibility.
 */
declare module '@tanstack/table-core' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<in out TFeatures extends TableFeatures, in out TData extends RowData, TValue extends CellData = CellData> {
    sticky?: boolean
    /** Hides the column on screens narrower than this breakpoint (phones), header and cells both. */
    hideBelow?: 'sm' | 'md' | 'lg'
    className?: string
    headerClassName?: string
  }
}

// The pinned column keeps an opaque background so scrolled cells pass under it; a right border
// marks its edge. Hover repeats the row's tint on top of the card colour.
const stickyCellClass =
  'sticky left-0 z-10 border-r bg-card group-hover/row:bg-muted'
const stickyHeadClass = 'sticky left-0 z-20 border-r bg-card'

// Spelled out in full so Tailwind sees the class names.
const HIDE_BELOW = { sm: 'hidden sm:table-cell', md: 'hidden md:table-cell', lg: 'hidden lg:table-cell' } as const

export function DataTable<TData extends RowData>({ columns, data, empty, onRowClick, loading, skeleton }: {
  columns: DataTableColumn<TData>[]
  data: TData[]
  empty: ReactNode
  onRowClick?: (row: DataTableRow<TData>) => void
  /**
   * Data is on its way. With no rows yet (the first load) the sample rows in `skeleton` show as
   * shimmering bones; with rows already shown (a new page or filter) they dim and a bar runs along the top.
   */
  loading?: boolean
  /** Sample rows drawn through the real columns as a skeleton while the first rows load. */
  skeleton?: TData[]
}) {
  const placeholder = !!loading && data.length === 0 && !!skeleton?.length
  const table = useTable({
    features,
    columns,
    data: placeholder ? skeleton! : data,
  })

  const rows = table.getRowModel().rows

  return (
    <div className="relative overflow-hidden rounded-xl border bg-card shadow-sm" aria-busy={loading || undefined}>
      {loading && !placeholder ? (
        <div className="absolute inset-x-0 top-0 z-30 h-0.5 overflow-hidden bg-link/10" role="progressbar" aria-label="Loading">
          <div className="h-full w-1/3 bg-link animate-[table-loading_1s_ease-in-out_infinite]" />
        </div>
      ) : null}
      {/* w-0 min-w-full: the scroll area takes its parent's width and never widens the page to fit the table. */}
      <Skeletonizer loading={placeholder}>
      <div className={cn('w-0 min-w-full overflow-x-auto transition-opacity', loading && !placeholder && 'pointer-events-none opacity-50')}>
        <Table>
          <TableHeader data-skeleton="keep">
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className="hover:bg-transparent">
                {headerGroup.headers.map((header) => {
                  const meta = header.column.columnDef.meta
                  const sortable = header.column.getCanSort()
                  const dir = header.column.getIsSorted()
                  return (
                    <TableHead
                      key={header.id}
                      className={cn(
                        'h-10 whitespace-nowrap px-4 text-xs font-medium text-muted-foreground',
                        meta?.sticky && stickyHeadClass,
                        meta?.hideBelow && HIDE_BELOW[meta.hideBelow],
                        meta?.headerClassName,
                      )}
                    >
                      {header.isPlaceholder ? null : sortable ? (
                        <button
                          type="button"
                          onClick={header.column.getToggleSortingHandler()}
                          className="-ml-1 inline-flex items-center gap-1 rounded px-1 py-0.5 transition-colors hover:text-foreground"
                        >
                          {flexRender(header.column.columnDef.header, header.getContext())}
                          {dir === 'asc' ? <ArrowUp className="size-3" />
                            : dir === 'desc' ? <ArrowDown className="size-3" />
                            : <ChevronsUpDown className="size-3 opacity-40" />}
                        </button>
                      ) : (
                        flexRender(header.column.columnDef.header, header.getContext())
                      )}
                    </TableHead>
                  )
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={columns.length} className="px-4 py-12">{empty}</TableCell>
              </TableRow>
            ) : rows.map((row) => (
              <TableRow
                key={row.id}
                className={cn('content-in group/row hover:bg-muted/50', onRowClick && 'cursor-pointer')}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                {row.getAllCells().map((cell) => {
                  const meta = cell.column.columnDef.meta
                  return (
                    <TableCell
                      key={cell.id}
                      className={cn('px-4 py-3 align-top text-sm', meta?.sticky && stickyCellClass, meta?.hideBelow && HIDE_BELOW[meta.hideBelow], meta?.className)}
                    >
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  )
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      </Skeletonizer>
    </div>
  )
}
