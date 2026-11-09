import { useState } from 'react'
import { nodeInputRule } from '@tiptap/core'
import { EditorContent, useEditor, useEditorState, type Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import {
  Bold, Braces, ChevronDown, Eye, Heading2, Heading3, Italic, Link2, List, ListOrdered, MousePointerClick,
  MessageSquareText, Plus, Repeat, Rows3, TableProperties, Trash2, Underline,
} from 'lucide-react'
import { sileo } from 'sileo'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import type { TemplateVariable } from '@/api/emailTemplates'
import { cn } from '@/lib/utils'
import {
  addDetailsRow, Callout, DetailsLabel, DetailsRow, DetailsTable, DetailsValue, EmailButton, inDetailsTable,
  removeDetailsRow, RepeatEach, ShowIf, Variable,
} from './nodes'
import './email-canvas.css'

/*
 * The visual editor: write the email like a document. Variables are chips (Insert → Variable, or type
 * {{name}}), and the blocks the layout knows how to style come from the Insert menu. What it writes is
 * converted back to the stored HTML + Liquid by liquid.ts.
 */

const KIND_LABEL = { text: 'Details', flag: 'Yes / no (for "Show only if")', list: 'Lists (for "Repeat for each")' } as const

export function VisualEditor({ html, variables, onChange }: {
  html: string
  variables: TemplateVariable[]
  onChange: (editorHtml: string) => void
}) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        codeBlock: false,
        code: false,
        blockquote: false,
        horizontalRule: false,
        link: { openOnClick: false, autolink: true, HTMLAttributes: { rel: null, target: null } },
      }),
      Variable.configure({ variables }).extend({
        // Typing {{business_name}} makes a chip. No capture group: nodeInputRule replaces only the
        // captured part when there is one, which would leave the braces behind.
        addInputRules() {
          return [nodeInputRule({
            find: /\{\{\s*[A-Za-z_][\w.]*\s*\}\}$/,
            type: this.type,
            getAttributes: (m) => ({ name: m[0].replace(/[{}\s]/g, '') }),
          })]
        },
      }),
      EmailButton.configure({ variables }),
      DetailsTable, DetailsRow, DetailsLabel, DetailsValue,
      Callout,
      ShowIf.configure({ variables }),
      RepeatEach.configure({ variables }),
    ],
    content: html,
    immediatelyRender: true,
    editorProps: { attributes: { class: 'email-canvas min-h-80 px-6 py-5 focus:outline-none', spellcheck: 'true' } },
    onCreate: ({ editor }) => onChange(editor.getHTML()),
    onUpdate: ({ editor }) => onChange(editor.getHTML()),
  })

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <Toolbar editor={editor} variables={variables} />
      <div className="min-h-0 flex-1 overflow-y-auto bg-muted/40 p-3 sm:p-5">
        <div className="mx-auto max-w-[600px] rounded-lg border bg-card shadow-xs">
          <EditorContent editor={editor} />
        </div>
      </div>
    </div>
  )
}

function Toolbar({ editor, variables }: { editor: Editor; variables: TemplateVariable[] }) {
  const state = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e.isActive('bold'), italic: e.isActive('italic'), underline: e.isActive('underline'),
      h2: e.isActive('heading', { level: 2 }), h3: e.isActive('heading', { level: 3 }),
      bullet: e.isActive('bulletList'), ordered: e.isActive('orderedList'), link: e.isActive('link'),
      inTable: inDetailsTable(e),
    }),
  })
  const flags = variables.filter((v) => v.kind === 'flag')
  const lists = variables.filter((v) => v.kind === 'list')

  const insertCallout = () => editor.chain().focus().insertContent({
    type: 'callout', attrs: { tone: 'info' },
    content: [{ type: 'paragraph', content: [{ type: 'text', marks: [{ type: 'bold' }], text: 'Heads up' }] }, { type: 'paragraph' }],
  }).run()
  const insertButton = () => editor.chain().focus().insertContent({
    type: 'emailButton', attrs: { label: 'Open your Business Portal', href: variables.some((v) => v.name === 'portal_url') ? '{{ portal_url }}' : 'https://' },
  }).run()
  const insertTable = () => editor.chain().focus().insertContent({
    type: 'detailsTable',
    content: [{ type: 'detailsRow', content: [{ type: 'detailsLabel', content: [{ type: 'text', text: 'Label' }] }, { type: 'detailsValue' }] }],
  }).run()
  const wrapShowIf = () => {
    if (!flags.length) return
    if (!editor.chain().focus().wrapIn('showIf', { cond: flags[0].name }).run())
      sileo.error({ title: 'Put the cursor in a paragraph (not a table) to make it conditional.' })
  }
  const insertRepeat = () => {
    if (!lists.length) return
    editor.chain().focus().insertContent({
      type: 'repeatEach', attrs: { expr: `item in ${lists[0].name}` },
      content: [{ type: 'paragraph', content: [{ type: 'variable', attrs: { name: 'item' } }] }],
    }).run()
  }

  return (
    <div className="flex flex-wrap items-center gap-0.5 border-b bg-card px-2 py-1.5">
      <Tool on={state.bold} label="Bold" onClick={() => editor.chain().focus().toggleBold().run()}><Bold /></Tool>
      <Tool on={state.italic} label="Italic" onClick={() => editor.chain().focus().toggleItalic().run()}><Italic /></Tool>
      <Tool on={state.underline} label="Underline" onClick={() => editor.chain().focus().toggleUnderline().run()}><Underline /></Tool>
      <LinkTool editor={editor} active={state.link} />
      <Separator orientation="vertical" className="mx-1 h-5!" />
      <Tool on={state.h2} label="Heading" onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}><Heading2 /></Tool>
      <Tool on={state.h3} label="Small heading" onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}><Heading3 /></Tool>
      <Tool on={state.bullet} label="Bulleted list" onClick={() => editor.chain().focus().toggleBulletList().run()}><List /></Tool>
      <Tool on={state.ordered} label="Numbered list" onClick={() => editor.chain().focus().toggleOrderedList().run()}><ListOrdered /></Tool>
      <Separator orientation="vertical" className="mx-1 h-5!" />
      <VariablePicker variables={variables} onPick={(name) => editor.chain().focus().insertContent({ type: 'variable', attrs: { name } }).run()} />
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm"><Plus /> Insert <ChevronDown className="opacity-60" /></Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="w-64">
          <DropdownMenuItem onSelect={insertButton}><MousePointerClick /> Button</DropdownMenuItem>
          <DropdownMenuItem onSelect={insertTable}><TableProperties /> Details table</DropdownMenuItem>
          <DropdownMenuItem onSelect={insertCallout}><MessageSquareText /> Callout box</DropdownMenuItem>
          {(flags.length > 0 || lists.length > 0) && <DropdownMenuSeparator />}
          {flags.length > 0 && (
            <DropdownMenuItem onSelect={wrapShowIf}><Eye /> Show only if… <span className="ml-auto text-caption text-muted-foreground">wraps selection</span></DropdownMenuItem>
          )}
          {lists.length > 0 && <DropdownMenuItem onSelect={insertRepeat}><Repeat /> Repeat for each…</DropdownMenuItem>}
        </DropdownMenuContent>
      </DropdownMenu>
      {state.inTable && (
        <>
          <Separator orientation="vertical" className="mx-1 h-5!" />
          <Button variant="ghost" size="sm" onClick={() => addDetailsRow(editor)}><Rows3 /> Add row</Button>
          <Button variant="ghost" size="sm" onClick={() => removeDetailsRow(editor)}><Trash2 /> Remove row</Button>
        </>
      )}
    </div>
  )
}

function Tool({ on, label, onClick, children }: { on: boolean; label: string; onClick: () => void; children: React.ReactNode }) {
  return (
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      aria-label={label}
      aria-pressed={on}
      title={label}
      onClick={onClick}
      className={cn(on && 'bg-accent text-foreground')}
    >
      {children}
    </Button>
  )
}

function LinkTool({ editor, active }: { editor: Editor; active: boolean }) {
  const [open, setOpen] = useState(false)
  const [url, setUrl] = useState('')
  return (
    <Popover open={open} onOpenChange={(o) => { setOpen(o); if (o) setUrl(editor.getAttributes('link').href ?? '') }}>
      <PopoverTrigger asChild>
        <Button type="button" variant="ghost" size="icon-sm" aria-label="Link" title="Link" className={cn(active && 'bg-accent text-foreground')}>
          <Link2 />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80" align="start">
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            const chain = editor.chain().focus().extendMarkRange('link')
            if (url.trim()) chain.setLink({ href: url.trim() }).run()
            else chain.unsetLink().run()
            setOpen(false)
          }}
        >
          <Input autoFocus placeholder="https:// or {{ portal_url }}" value={url} onChange={(e) => setUrl(e.target.value)} />
          <Button size="sm" type="submit">{url.trim() ? 'Set' : 'Remove'}</Button>
        </form>
      </PopoverContent>
    </Popover>
  )
}

/** Insert → Variable: every value this email has, searchable, with what it holds. */
export function VariablePicker({ variables, onPick, label = 'Variable' }: {
  variables: TemplateVariable[]
  onPick: (name: string) => void
  label?: string
}) {
  const [open, setOpen] = useState(false)
  const groups = (['text', 'flag', 'list'] as const).map((k) => ({ kind: k, items: variables.filter((v) => v.kind === k) })).filter((g) => g.items.length)
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="ghost" size="sm"><Braces /> {label}</Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-0" align="start">
        <Command>
          <CommandInput placeholder="Find a variable…" />
          <CommandList className="max-h-80">
            <CommandEmpty>No variable by that name.</CommandEmpty>
            {groups.map((g) => (
              <CommandGroup key={g.kind} heading={KIND_LABEL[g.kind]}>
                {g.items.map((v) => (
                  <CommandItem key={v.name} value={`${v.label} ${v.name}`} onSelect={() => { onPick(v.name); setOpen(false) }} className="flex-col items-start gap-0">
                    <span className="text-sm">{v.label}</span>
                    <span className="text-caption text-muted-foreground">{v.description}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
