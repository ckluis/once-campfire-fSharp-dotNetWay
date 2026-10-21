// Writes what loose.ts makes of the view layer's stored pages, for tests/Shared/LooseDom.fs (the F# port over AngleSharp) to be
// pinned to: every rust/crates/views/tests/golden/a/*.html (Rails' own renders) and 30 of the frozen oracle's answers
// (tests/Campfire.Views.Tests/oracle.jsonl). Run by parity/bin/loose-golden; the outputs are committed, so a change to loose.ts or
// normalize.ts shows as a diff there and fails the W2 test until the port follows.
//
//   node capture/loose-golden.ts OUT_DIR GOLDEN_A_DIR ORACLE_JSONL
//
// A "page" (starts with a doctype or <html>) is parsed whole and its body's outerHTML goes to looseDom, as the live layer does with
// the browser's body.outerHTML; anything else is a fragment and goes in as <body>FRAGMENT</body>.
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { parse, serializeOuter } from "parse5"
import { looseDom } from "./loose.ts"

const [outDir, goldenDir, oraclePath] = process.argv.slice(2)
const options = { seedTime: Date.parse("2026-03-02T16:00:00Z") }

export function loose(markup: string): string {
  if (/^\s*(<!doctype|<html[\s>])/i.test(markup)) {
    const document: any = parse(markup)
    const html = document.childNodes.find((n: any) => n.tagName === "html")
    const body = html.childNodes.find((n: any) => n.tagName === "body")
    return looseDom(serializeOuter(body), options)
  }
  return looseDom(`<body>${markup}</body>`, options)
}

// outDir is a bind mount: clear what is inside, not the directory
for (const sub of ["a", "oracle"]) {
  rmSync(join(outDir, sub), { recursive: true, force: true })
  mkdirSync(join(outDir, sub), { recursive: true })
}

let golden = 0
for (const file of readdirSync(goldenDir).sort()) {
  if (!file.endsWith(".html")) continue
  writeFileSync(join(outDir, "a", file.replace(/\.html$/, ".txt")), loose(readFileSync(join(goldenDir, file), "utf8")))
  golden++
}

// 30 oracle answers: the first markup answer of each operation (smaller than 40,000 characters), in file order.
const picked: { line: number; op: string }[] = []
const seen = new Set<string>()
const lines = readFileSync(oraclePath, "utf8").split("\n")
lines.forEach((text, index) => {
  if (picked.length >= 30 || !text) return
  const row = JSON.parse(text)
  if (row.shared) return
  const op = row.case.op + (row.case.args?.name === "to_lowercase" ? ":to_lowercase" : "")
  const out: string = row.rust.out
  if (seen.has(op) || out.length >= 40_000 || !out.trimStart().startsWith("<")) return
  seen.add(op)
  picked.push({ line: index + 1, op })
  writeFileSync(join(outDir, "oracle", `${String(picked.length).padStart(2, "0")}.txt`), loose(out))
})
writeFileSync(join(outDir, "oracle", "index.json"), JSON.stringify(picked) + "\n")
console.log(`loose-golden: ${golden} golden/a pages, ${picked.length} oracle answers`)
