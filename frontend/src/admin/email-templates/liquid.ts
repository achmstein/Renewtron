/*
 * A template is stored as HTML with Liquid in it. The visual editor can't hold Liquid tags, so on the
 * way in they become editor elements, and on the way out they turn back:
 *
 *   {% if flag %} …blocks… {% endif %}        ⇄  <div data-if="flag">…</div>          ("Show only if")
 *   {% for x in list %} …blocks… {% endfor %} ⇄  <div data-for="x in list">…</div>    ("Repeat for each")
 *   {{ name }}                                 ⇄  <span data-var="name"></span>        (a variable chip)
 *
 * Anything else (else, unless, filters like {{ x | upcase }}, a tag inside a sentence) can't be shown
 * visually without losing it, so toEditor says why and the page keeps that template in code view.
 */

const CONTROL = /\{%-?\s*(\w+)\s*([\s\S]*?)\s*-?%\}/g
const VARIABLE = /\{\{-?\s*([A-Za-z_][\w]*(?:\.[A-Za-z_]\w*|\[\d+\])*)\s*-?\}\}/g
/** Elements whose children are blocks (so an if/for can sit inside them). */
const CONTAINERS = new Set(['DIV', 'BLOCKQUOTE'])

export type ToEditor = { html: string } | { advanced: string }

export function toEditor(source: string): ToEditor {
  const other = [...source.matchAll(CONTROL)].find((m) => !['if', 'endif', 'for', 'endfor'].includes(m[1]))
  if (other) return { advanced: `It uses {% ${other[1]} %}, which only code view can edit.` }
  if (/\{%[^%]*</.test(source)) return { advanced: 'A Liquid tag here contains "<", which only code view can edit.' }

  const doc = new DOMParser().parseFromString(`<!doctype html><body>${source}</body>`, 'text/html')
  for (const el of Array.from(doc.body.querySelectorAll('*')))
    for (const attr of Array.from(el.attributes))
      if (attr.value.includes('{%')) return { advanced: 'A Liquid tag inside an element\'s attributes needs code view.' }

  const error = restructure(doc, doc.body)
  if (error) return { advanced: error }

  // Variables in text become chips; one with a filter (or anything else in {{ }}) can't.
  for (const text of textNodes(doc.body)) {
    const value = text.data
    if (!value.includes('{{')) continue
    const frag = doc.createDocumentFragment()
    let last = 0
    for (const m of value.matchAll(VARIABLE)) {
      frag.append(value.slice(last, m.index))
      const chip = doc.createElement('span')
      chip.setAttribute('data-var', m[1])
      frag.append(chip)
      last = m.index! + m[0].length
    }
    const rest = value.slice(last)
    if (rest.includes('{{')) return { advanced: `"${rest.slice(rest.indexOf('{{')).split('}}')[0]}}}" uses a filter or expression, which only code view can edit.` }
    frag.append(rest)
    text.replaceWith(frag)
  }
  return { html: doc.body.innerHTML }
}

/** Moves the blocks between {% if %}/{% endif %} (and for/endfor) into a wrapper element. Returns a problem, or null. */
function restructure(doc: Document, parent: Element): string | null {
  const nodes = Array.from(parent.childNodes)
  nodes.forEach((n) => n.remove())
  const stack: Array<{ el: Element; tag: 'root' | 'if' | 'for' }> = [{ el: parent, tag: 'root' }]
  const top = () => stack[stack.length - 1].el

  for (const node of nodes) {
    if (node.nodeType === Node.TEXT_NODE) {
      const value = (node as Text).data
      let last = 0
      for (const m of value.matchAll(CONTROL)) {
        appendLooseText(doc, top(), value.slice(last, m.index))
        last = m.index! + m[0].length
        const [, tag, expr] = m
        if (tag === 'if' || tag === 'for') {
          const box = doc.createElement('div')
          box.setAttribute(tag === 'if' ? 'data-if' : 'data-for', expr.trim())
          top().append(box)
          stack.push({ el: box, tag })
        } else {
          const want = tag === 'endif' ? 'if' : 'for'
          if (stack[stack.length - 1].tag !== want) return `{% ${tag} %} doesn't match an open {% ${want} %}.`
          stack.pop()
        }
      }
      appendLooseText(doc, top(), value.slice(last))
      continue
    }
    if (node.nodeType !== Node.ELEMENT_NODE) continue
    const el = node as Element
    const isContainer = CONTAINERS.has(el.tagName) && !el.classList.contains('button-row')
    if (isContainer) {
      const error = restructure(doc, el)
      if (error) return error
    } else if (el.textContent?.includes('{%')) {
      return 'A Liquid tag inside a paragraph, list or table needs code view.'
    }
    top().append(el)
  }
  if (stack.length > 1) return `{% ${stack[stack.length - 1].tag} %} is never closed.`
  return null
}

/** Text between blocks: dropped if it's only spacing, otherwise kept as a paragraph. */
function appendLooseText(doc: Document, parent: Element, text: string) {
  if (!text.trim()) return
  const p = doc.createElement('p')
  p.textContent = text.trim()
  parent.append(p)
}

function textNodes(root: Node): Text[] {
  const out: Text[] = []
  const walker = root.ownerDocument!.createTreeWalker(root, NodeFilter.SHOW_TEXT)
  for (let n = walker.nextNode(); n; n = walker.nextNode()) out.push(n as Text)
  return out
}

/** The editor's HTML back to the stored form: one block per line, Liquid tags on their own lines. */
export function fromEditor(html: string): string {
  const doc = new DOMParser().parseFromString(`<!doctype html><body>${html}</body>`, 'text/html')
  for (const chip of Array.from(doc.querySelectorAll('span[data-var]')))
    chip.replaceWith(doc.createTextNode(`{{ ${chip.getAttribute('data-var')} }}`))

  const lines: string[] = []
  const emit = (parent: Element, indent: string) => {
    for (const node of Array.from(parent.childNodes)) {
      if (node.nodeType === Node.TEXT_NODE) {
        const t = (node as Text).data.trim()
        if (t) lines.push(indent + t)
        continue
      }
      if (node.nodeType !== Node.ELEMENT_NODE) continue
      const el = node as Element
      const cond = el.getAttribute('data-if')
      const loop = el.getAttribute('data-for')
      if (cond !== null || loop !== null) {
        lines.push(cond !== null ? `{% if ${cond} %}` : `{% for ${loop} %}`)
        emit(el, '')
        lines.push(cond !== null ? '{% endif %}' : '{% endfor %}')
      } else if (el.querySelector('[data-if],[data-for]')) {
        // A callout with a "show only if" inside: write its tags, then its children inside it.
        const inner: string[] = []
        const saved = lines.splice(0)
        emit(el, '')
        inner.push(...lines.splice(0))
        lines.push(...saved)
        const shell = el.cloneNode(false) as Element
        shell.innerHTML = '\n' + inner.join('\n') + '\n'
        lines.push(shell.outerHTML)
      } else {
        lines.push(format(el))
      }
    }
  }
  emit(doc.body, '')
  // The editor keeps an empty paragraph at the end so there's somewhere to type; the email doesn't need it.
  while (lines.length && lines[lines.length - 1] === '<p></p>') lines.pop()
  return lines.join('\n')
}

/** A table's rows and a list's items each on their own line, so History compares them line by line. */
function format(el: Element): string {
  const open = (e: Element) => e.outerHTML.slice(0, e.outerHTML.indexOf('>') + 1)
  if (el.tagName === 'TABLE') {
    const rows = Array.from(el.querySelectorAll('tr')).map((r) => r.outerHTML)
    return [`${open(el)}<tbody>`, ...rows, '</tbody></table>'].join('\n')
  }
  if (el.tagName === 'UL' || el.tagName === 'OL') {
    const items = Array.from(el.children).map((li) => li.outerHTML)
    return [open(el), ...items, `</${el.tagName.toLowerCase()}>`].join('\n')
  }
  return el.outerHTML
}

/** The variables a template uses, in order, for the "used here" list. */
export function usedVariables(source: string): string[] {
  return [...new Set([...source.matchAll(VARIABLE)].map((m) => m[1].split(/[.[]/)[0]))]
}
