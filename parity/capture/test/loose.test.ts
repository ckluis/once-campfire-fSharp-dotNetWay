// Self-test of the loose DOM layer (capture/loose.ts; plans/dotnetway.md, section 4). Run: parity/bin/test-loose-dom
import assert from "node:assert/strict"
import { test } from "node:test"
import { diffText } from "../diff.ts"
import { looseDom } from "../loose.ts"

const seedTime = Date.parse("2026-03-02T16:00:00Z")
const loose = (body: string) => looseDom(body, { seedTime })
const equal = (a: string, b: string) => diffText(loose(a), loose(b)).equal

test("a lost data-* attribute is flagged", () => {
  const rails = `<body><div id="m" class="message" data-controller="messages" data-message-id="7" data-action="click->messages#open">hi</div></body>`
  const ours = `<body><div id="m" class="message" data-controller="messages" data-action="click->messages#open">hi</div></body>`
  const d = diffText(loose(rails), loose(ours))
  assert.equal(d.equal, false)
  assert.equal(d.removed, 1)
  assert.match(loose(rails), /data-message-id="7"/)
  assert.doesNotMatch(loose(ours), /data-message-id/)
})

test("a changed or lost id, aria, Turbo and Stimulus hook is flagged", () => {
  const base = `<body><turbo-frame id="f" target="_top"><button data-action="a#b" aria-label="Send" role="button">x</button></turbo-frame></body>`
  for (const changed of [
    base.replace(' id="f"', ""), base.replace('target="_top"', 'target="_self"'), base.replace(' data-action="a#b"', ' data-action="a#c"'),
    base.replace(' aria-label="Send"', ""), base.replace(' role="button"', ""),
  ]) assert.equal(equal(base, changed), false, changed)
})

test("attribute order is ignored", () => {
  const a = `<body><a href="/x" id="a" data-turbo="false" class="btn">go</a></body>`
  const b = `<body><a class="btn" data-turbo="false" id="a" href="/x">go</a></body>`
  assert.notEqual(a, b)
  assert.equal(equal(a, b), true)
  assert.equal(equal(`<body><div class="b a  c"></div></body>`, `<body><div class="c a b"></div></body>`), true)
})

test("character references, quoting, void-element syntax and comments are byte-level only", () => {
  assert.equal(equal(`<body><p title="a &amp; b">Tom &amp; Jerry &#8212; &quot;x&quot;</p><br></body>`, `<body><p title='a & b'>Tom & Jerry — "x"</p><br/><!-- c --></body>`), true)
  assert.equal(equal(`<body><input disabled name=q></body>`, `<body><input name="q" disabled=""></body>`), true)
})

test("whitespace between blocks does not matter, whitespace that renders does", () => {
  assert.equal(equal(`<body><div><p>a</p>\n  <p>b</p></div></body>`, `<body><div><p>a</p><p>b</p></div></body>`), true)
  assert.equal(equal(`<body><p>a   b\n c</p></body>`, `<body><p>a b c</p></body>`), true)
  // inline formatting context: the space between the two links is drawn
  assert.equal(equal(`<body><p><a>x</a> <a>y</a></p></body>`, `<body><p><a>x</a><a>y</a></p></body>`), false)
  assert.equal(equal(`<body><p>Hi <b>there</b></p></body>`, `<body><p>Hi<b>there</b></p></body>`), false)
  // pre and textarea keep every character
  assert.equal(equal(`<body><pre>a  b</pre></body>`, `<body><pre>a b</pre></body>`), false)
  assert.equal(equal(`<body><textarea>\n x </textarea></body>`, `<body><textarea>\nx </textarea></body>`), false)
})

test("whitespace beside a left-out forgery token or a hidden input does not matter, unless it sits between drawn inline content", () => {
  // Rails' form_with: the token, the template's newline, a hidden field, a newline, the button. The port writes no token.
  const rails = `<body><form action="/b" method="post"><input type="hidden" name="authenticity_token" value="t" autocomplete="off">\n  <input type="hidden" name="boost[content]" value="x">\n  <button type="submit">x</button>\n</form></body>`
  const port = `<body><form action="/b" method="post"><input type="hidden" name="boost[content]" value="x">\n  <button type="submit">x</button>\n</form></body>`
  const none = `<body><form action="/b" method="post"><input type="hidden" name="boost[content]" value="x"><button type="submit">x</button></form></body>`
  assert.equal(equal(rails, port), true)
  assert.equal(equal(rails, none), true)
  // a hidden input between two links: the space still renders, once
  assert.equal(equal(`<body><p><a>x</a> <input type="hidden" name="q"> <a>y</a></p></body>`, `<body><p><a>x</a> <input type="hidden" name="q"><a>y</a></p></body>`), true)
  assert.equal(equal(`<body><p><a>x</a> <input type="hidden" name="q"> <a>y</a></p></body>`, `<body><p><a>x</a><input type="hidden" name="q"><a>y</a></p></body>`), false)
  // a visible input is drawn: the space beside it still counts
  assert.equal(equal(`<body><p><input name="q"> <a>y</a></p></body>`, `<body><p><input name="q"><a>y</a></p></body>`), false)
  // text against a hidden input then a link: the edge space renders
  assert.equal(equal(`<body><p>Hi <input type="hidden" name="q"><b>there</b></p></body>`, `<body><p>Hi<input type="hidden" name="q"><b>there</b></p></body>`), false)
})

test("text, element order and nesting differences are flagged", () => {
  assert.equal(equal(`<body><p>Hello</p></body>`, `<body><p>Hullo</p></body>`), false)
  assert.equal(equal(`<body><p>a</p><p>b</p></body>`, `<body><p>b</p><p>a</p></body>`), false)
  assert.equal(equal(`<body><div><p>a</p></div></body>`, `<body><div></div><p>a</p></body>`), false)
})

test("volatile values are masked as in the live layer, forgery tokens left out", () => {
  assert.equal(equal(`<body><meta name="csrf-token" content="abc"><time datetime="2026-03-02T16:00:05Z">x</time></body>`, `<body><time datetime="2026-03-02T16:00:05Z">x</time></body>`), true)
  assert.equal(equal(`<body><time datetime="2026-03-02T16:00:05Z">x</time></body>`, `<body><time datetime="2026-03-02T16:00:09Z">x</time></body>`), false)
})
