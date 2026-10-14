// The loose DOM layer (plans/dotnetway.md, section 4): the page's live body.outerHTML parsed as a tree and compared as the tree a
// reader of the DOM sees, not as the bytes a server sent. It is what "data attributes and Turbo/Stimulus hooks must survive" is
// checked by, while the byte-level differences the plan accepts (class A) vanish:
//
//   * attributes are a set: order never matters, a repeated attribute counts once, `disabled` and `disabled=""` are the same, and
//     `class` is a set of tokens (a data-*, id, aria-*, role, Turbo or Stimulus attribute that is lost or changed still differs)
//   * character references are decoded (`&amp;` and `&#38;` and `&` are one character); the output prints the decoded text
//   * `<br>` and `<br/>`, attribute quoting and comments are not part of the tree
//   * a whitespace-only text node is dropped, except inside pre, textarea, listing, plaintext and xmp (where every character
//     renders) and between two inline-level siblings (`<a>x</a> <a>y</a>`: that space renders, so losing it is a pixel change);
//     elements that draw no box (`input type=hidden`, script, style, template, ...) and the left-out forgery tokens are looked
//     through, and a run of blanks across them counts as one
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
// Elements that generate no box (display: none by default): whitespace beside them renders only if it has rendered inline
// content on both sides further out, so they are looked through when deciding whether a space shows. A hidden input between two
// spaces at the start of a form (Rails' `form_with` writes `<input type="hidden" name="authenticity_token">` and then its template's
// newline) draws nothing.
const BOXLESS = new Set(["script", "style", "template", "link", "meta", "base", "title", "noscript"])
const isBoxless = (node: any): boolean =>
  node.tagName !== undefined &&
  (BOXLESS.has(node.tagName) ||
    (node.tagName === "input" && (node.attrs.find((a: any) => a.name === "type")?.value ?? "").toLowerCase() === "hidden"))
const isBlankText = (node: any): boolean => node.nodeName === "#text" && !collapse(node.value)

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
    // Comments are not part of the tree a reader sees, and a left-out forgery token is not either: it must not decide whether the
    // whitespace beside it renders.
    const kids = children.filter((c: any) => c.nodeName !== "#comment" && !(c.tagName !== undefined && isForgeryToken(c.tagName, c.attrs)))
    // The nearest sibling that draws something, looking through box-less elements (and, going forward, through blank text).
    const prevDrawn = (i: number): any => { for (let j = i - 1; j >= 0; j--) if (!isBoxless(kids[j])) return kids[j]; return undefined }
    const nextDrawn = (i: number, skipBlank: boolean): any => {
      for (let j = i + 1; j < kids.length; j++) if (!isBoxless(kids[j]) && !(skipBlank && isBlankText(kids[j]))) return kids[j]
      return undefined
    }
    kids.forEach((child, i) => {
      if (child.nodeName !== "#text") return walk(child, depth + 1, out, options, childRaw)
      if (childRaw) {
        if (child.value) out.push(`${indent}  ${JSON.stringify(maskText(child.value, options))}`)
        return
      }
      const prev = prevDrawn(i)
      const text = collapse(child.value)
      if (!text) {
        // Whitespace only: a run of it (across box-less elements) renders as one space, only between two inline-level siblings.
        // The first blank of a run speaks for it.
        if (child.value === "" || (prev !== undefined && isBlankText(prev))) return
        const next = nextDrawn(i, true)
        if (prev !== undefined && isInlineNode(prev) && next !== undefined && isInlineNode(next)) out.push(`${indent}  " "`)
        return
      }
      const next = nextDrawn(i, false)
      // A space at the text's edge renders against an inline neighbour, unless a blank run between them already stands for it.
      const lead = /^[\t\n\f\r ]/.test(child.value) && prev !== undefined && isInlineNode(prev) && !isBlankText(prev) ? " " : ""
      const trail = /[\t\n\f\r ]$/.test(child.value) && next !== undefined && isInlineNode(next) && !isBlankText(next) ? " " : ""
      out.push(`${indent}  ${JSON.stringify(maskText(lead + text + trail, options))}`)
    })
  }
  if (!VOID.has(tag)) out.push(`${indent}</${tag}>`)
}
