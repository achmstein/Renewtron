import { useState } from 'react'
import { Node, mergeAttributes, type Editor } from '@tiptap/core'
import { NodeViewContent, NodeViewWrapper, ReactNodeViewRenderer, type NodeViewProps } from '@tiptap/react'
import { Braces, Eye, GripVertical, Repeat, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { TemplateVariable } from '@/api/emailTemplates'
import { cn } from '@/lib/utils'

/*
 * The email's building blocks in the visual editor. Each one is stored as the plain HTML the layout
 * styles (a.button, table.details, div.callout), so code view and the sent email see the same thing.
 * "Show only if" and "Repeat for each" become Liquid tags on save (see liquid.ts).
 */

export interface NodeOptions { variables: TemplateVariable[] }

const labelOf = (vars: TemplateVariable[], name: string) => vars.find((v) => v.name === name)?.label

// ── Variable chip ──

/** Inside "Repeat for each name in business_names", the item ("name") is known: "Business names (each)". */
function loopLabel(props: NodeViewProps, name: string): string | undefined {
  const pos = props.getPos()
  if (typeof pos !== 'number') return undefined
  const $pos = props.editor.state.doc.resolve(pos)
  for (let d = $pos.depth; d > 0; d--) {
    const n = $pos.node(d)
    if (n.type.name !== 'repeatEach') continue
    const [item, list] = (n.attrs.expr as string).split(/\s+in\s+/)
    if (item === name) return `${labelOf((props.extension.options as NodeOptions).variables, list) ?? list} (each)`
  }
  return undefined
}

function VariableChip(props: NodeViewProps) {
  const { node, extension, selected } = props
  const vars = (extension.options as NodeOptions).variables
  const name = node.attrs.name as string
  const label = labelOf(vars, name) ?? loopLabel(props, name)
  return (
    <NodeViewWrapper
      as="span"
      title={label ? `{{ ${name} }}` : `{{ ${name} }}: not one of this email's variables. Saving will say so.`}
      className={cn(
        'mx-px inline-flex items-center rounded-md border px-1.5 align-baseline text-[0.9em] leading-snug',
        label ? 'border-info/30 bg-info/10 text-info' : 'border-warning/40 bg-warning/10 text-warning',
        selected && 'ring-2 ring-ring',
      )}
    >
      {label ?? name}
    </NodeViewWrapper>
  )
}

export const Variable = Node.create<NodeOptions>({
  name: 'variable',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  addOptions: () => ({ variables: [] }),
  addAttributes: () => ({
    name: { default: '', parseHTML: (el) => el.getAttribute('data-var'), renderHTML: (a) => ({ 'data-var': a.name }) },
  }),
  parseHTML: () => [{ tag: 'span[data-var]' }],
  renderHTML: ({ HTMLAttributes, node }) => ['span', HTMLAttributes, `{{ ${node.attrs.name} }}`],
  renderText: ({ node }) => `{{ ${node.attrs.name} }}`,
  addNodeView() { return ReactNodeViewRenderer(VariableChip) },
})

// ── Button ──

function ButtonView({ node, updateAttributes, deleteNode, extension, selected }: NodeViewProps) {
  const vars = (extension.options as NodeOptions).variables.filter((v) => v.kind === 'text')
  const [open, setOpen] = useState(false)
  const href = node.attrs.href as string
  const linkVar = /^\{\{\s*(\w+)\s*\}\}$/.exec(href)?.[1]
  return (
    <NodeViewWrapper className="my-5 text-center" data-drag-handle>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            type="button"
            contentEditable={false}
            className={cn('email-button', selected && 'ring-2 ring-ring ring-offset-2')}
            title="Edit the button"
          >
            {node.attrs.label || 'Button'}
          </button>
        </PopoverTrigger>
        <PopoverContent className="w-80 space-y-3" align="center">
          <div className="space-y-1.5">
            <Label htmlFor="btn-label">Button text</Label>
            <Input id="btn-label" value={node.attrs.label} onChange={(e) => updateAttributes({ label: e.target.value })} />
          </div>
          <div className="space-y-1.5">
            <Label>Goes to</Label>
            <Select
              value={linkVar ?? '_url'}
              onValueChange={(v) => updateAttributes({ href: v === '_url' ? (linkVar ? '' : href) : `{{ ${v} }}` })}
            >
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="_url">A web address</SelectItem>
                {vars.filter((v) => /url|link/i.test(v.name)).map((v) => (
                  <SelectItem key={v.name} value={v.name}>{v.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            {!linkVar && (
              <Input placeholder="https://" value={href} onChange={(e) => updateAttributes({ href: e.target.value })} />
            )}
          </div>
          <div className="flex justify-between">
            <Button variant="ghost" size="sm" onClick={deleteNode}><Trash2 /> Remove</Button>
            <Button size="sm" onClick={() => setOpen(false)}>Done</Button>
          </div>
        </PopoverContent>
      </Popover>
    </NodeViewWrapper>
  )
}

export const EmailButton = Node.create<NodeOptions>({
  name: 'emailButton',
  group: 'block',
  atom: true,
  draggable: true,
  addOptions: () => ({ variables: [] }),
  addAttributes: () => ({
    href: { default: '', parseHTML: (el) => el.querySelector('a')?.getAttribute('href') ?? '', renderHTML: () => ({}) },
    label: { default: 'Button', parseHTML: (el) => el.querySelector('a')?.textContent ?? '', renderHTML: () => ({}) },
  }),
  parseHTML: () => [{ tag: 'div.button-row' }],
  renderHTML: ({ node }) => ['div', { class: 'button-row' }, ['a', { class: 'button', href: node.attrs.href }, node.attrs.label]],
  addNodeView() { return ReactNodeViewRenderer(ButtonView) },
})

// ── Details table ──

export const DetailsTable = Node.create({
  name: 'detailsTable',
  group: 'block',
  content: 'detailsRow+',
  isolating: true,
  parseHTML: () => [{ tag: 'table.details' }],
  renderHTML: () => ['table', { class: 'details' }, ['tbody', 0]],
})

export const DetailsRow = Node.create({
  name: 'detailsRow',
  content: 'detailsLabel detailsValue',
  parseHTML: () => [{ tag: 'tr' }],
  renderHTML: () => ['tr', 0],
})

export const DetailsLabel = Node.create({
  name: 'detailsLabel',
  content: 'inline*',
  isolating: true,
  parseHTML: () => [{ tag: 'th' }],
  renderHTML: ({ HTMLAttributes }) => ['th', mergeAttributes(HTMLAttributes), 0],
})

export const DetailsValue = Node.create({
  name: 'detailsValue',
  content: 'inline*',
  isolating: true,
  parseHTML: () => [{ tag: 'td' }],
  renderHTML: ({ HTMLAttributes }) => ['td', mergeAttributes(HTMLAttributes), 0],
})

/** The details row the cursor is in: its position and size, or null. */
function currentRow(editor: Editor) {
  const { $from } = editor.state.selection
  for (let d = $from.depth; d > 0; d--) {
    if ($from.node(d).type.name === 'detailsRow') return { pos: $from.before(d), size: $from.node(d).nodeSize, table: $from.node(d - 1) }
  }
  return null
}

export function inDetailsTable(editor: Editor) { return currentRow(editor) !== null }

export function addDetailsRow(editor: Editor) {
  const row = currentRow(editor)
  if (!row) return
  const at = row.pos + row.size
  editor.chain().focus().insertContentAt(at, {
    type: 'detailsRow',
    content: [{ type: 'detailsLabel', content: [{ type: 'text', text: 'Label' }] }, { type: 'detailsValue' }],
  }).setTextSelection(at + 2).run()
}

export function removeDetailsRow(editor: Editor) {
  const row = currentRow(editor)
  if (!row) return
  if (row.table.childCount <= 1) {
    // The last row: remove the whole table.
    const { $from } = editor.state.selection
    for (let d = $from.depth; d > 0; d--)
      if ($from.node(d).type.name === 'detailsTable') { editor.chain().focus().deleteRange({ from: $from.before(d), to: $from.after(d) }).run(); return }
  }
  editor.chain().focus().deleteRange({ from: row.pos, to: row.pos + row.size }).run()
}

// ── Callout ──

export const TONES = [
  { value: 'info', label: 'Blue' },
  { value: 'warning', label: 'Amber' },
  { value: 'success', label: 'Green' },
  { value: 'note', label: 'Grey, small print' },
] as const

function CalloutView({ node, updateAttributes, deleteNode }: NodeViewProps) {
  const tone = node.attrs.tone as string
  return (
    <NodeViewWrapper className={cn('group/block relative', `email-callout email-callout-${tone}`)}>
      <BlockTools>
        <Select value={tone} onValueChange={(v) => updateAttributes({ tone: v })}>
          <SelectTrigger size="sm" className="h-7 bg-card text-caption"><SelectValue /></SelectTrigger>
          <SelectContent>{TONES.map((t) => <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>)}</SelectContent>
        </Select>
        <Button variant="ghost" size="icon-sm" onClick={deleteNode} aria-label="Remove callout"><Trash2 /></Button>
      </BlockTools>
      <NodeViewContent />
    </NodeViewWrapper>
  )
}

export const Callout = Node.create({
  name: 'callout',
  group: 'block',
  content: 'block+',
  defining: true,
  addAttributes: () => ({
    tone: {
      default: 'info',
      parseHTML: (el) => TONES.find((t) => el.classList.contains(`callout-${t.value}`))?.value ?? 'info',
      renderHTML: () => ({}),
    },
  }),
  parseHTML: () => [{ tag: 'div.callout' }],
  renderHTML: ({ node }) => ['div', { class: `callout callout-${node.attrs.tone}` }, 0],
  addNodeView() { return ReactNodeViewRenderer(CalloutView) },
})

// ── Show only if / Repeat for each ──

function BlockTools({ children }: { children: React.ReactNode }) {
  return (
    <div contentEditable={false} className="absolute -top-3.5 right-2 z-10 flex items-center gap-1 opacity-0 transition-opacity group-hover/block:opacity-100 group-focus-within/block:opacity-100">
      {children}
    </div>
  )
}

function ShowIfView({ node, updateAttributes, editor, getPos, extension }: NodeViewProps) {
  const flags = (extension.options as NodeOptions).variables.filter((v) => v.kind === 'flag')
  const cond = node.attrs.cond as string
  const known = flags.some((f) => f.name === cond)
  const unwrap = () => {
    const pos = getPos()
    if (typeof pos !== 'number') return
    // Keep what's inside, drop the condition.
    editor.chain().focus().command(({ tr }) => {
      tr.replaceWith(pos, pos + node.nodeSize, node.content)
      return true
    }).run()
  }
  return (
    <NodeViewWrapper className="relative my-4 rounded-lg border border-dashed border-info/50 px-3 pt-2 pb-1">
      <div contentEditable={false} className="mb-1 flex flex-wrap items-center gap-2 text-caption text-muted-foreground">
        <GripVertical className="size-3.5" data-drag-handle />
        <Eye className="size-3.5 text-info" />
        <span>Show only if</span>
        <Select value={known ? cond : '_custom'} onValueChange={(v) => v !== '_custom' && updateAttributes({ cond: v })}>
          <SelectTrigger size="sm" className="h-7 min-w-40 bg-card text-caption"><SelectValue placeholder="Pick a condition" /></SelectTrigger>
          <SelectContent>
            {flags.map((f) => <SelectItem key={f.name} value={f.name}>{f.label}</SelectItem>)}
            {!known && <SelectItem value="_custom">{cond || 'Pick a condition'}</SelectItem>}
          </SelectContent>
        </Select>
        <Button variant="ghost" size="sm" className="ml-auto h-7" onClick={unwrap}>Always show</Button>
      </div>
      <NodeViewContent />
    </NodeViewWrapper>
  )
}

export const ShowIf = Node.create<NodeOptions>({
  name: 'showIf',
  group: 'block',
  content: 'block+',
  defining: true,
  draggable: true,
  addOptions: () => ({ variables: [] }),
  addAttributes: () => ({
    cond: { default: '', parseHTML: (el) => el.getAttribute('data-if') ?? '', renderHTML: (a) => ({ 'data-if': a.cond }) },
  }),
  parseHTML: () => [{ tag: 'div[data-if]' }],
  renderHTML: ({ HTMLAttributes }) => ['div', HTMLAttributes, 0],
  addNodeView() { return ReactNodeViewRenderer(ShowIfView) },
})

function RepeatView({ node, updateAttributes, deleteNode, extension }: NodeViewProps) {
  const lists = (extension.options as NodeOptions).variables.filter((v) => v.kind === 'list')
  const [item, list] = (node.attrs.expr as string).split(/\s+in\s+/)
  return (
    <NodeViewWrapper className="relative my-4 rounded-lg border border-dashed border-success/50 px-3 pt-2 pb-1">
      <div contentEditable={false} className="mb-1 flex flex-wrap items-center gap-2 text-caption text-muted-foreground">
        <GripVertical className="size-3.5" data-drag-handle />
        <Repeat className="size-3.5 text-success" />
        <span>Repeat for each</span>
        <Select value={list} onValueChange={(v) => updateAttributes({ expr: `${item || 'item'} in ${v}` })}>
          <SelectTrigger size="sm" className="h-7 min-w-36 bg-card text-caption"><SelectValue /></SelectTrigger>
          <SelectContent>
            {lists.map((l) => <SelectItem key={l.name} value={l.name}>{l.label}</SelectItem>)}
            {!lists.some((l) => l.name === list) && <SelectItem value={list}>{list}</SelectItem>}
          </SelectContent>
        </Select>
        <span className="inline-flex items-center gap-1">
          <Braces className="size-3.5" /> each one is <code className="rounded bg-muted px-1">{item}</code>
        </span>
        <Button variant="ghost" size="icon-sm" className="ml-auto" onClick={deleteNode} aria-label="Remove"><Trash2 /></Button>
      </div>
      <NodeViewContent />
    </NodeViewWrapper>
  )
}

export const RepeatEach = Node.create<NodeOptions>({
  name: 'repeatEach',
  group: 'block',
  content: 'block+',
  defining: true,
  draggable: true,
  addOptions: () => ({ variables: [] }),
  addAttributes: () => ({
    expr: { default: 'item in list', parseHTML: (el) => el.getAttribute('data-for') ?? '', renderHTML: (a) => ({ 'data-for': a.expr }) },
  }),
  parseHTML: () => [{ tag: 'div[data-for]' }],
  renderHTML: ({ HTMLAttributes }) => ['div', HTMLAttributes, 0],
  addNodeView() { return ReactNodeViewRenderer(RepeatView) },
})
