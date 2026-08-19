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
  'sticky left-0 z-10 bg-white shadow-[inset_-1px_0_0_#E4E4E7] group-hover/row:bg-zinc-50'
const stickyHeadClass = 'sticky left-0 z-20 bg-zinc-50 shadow-[inset_-1px_0_0_#E4E4E7]'

export function DataTable<TData extends RowData>({ columns, data, empty, onRowClick }: {
  columns: DataTableColumn<TData>[]
  data: TData[]
  empty: ReactNode
  onRowClick?: (row: DataTableRow<TData>) => void
}) {
  const table = useTable({
    features,
    columns,
    data,
  })

  const rows = table.getRowModel().rows

  return (
    <div className="overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-sm">
      <div className="overflow-x-auto">
        <Table>
          <TableHeader className="bg-zinc-50/80">
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
                        'whitespace-nowrap px-4 py-2.5 text-xxs font-mono font-medium uppercase tracking-[0.12em] text-zinc-500 border-b border-zinc-200',
                        meta?.sticky && stickyHeadClass,
                        meta?.headerClassName,
                      )}
                    >
                      {header.isPlaceholder ? null : sortable ? (
                        <button
                          type="button"
                          onClick={header.column.getToggleSortingHandler()}
                          className="inline-flex items-center gap-1 hover:text-zinc-800 transition-colors"
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
          <TableBody className="divide-y divide-zinc-100">
            {rows.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={columns.length} className="px-4 py-12">{empty}</TableCell>
              </TableRow>
            ) : rows.map((row) => (
              <TableRow
                key={row.id}
                className={cn('group/row border-0 transition-colors hover:bg-zinc-50', onRowClick && 'cursor-pointer')}
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
