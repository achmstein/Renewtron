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
    className?: string
    headerClassName?: string
  }
}

const stickyCellClass =
  'sticky left-0 z-10 bg-card shadow-[inset_-1px_0_0_#E4E4E7] group-hover/row:bg-muted/50'
const stickyHeadClass = 'sticky left-0 z-20 bg-muted/50 shadow-[inset_-1px_0_0_#E4E4E7]'

export function DataTable<TData extends RowData>({ columns, data, empty, onRowClick, loading }: {
  columns: DataTableColumn<TData>[]
  data: TData[]
  empty: ReactNode
  onRowClick?: (row: DataTableRow<TData>) => void
  /** A new page or filter is on its way: the rows dim and a bar runs along the top. */
  loading?: boolean
}) {
  const table = useTable({
    features,
    columns,
    data,
  })

  const rows = table.getRowModel().rows

  return (
    <div className="relative overflow-hidden rounded-xl border border-border bg-card shadow-sm" aria-busy={loading || undefined}>
      {loading ? (
        <div className="absolute inset-x-0 top-0 z-30 h-0.5 overflow-hidden bg-primary/10" role="progressbar" aria-label="Loading">
          <div className="h-full w-1/3 bg-primary animate-[table-loading_1s_ease-in-out_infinite]" />
        </div>
      ) : null}
      <div className={cn('overflow-x-auto transition-opacity', loading && 'opacity-50 pointer-events-none')}>
        <Table>
          <TableHeader className="bg-muted/50">
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
                        'whitespace-nowrap px-4 py-2.5 text-xs font-medium text-muted-foreground border-b border-border',
                        meta?.sticky && stickyHeadClass,
                        meta?.headerClassName,
                      )}
                    >
                      {header.isPlaceholder ? null : sortable ? (
                        <button
                          type="button"
                          onClick={header.column.getToggleSortingHandler()}
                          className="inline-flex items-center gap-1 hover:text-foreground transition-colors"
                        >
                          {flexRender(header.column.columnDef.header, header.getContext())}
                          {dir === 'asc' ? <ArrowUp className="h-3 w-3" />
                            : dir === 'desc' ? <ArrowDown className="h-3 w-3" />
                            : <ChevronsUpDown className="h-3 w-3 opacity-40" />}
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
          <TableBody className="divide-y divide-border">
            {rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={columns.length} className="px-4 py-12">{empty}</TableCell>
              </TableRow>
            ) : rows.map((row) => (
              <TableRow
                key={row.id}
                className={cn('group/row border-0 transition-colors hover:bg-muted/50', onRowClick && 'cursor-pointer')}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                {row.getAllCells().map((cell) => {
                  const meta = cell.column.columnDef.meta
                  return (
                    <TableCell
                      key={cell.id}
                      className={cn('px-4 py-3 align-top', meta?.sticky && stickyCellClass, meta?.className)}
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
    </div>
  )
}
