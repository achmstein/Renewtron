import Editor, { DiffEditor, loader } from '@monaco-editor/react'
import * as monaco from 'monaco-editor'
import EditorWorker from 'monaco-editor/editor/editor.worker?worker'
import HtmlWorker from 'monaco-editor/language/html/html.worker?worker'
import type { TemplateVariable } from '@/api/emailTemplates'

/*
 * Monaco (VS Code's editor) for code view and for comparing versions. Bundled with the app (the
 * editor chunk is only loaded on the template pages), not fetched from a CDN.
 */

self.MonacoEnvironment = {
  getWorker: (_id: string, label: string) => (label === 'html' ? new HtmlWorker() : new EditorWorker()),
}
loader.config({ monaco })

/** The open template's variables, for completion after "{{". Set by the page. */
let completionVariables: TemplateVariable[] = []
export function setCompletionVariables(vars: TemplateVariable[]) { completionVariables = vars }

let registered = false
function registerLiquid() {
  if (registered) return
  registered = true
  monaco.languages.registerCompletionItemProvider('html', {
    triggerCharacters: ['{', ' '],
    provideCompletionItems: (model, position) => {
      const before = model.getValueInRange({ startLineNumber: position.lineNumber, startColumn: 1, endLineNumber: position.lineNumber, endColumn: position.column })
      const word = model.getWordUntilPosition(position)
      const range = { startLineNumber: position.lineNumber, endLineNumber: position.lineNumber, startColumn: word.startColumn, endColumn: word.endColumn }
      const inOutput = /\{\{[^}]*$/.test(before)
      const inTag = /\{%[^%]*$/.test(before)
      if (!inOutput && !inTag) {
        if (!before.endsWith('{')) return { suggestions: [] }
        return {
          suggestions: [
            ...completionVariables.map((v) => ({
              label: `{{ ${v.name} }}`, kind: monaco.languages.CompletionItemKind.Variable, detail: v.label, documentation: v.description,
              insertText: `{ ${v.name} }}`, range,
            })),
            { label: '{% if … %}', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '% if ${1:condition} %}\n$0\n{% endif %}', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, range, detail: 'Show only if' },
            { label: '{% for … %}', kind: monaco.languages.CompletionItemKind.Snippet, insertText: '% for ${1:item} in ${2:list} %}\n$0\n{% endfor %}', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, range, detail: 'Repeat for each' },
          ],
        }
      }
      return {
        suggestions: completionVariables
          .filter((v) => inOutput || v.kind !== 'text')
          .map((v) => ({ label: v.name, kind: monaco.languages.CompletionItemKind.Variable, detail: v.label, documentation: v.description, insertText: v.name, range })),
      }
    },
  })
}

const options: monaco.editor.IStandaloneEditorConstructionOptions = {
  minimap: { enabled: false },
  wordWrap: 'on',
  fontSize: 13,
  lineNumbersMinChars: 3,
  scrollBeyondLastLine: false,
  tabSize: 2,
  automaticLayout: true,
  renderWhitespace: 'none',
  quickSuggestions: { other: true, strings: true, comments: false },
}

export function CodeEditor({ value, onChange, height = '100%' }: { value: string; onChange: (v: string) => void; height?: string }) {
  return (
    <Editor
      height={height}
      language="html"
      value={value}
      onChange={(v) => onChange(v ?? '')}
      beforeMount={registerLiquid}
      options={options}
    />
  )
}

export function CodeDiff({ original, modified, height = '100%' }: { original: string; modified: string; height?: string }) {
  return (
    <DiffEditor
      height={height}
      language="html"
      original={original}
      modified={modified}
      options={{ ...options, readOnly: true, renderSideBySide: true, originalEditable: false, useInlineViewWhenSpaceIsLimited: true }}
    />
  )
}
