import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

/*
 * Skeletonizer, after Flutter's: while loading, the page renders its real components with sample
 * data, and this draws a shimmering "bone" over each thing on screen, shaped like it: one per line
 * of text, and one each for badges, buttons, icons, inputs and coloured dots. Cards, borders and
 * table lines stay as they are, so the loading page has the same shape as the loaded one.
 *
 * The bones sit in an overlay; nothing inside the page's own markup is changed, so the real
 * content simply shows when the data arrives.
 *
 *   <Skeletonizer loading={isPending}>
 *     <ClientCard client={data ?? SAMPLE_CLIENT} />
 *   </Skeletonizer>
 *
 * Inside, data-skeleton="keep" leaves an element as it is (a page title, a table header),
 * data-skeleton="bone" draws one bone over the whole element, and data-skeleton="ignore" skips it.
 */

type Bone = { x: number; y: number; w: number; h: number; r: string; text?: boolean }

// Drawn as a single block rather than looked inside.
const WHOLE = new Set(['svg', 'img', 'input', 'textarea', 'select', 'button', 'canvas', 'video', 'iframe'])
const WHOLE_SLOTS = new Set(['badge', 'button', 'checkbox', 'switch', 'select-trigger', 'toggle-group-item', 'avatar', 'input'])
const WHOLE_ROLES = new Set(['checkbox', 'switch', 'radio', 'combobox'])

// A button or link laid out like a card (blocks of text inside) is looked inside, not drawn whole.
// Kept in step with the :has() list in index.css.
const CARD_CONTENT = 'div, p, h1, h2, h3, h4, dl, ul, ol, table'

const hasFill = (el: Element) => {
  const bg = getComputedStyle(el).backgroundColor
  return !!bg && bg !== 'transparent' && !/rgba\(.*,\s*0\)$/.test(bg)
}

function isWhole(el: Element): boolean {
  const tag = el.tagName.toLowerCase()
  const slot = el.getAttribute('data-slot')
  if (tag === 'button' || slot === 'button') {
    // A solid button, or an icon on its own, is one bone. A card-like button, or a ghost or outline
    // one with words, is looked inside: its text and icon get their own bones and its outline stays.
    if (el.querySelector(CARD_CONTENT)) return false
    return hasFill(el) || !el.textContent?.trim()
  }
  if (WHOLE.has(tag)) return true
  if (slot && WHOLE_SLOTS.has(slot)) return true
  const role = el.getAttribute('role')
  return !!role && WHOLE_ROLES.has(role)
}

/** A leaf with a fill of its own and nothing in it: a status dot, a bar, a swatch. */
function isFilledLeaf(el: Element): boolean {
  if (el.childElementCount > 0 || el.textContent?.trim()) return false
  return hasFill(el)
}

function collect(root: HTMLElement): Bone[] {
  const origin = root.getBoundingClientRect()
  const bones: Bone[] = []
  type Clip = { l: number; t: number; r: number; b: number }
  // Bones are cut to the scroll area they sit in (a table scrolled sideways, a row of filters),
  // so nothing shows past an edge the content itself is hidden behind.
  const clipTo = (r: { left: number; top: number; width: number; height: number }, c: Clip | null) => {
    if (!c) return { x: r.left, y: r.top, w: r.width, h: r.height }
    const x = Math.max(r.left, c.l), y = Math.max(r.top, c.t)
    return { x, y, w: Math.min(r.left + r.width, c.r) - x, h: Math.min(r.top + r.height, c.b) - y }
  }
  const push = (r: DOMRect, radius: string, clip: Clip | null, text = false) => {
    const c = clipTo(r, clip)
    if (c.w < 1 || c.h < 1) return
    bones.push({ x: c.x - origin.left, y: c.y - origin.top, w: c.w, h: c.h, r: radius, text })
  }
  const range = document.createRange()

  const walk = (node: Node, clip: Clip | null) => {
    for (let child = node.firstChild; child; child = child.nextSibling) {
      if (child.nodeType === Node.TEXT_NODE) {
        if (!child.nodeValue?.trim()) continue
        range.selectNodeContents(child)
        // One bone per line the text wraps onto, trimmed to the letters' height like a text line.
        for (const r of Array.from(range.getClientRects())) {
          if (r.width < 1) continue
          const h = Math.max(6, r.height * 0.68)
          const c = clipTo({ left: r.left, top: r.top + (r.height - h) / 2, width: r.width, height: h }, clip)
          if (c.w < 1 || c.h < 1) continue
          const x = c.x - origin.left, y = c.y - origin.top
          // Text React splits into pieces ("+", "1", " more") joins into one bone when they touch.
          const last = bones[bones.length - 1]
          if (last && last.text && Math.abs(last.y - y) < 2 && x - (last.x + last.w) < 8 && x >= last.x) {
            last.w = x + c.w - last.x
            continue
          }
          bones.push({ x, y, w: c.w, h: c.h, r: '4px', text: true })
        }
        continue
      }
      if (child.nodeType !== Node.ELEMENT_NODE) continue
      const el = child as HTMLElement
      const mode = el.getAttribute('data-skeleton')
      if (mode === 'ignore' || mode === 'keep') continue
      const style = getComputedStyle(el)
      const radius = style.borderRadius && style.borderRadius !== '0px' ? style.borderRadius : '4px'
      const tag = el.tagName.toLowerCase()
      const filled = tag === 'button' || el.getAttribute('data-slot') === 'button' || isFilledLeaf(el)
      if (mode === 'bone' || isWhole(el) || isFilledLeaf(el)) {
        const r = el.getBoundingClientRect()
        // CSS can't hide a solid button or a dot (it can't see the fill), so their bone grows a
        // pixel to cover the edge; everything else is hidden under its bone by index.css.
        push(filled ? new DOMRect(r.left - 1, r.top - 1, r.width + 2, r.height + 2) : r, radius, clip)
        continue
      }
      let inner = clip
      if (style.overflowX !== 'visible' || style.overflowY !== 'visible') {
        const r = el.getBoundingClientRect()
        const own = { l: r.left, t: r.top, r: r.right, b: r.bottom }
        inner = clip ? { l: Math.max(own.l, clip.l), t: Math.max(own.t, clip.t), r: Math.min(own.r, clip.r), b: Math.min(own.b, clip.b) } : own
      }
      walk(el, inner)
    }
  }
  walk(root, null)
  return bones
}

export function Skeletonizer({ loading, children, className }: {
  loading: boolean
  children: ReactNode
  className?: string
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [bones, setBones] = useState<Bone[]>([])

  useLayoutEffect(() => {
    const root = ref.current
    if (!loading || !root) { setBones([]); return }
    let frame = 0
    const measure = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => setBones(collect(root)))
    }
    setBones(collect(root))
    // Re-measure when the layout moves: a resize, a web font arriving, content changing.
    const ro = new ResizeObserver(measure)
    ro.observe(root)
    const mo = new MutationObserver(measure)
    mo.observe(root, { subtree: true, childList: true, characterData: true })
    void document.fonts?.ready.then(measure)
    return () => { cancelAnimationFrame(frame); ro.disconnect(); mo.disconnect() }
  }, [loading])

  return (
    <div className={cn('relative', className)} aria-busy={loading || undefined}>
      {/* Measured and watched on its own, so drawing the bones doesn't set off another measure. */}
      <div ref={ref} className={cn(loading && 'skeletonize')} inert={loading || undefined}>
        {children}
      </div>
      {loading ? (
        <div aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden">
          {bones.map((b, i) => (
            <div key={i} className="shimmer absolute" style={{ left: b.x, top: b.y, width: b.w, height: b.h, borderRadius: b.r }} />
          ))}
        </div>
      ) : null}
      {loading ? <span role="status" className="sr-only">Loading</span> : null}
    </div>
  )
}

/** n sample rows for a table or list while it loads: make(i) builds row i. */
export function sampleRows<T>(n: number, make: (i: number) => T): T[] {
  return Array.from({ length: n }, (_, i) => make(i))
}
