// The loose DOM layer (plans/dotnetway.md, section 4): the page's live body.outerHTML parsed as a tree and compared as the tree a
// reader of the DOM sees, not as the bytes a server sent. It is what "data attributes and Turbo/Stimulus hooks must survive" is
// checked by, while the byte-level differences the plan accepts (class A) vanish:
//
//   * attributes are a set: order never matters, a repeated attribute counts once, `disabled` and `disabled=""` are the same, and
//     `class` is a set of tokens (a data-*, id, aria-*, role, Turbo or Stimulus attribute that is lost or changed still differs)
//   * character references are decoded (`&amp;` and `&#38;` and `&` are one character); the output prints the decoded text
//   * `<br>` and `<br/>`, attribute quoting and comments are not part of the tree
//   * a whitespace-only text node is dropped, except inside pre, textarea, listing, plaintext and xmp (where every character
//     renders) and between two inline-level siblings (`<a>x</a> <a>y</a>`: that space renders, so losing it is a pixel change)
//   * other text keeps its characters, with runs of whitespace collapsed to one space, and loses the space at its edges unless
//     it touches an inline sibling
//
// Everything volatile is masked exactly as the live layer masks it (tokens, nonces, timestamps, asset digests), and the forgery
// tokens Rails renders are left out, as there (class B in the plan: the antiforgery scheme differs by design).
//
// The output is one node per line with its attributes sorted, so a line diff reads like a DOM diff.
import { parse } from "parse5"
import { collapse, isForgeryToken, maskAttr, maskText, normalizeJson, relativeCopyLink } from "./normalize.ts"
import type { NormalizeOptions } from "./normalize.ts"

const RAW_TEXT = new Set(["pre", "textarea", "listing", "plaintext", "xmp"])
const VOID = new Set(["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"])

// Elements that flow inside a line (CSS display: inline or inline-block by default): whitespace next to them renders.
const INLINE = new Set([
  "a", "abbr", "acronym", "b", "bdi", "bdo", "big", "br", "button", "canvas", "cite", "code", "data", "del", "dfn", "em", "font", "i",
  "iframe", "img", "input", "ins", "kbd", "label", "mark", "meter", "object", "output", "picture", "progress", "q", "ruby", "s", "samp",
  "select", "slot", "small", "span", "strike", "strong", "sub", "sup", "svg", "textarea", "time", "tt", "u", "var", "video", "audio", "wbr",
])
// Custom elements (turbo-frame, action-text-attachment, ...) have no default display; the browser treats them as inline.
const isInlineNode = (node: any): boolean =>
  node.nodeName === "#text" || (node.tagName !== undefined && (INLINE.has(node.tagName) || node.tagName.includes("-")))

export function looseDom(bodyOuterHtml: string, options: NormalizeOptions = {}): string {
  const document: any = parse(`<!DOCTYPE html><html><head></head>${bodyOuterHtml}</html>`)
  const html = document.childNodes.find((n: any) => n.tagName === "html")
  const body = html?.childNodes.find((n: any) => n.tagName === "body")
  const lines: string[] = []
  if (body) walk(body, 0, lines, options, false)
  return lines.join("\n") + "\n"
}

function walk(node: any, depth: number, out: string[], options: NormalizeOptions, raw: boolean) {
  const indent = "  ".repeat(depth)
  const tag = node.tagName as string
  if (isForgeryToken(tag, node.attrs)) return
  const seen = new Set<string>()
  const attrs: [string, string][] = []
  for (const a of node.attrs.map((a: any) => relativeCopyLink(node.attrs, a))) {
    if (seen.has(a.name)) continue
    seen.add(a.name)
    const value = maskAttr(tag, a.name, a.value, node.attrs, options)
    attrs.push([a.name, a.name === "class" ? [...new Set(value.split(/[\t\n\f\r ]+/).filter(Boolean))].sort().join(" ") : value])
  }
  attrs.sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))
  out.push(`${indent}<${tag}${attrs.map(([n, v]) => (v === "" ? ` ${n}` : ` ${n}=${JSON.stringify(v)}`)).join("")}>`)
  const children: any[] = tag === "template" ? node.content.childNodes : node.childNodes
  if (tag === "script" || tag === "style") {
    const text = children.map((c: any) => c.value ?? "").join("")
    const body = tag === "script" && /importmap|json/.test(node.attrs.find((a: any) => a.name === "type")?.value ?? "") ? normalizeJson(text) : collapse(text)
    if (body) out.push(`${indent}  ${maskText(body, options)}`)
  } else {
    const childRaw = raw || RAW_TEXT.has(tag)
    // Comments are not part of the tree a reader sees.
    const kids = children.filter((c: any) => c.nodeName !== "#comment")
    kids.forEach((child, i) => {
      if (child.nodeName !== "#text") return walk(child, depth + 1, out, options, childRaw)
      if (childRaw) {
        if (child.value) out.push(`${indent}  ${JSON.stringify(maskText(child.value, options))}`)
        return
      }
      const before = i > 0 && isInlineNode(kids[i - 1])
      const after = i < kids.length - 1 && isInlineNode(kids[i + 1])
      const text = collapse(child.value)
      if (!text) {
        // Whitespace only: it renders as one space only between two inline-level siblings.
        if (child.value !== "" && before && after) out.push(`${indent}  " "`)
        return
      }
      const lead = /^[\t\n\f\r ]/.test(child.value) && before ? " " : ""
      const trail = /[\t\n\f\r ]$/.test(child.value) && after ? " " : ""
      out.push(`${indent}  ${JSON.stringify(maskText(lead + text + trail, options))}`)
    })
  }
  if (!VOID.has(tag)) out.push(`${indent}</${tag}>`)
}
