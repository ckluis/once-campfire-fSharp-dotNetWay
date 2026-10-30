/// Hostile rich text bodies: a grammar fuzzer over the shapes that break sanitizers, plus fixed mXSS classics.
module Gen

open System
open System.Text

let urls =
    [| "javascript:alert(1)"; "JaVaScRiPt:alert(1)"; " javascript:alert(1)"; "\tjavascript:alert(1)"; "java\tscript:alert(1)"
       "java\nscript:alert(1)"; "java\rscript:alert(1)"; "\u0001javascript:alert(1)"; "\u000bjavascript:alert(1)"; "javascript\u0000:alert(1)"
       "&#106;avascript:alert(1)"; "&#x6A;avascript:alert(1)"; "&#0000106avascript:alert(1)"; "&#x6a&#x61vascript:alert(1)"
       "javascript&colon;alert(1)"; "javascript&#58;alert(1)"; "javascript&#x3a;alert(1)"; "javascript&#X3A;alert(1)"; "javascript&#0000058alert(1)"
       "jav&Tab;ascript:alert(1)"; "jav&NewLine;ascript:alert(1)"; "jav&#9;ascript:alert(1)"; "jav&#x0A;ascript:alert(1)"; "jav&#13;ascript:alert(1)"
       "javascript%3aalert(1)"; "vbscript:msgbox(1)"; "VBScript:x"; "livescript:x"; "&amp;#106;avascript:alert(1)"
       "data:text/html,<script>alert(1)</script>"; "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg=="
       "data:image/svg+xml,<svg onload=alert(1)>"; "data:image/png;base64,iVBORw0KGgo="; " data:text/html,x"; "DATA:TEXT/HTML,x"
       "data:text/html;charset=utf-8,x"; "data:,<script>alert(1)</script>"; "data:image/png;x=text/html,x"; "data:text/plain,x"
       "data:image/png&#44;text/html,x"; "data&colon;text/html,x"; "&#100;ata:text/html,x"; "\u0000data:text/html,x"
       "javas&#99;ript:alert(1)"; "javas&#x0000000063;ript:alert(1)"; "j​avascript:alert(1)"; "javascript­:alert(1)"
       "javascript://%0aalert(1)"; "javascript://example.com/%0aalert(1)"; "javascript:/*--></title></style></textarea></script></xmp><svg/onload='+/\"/+/onmouseover=1/+/[*/[]/+alert(1)//'>"
       "https://example.com/"; "http://example.com/a?b=1&c=2"; "//evil.example/x"; "/rooms/1"; "mailto:a@b.co"; "file:///etc/passwd"
       "ftp://example.com/x"; "https://example.com/\"><img src=x onerror=alert(1)>"; "https://example.com/<123>"; ""
       "x:alert(1)"; "javascript :alert(1)"; "javascript: alert(1)"; " javascript:alert(1)"; "　javascript:alert(1)"
       "jAvAsCrIpT\u0009:alert(1)"; "&Tab;javascript:alert(1)"; "&#x20;javascript:alert(1)"; "%20javascript:alert(1)"; "`javascript:alert(1)" |]

let tags =
    [| "a"; "abbr"; "b"; "blockquote"; "br"; "code"; "div"; "em"; "figure"; "figcaption"; "h1"; "hr"; "i"; "img"; "li"; "ol"; "p"; "pre"; "s"
       "span"; "strong"; "table"; "tbody"; "td"; "th"; "thead"; "tfoot"; "tr"; "caption"; "colgroup"; "col"; "u"; "ul"; "video"; "audio"; "source"
       "embed"; "object"; "param"; "iframe"; "frame"; "frameset"; "script"; "style"; "template"; "noscript"; "noembed"; "noframes"; "xmp"
       "plaintext"; "textarea"; "title"; "select"; "option"; "optgroup"; "datalist"; "keygen"; "input"; "button"; "form"; "isindex"; "image"
       "math"; "svg"; "mi"; "mo"; "mn"; "ms"; "mtext"; "mglyph"; "malignmark"; "annotation-xml"; "foreignObject"; "desc"; "path"; "use"
       "animate"; "set"; "font"; "center"; "marquee"; "details"; "summary"; "dialog"; "listing"; "base"; "meta"; "link"; "head"; "body"
       "html"; "action-text-attachment"; "lexxy-content"; "actiontext-opengraph-embed"; "x-y"; "mark"; "time"; "del"; "ins"; "sub"; "sup"
       "kbd"; "var"; "tt"; "cite"; "q"; "dl"; "dt"; "dd"; "nobr"; "rb"; "rt"; "ruby"; "slot"; "search"; "hgroup"; "menu"; "applet"; "bgsound" |]

let attrNames =
    [| "href"; "src"; "cite"; "poster"; "action"; "formaction"; "xlink:href"; "data"; "srcset"; "background"; "longdesc"; "xml:base"; "style"
       "id"; "name"; "class"; "data-controller"; "data-action"; "data-language"; "content"; "url"; "sgid"; "content-type"; "filename"; "caption"
       "presentation"; "width"; "height"; "target"; "rel"; "value"; "start"; "type"; "encoding"; "title"; "alt"; "lang"; "xml:lang"; "dir"
       "onerror"; "onclick"; "onload"; "ONMOUSEOVER"; "onfocus"; "autofocus"; "is"; "slot"; "popover"; "xmlns"; "xmlns:xlink"; "srcdoc"
       "\"a"; "a<b"; "/onerror"; "data-turbo-frame"; "data-trix-attachment"; "data-trix-attributes"; "datetime"; "abbr"; "controls"; "preload" |]

let texts =
    [| "hello"; "https://example.com/x."; "www.example.com"; "(https://example.com/(a))"; "me@example.com"; "&amp; &lt; &gt; &quot; &nbsp;"
       "&lt;script&gt;alert(1)&lt;/script&gt;"; "&lt;img src=x onerror=alert(1)&gt;"; "<"; ">"; "&"; "]]>"; "--!>"; "<!--"; "-->"; ""
       "<1>"; "&#57344;"; "&#xE000;1&gt;"; "\u0000"; "\ud800"; "‮"; " "; "javascript:alert(1)"; "http://example.com/&gt;<b>"
       "www.example.com/\"><script>x()</script>"; "me@example.com\"onclick=\"x"; "https://example.com/\"onmouseover=\"alert(1)"
       "http://x.test/ <img src=x onerror=alert(1)>"; "&#106;avascript:alert(1)"; "https://x.com/dhh/status/1?s=2"; "a@b.co&#x22;onclick=x"
       "http://example.com/%22%3E%3Cimg%20src%3Dx%3E"; "\n"; " "; "😀"; "<\u0000script>"; "</script>"; "</textarea>"; "</style>"; "</noscript>"
       "</title>"; "</xmp>"; "</iframe>"; "</noembed>"; "</select>" |]

let rawPieces =
    [| "<!-- c -->"; "<!--->"; "<!-->"; "<!--!-->"; "<!---->"; "<!-- --!> <img src=x onerror=alert(1)> -->"; "<![CDATA[<img src=x onerror=alert(1)>]]>"
       "<?php echo 1 ?>"; "</ x>"; "<a/b>"; "<img/src=x/onerror=alert(1)>"; "<img src=x onerror=alert(1)//"; "<a href='x' title=\""
       "<p title=x>y>"; "<img src=\"x\"onerror=\"alert(1)\">"; "<IMG SRC=\"jav&#x09;ascript:alert(1);\">"; "<a href=\"\" onclick=alert(1)>x</a>"
       "<svg/onload=alert(1)>"; "<body onload=alert(1)>"; "<html onload=alert(1)>"; "<base href=\"javascript:/\">"; "<meta http-equiv=refresh content=\"0;url=javascript:alert(1)\">"
       "<object data=\"javascript:alert(1)\">"; "<embed src=\"javascript:alert(1)\">"; "<iframe srcdoc=\"<script>alert(1)</script>\">"
       "<form action=javascript:alert(1)><input type=submit>"; "<button formaction=javascript:alert(1)>x</button>"; "<isindex action=javascript:alert(1) type=image>"
       "<image src=x onerror=alert(1)>"; "<keygen autofocus onfocus=alert(1)>"; "<details open ontoggle=alert(1)>"; "<video><source onerror=alert(1)>"
       "<a href=\"javascript&colon;alert(1)\">c</a>"; "<a href=\"&#14;javascript:alert(1)\">d</a>"; "<a href=\"\u0001javascript:alert(1)\">e</a>" |]

let mxss =
    [| "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">"
       "<math><mtext><table><mglyph><style><!--</style><img title=\"--&gt;&lt;/mglyph&gt;&lt;img&Tab;src=1&Tab;onerror=alert(1)&gt;\">"
       "<math><mi><mglyph><svg><mtext><textarea><path id=\"</textarea><img onerror=alert(1) src=1>\">"
       "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">"
       "<form><math><mtext></form><form><mglyph><style></math><img src onerror=alert(1)>"
       "<svg><style><img src=x onerror=alert(1)></style></svg>"
       "<template><style></template><img src=x onerror=alert(1)></style></template>"
       "<select><template><style><!--</style><a rel=\"--></template></select><img src=x onerror=alert(1)>\">"
       "<table><caption><svg><title></table><img src=x onerror=alert(1)>"
       "<xmp><p title=\"</xmp><img src=x onerror=alert(1)>\">"
       "<textarea><p title=\"</textarea><img src=x onerror=alert(1)>\">"
       "<iframe><p title=\"</iframe><img src=x onerror=alert(1)>\">"
       "<noembed><p title=\"</noembed><img src=x onerror=alert(1)>\">"
       "<noframes><p title=\"</noframes><img src=x onerror=alert(1)>\">"
       "<style><p title=\"</style><img src=x onerror=alert(1)>\">"
       "<title><p title=\"</title><img src=x onerror=alert(1)>\">"
       "<plaintext><p title=\"</plaintext><img src=x onerror=alert(1)>\">"
       "<a title=\"&lt;/noscript&gt;&lt;img src=x onerror=alert(1)&gt;\">x</a>"
       "<p title=\"<!--\">a</p><p title=\"--><img src=x onerror=alert(1)>\">b</p>"
       "<math><annotation-xml encoding=\"text/html\"><style><img src=x onerror=alert(1)></style></annotation-xml></math>"
       "<math><annotation-xml encoding=\"text/html\"><p title=\"</annotation-xml><img src=x onerror=alert(1)>\">"
       "<svg><foreignObject><p title=\"</foreignObject><img src=x onerror=alert(1)>\">x</p></foreignObject></svg>"
       "<svg><desc><style><img src=x onerror=alert(1)></style></desc></svg>"
       "<math><mtext><mglyph><svg><mtext><style><path id=\"</style><img onerror=alert(1) src>\">"
       "<svg><![CDATA[</svg><img src=x onerror=alert(1)>]]></svg>"
       "<math><![CDATA[x]]><mi>y</mi></math><img src=x onerror=alert(1)>"
       "<svg><a xlink:href=\"javascript:alert(1)\"><text x=\"20\" y=\"20\">XSS</text></a></svg>"
       "<math href=\"javascript:alert(1)\">CLICK</math>"
       "<form id=a><form><p>x</p></form>"
       "<table><template><table></table></template></table><p>after</p>"
       "<table><tr><template><td><b>x</b></td></template></tr></table>"
       "<a><template><a>x</a></template></a>"
       "<table><tbody><template><div>x</div></template></tbody></table>"
       "<p><table><template></p><img src=x onerror=alert(1)></template></table>"
       "<select><option><b>x<img src=x onerror=alert(1)></b></option></select>"
       "<select><hr><img src=x onerror=alert(1)></select>"
       "<template><form><summary></form>text</template>"
       "<div><template><div></template></div>x</div>"
       "<table><div><table><tr><td>x</td></tr></table></div></table>"
       "<b><table><tr><td></b>x</td></tr></table>"
       "<a href=\"https://a.test/\"><table><a href=\"https://b.test/\">x</a></table></a>"
       "<table><td><table></td><p>escape</p></table>"
       "<p><b><i><u><s>x</p>y</s></u></i></b>"
       "<frameset><frame src=javascript:alert(1)>"
       "<noscript><style></noscript><img src=x onerror=alert(1)>"
       "<template><noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript></template>"
       "<figure><noscript><img title=\"</noscript><img src=x onerror=alert(1)>\"></noscript></figure>" |]

let attachments =
    [| "<action-text-attachment content-type=\"text/html\" content=\"<img src=x onerror=alert(1)><svg onload=alert(1)><a href=javascript:alert(1)>x</a><style>x</style>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"image/png\" url=\"javascript://%0aalert(1)\" caption=\"<img src=x onerror=alert(1)>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"image/png\" url=\"data:text/html,<script>alert(1)</script>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"image/png\" url=\"//evil.example/x.png\" width=\"1&quot; onerror=&quot;alert(1)\"></action-text-attachment>"
       "<action-text-attachment content-type=\"image/png\" url=\"data:image/svg+xml,<svg onload=alert(1)>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"image/png\" url=\"cid:x\"></action-text-attachment>"
       "<action-text-attachment content-type=\"video/mp4\" url=\"javascript:alert(1)\" filename=\"<script>alert(1)</script>\" caption=\"</figcaption><img src=x onerror=alert(1)>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"video/mp4\" url=\"https://e.test/v.mp4&quot;><script>alert(1)</script>\" width=\"x\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"javascript:alert(1)\" url=\"javascript:alert(1)\" filename=\"<img src=x onerror=alert(1)>\" caption=\"</div><script>alert(1)</script>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"https://evil.example/&quot;onmouseover=&quot;alert(1)\" url=\"https://evil.example/i.png?x=&quot;><img src=x onerror=alert(1)>\" filename=\"t\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"https://evil.example/?q=<script>\" url=\"https://evil.example/i.png?q=</div><img src=x onerror=alert(1)>\" filename=\"t\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" content=\"<div class=og-embed__title><a href='javascript:alert(1)'>t</a></div><div class=og-embed__image><img src='javascript:alert(1)'></div><div class=og-embed__description><img src=x onerror=alert(1)></div>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" content=\"<div class=og-embed__title><a href='https://e.test/a?x=&quot;&gt;&lt;img src=x onerror=alert(1)&gt;'>&lt;img src=x onerror=alert(1)&gt;</a></div><div class=og-embed__description>&lt;script&gt;</div>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"https://e.test/\" filename=\"xé\" caption=\"\"></action-text-attachment>"
       "<action-text-attachment sgid=\"hostile\" content-type=\"application/vnd.campfire.mention\"></action-text-attachment>"
       "<action-text-attachment sgid=\"hostile\" content-type=\"application/vnd.campfire.mention\" caption=\"<img src=x onerror=alert(1)>\"></action-text-attachment>"
       "<action-text-attachment sgid=\"bad\" content-type=\"text/html\" content=\"<action-text-attachment content-type=&quot;text/html&quot; content=&quot;&amp;lt;img src=x onerror=alert(1)&amp;gt;&quot;></action-text-attachment>\"></action-text-attachment>"
       "<div><action-text-attachment presentation=\"gallery\" content-type=\"image/png\" url=\"javascript://%0aalert(1)\"></action-text-attachment><action-text-attachment presentation=\"gallery\" content-type=\"image/png\" url=\"https://e.test/a.png\" caption=\"<b>x</b>\"></action-text-attachment></div>"
       "<figure data-trix-attachment='{\"contentType\":\"text/html\",\"content\":\"<img src=x onerror=alert(1)><a href=javascript:alert(1)>x</a>\"}'></figure>"
       "<figure data-trix-attachment='{\"url\":\"javascript://%0aalert(1)\",\"contentType\":\"image/png\"}' data-trix-attributes='{\"caption\":\"<img src=x onerror=alert(1)>\"}'></figure>"
       "<figure data-trix-attachment='{\"sgid\":\"hostile\",\"contentType\":\"application/vnd.campfire.mention\"}'></figure>"
       "<figure data-trix-attachment='{\"href\":\"javascript:alert(1)\",\"filename\":\"x\",\"contentType\":\"application/vnd.actiontext.opengraph-embed\"}'></figure>"
       "<figure data-trix-attachment='{\"contentType\":\"text/html\",\"content\":\"<svg><style><img src=x onerror=alert(1)></style></svg>\"}'></figure>"
       "<action-text-attachment content-type=\"text/html\" content=\"<noscript><p title=&quot;</noscript><img src=x onerror=alert(1)>&quot;></noscript>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"text/html\" content=\"<table><template><table></table></template></table>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"text/html\" content=\"<embed src=https://evil.example/x.html><video poster=javascript:alert(1)><source src=javascript:alert(1)></video><audio src=x onerror=alert(1)></audio>\"></action-text-attachment>"
       "<action-text-attachment content-type=\"text/html\" content=\"<p style=&quot;color: red; background: url(javascript:alert(1))&quot;>s</p><p style=&quot;color:expression(alert(1))&quot;>t</p>\"></action-text-attachment>" |]

let pick (rng: Random) (a: 'a[]) = a[rng.Next a.Length]

let attribute (rng: Random) =
    let name = pick rng attrNames
    let value =
        match rng.Next 6 with
        | 0
        | 1 -> pick rng urls
        | 2 -> pick rng texts
        | 3 -> "<img src=x onerror=alert(1)>"
        | 4 -> "alert(1)"
        | _ -> "x"
    let escapedValue = value.Replace("\"", "&quot;")
    match rng.Next 5 with
    | 0 -> name + "='" + value.Replace('\'', '`') + "'"
    | 1 -> name + "=" + value.Replace(' ', '+').Replace(">", "")
    | 2 -> name
    | _ -> name + "=\"" + escapedValue + "\""

let rec body (rng: Random) (depth: int) : string =
    let sb = StringBuilder()
    for _ in 1 .. rng.Next(1, 5) do
        let r = rng.NextDouble()
        if depth > 4 || r < 0.30 then sb.Append(pick rng texts) |> ignore
        elif r < 0.38 then sb.Append(pick rng rawPieces) |> ignore
        elif r < 0.45 then sb.Append(pick rng mxss) |> ignore
        elif r < 0.52 then sb.Append(pick rng attachments) |> ignore
        else
            let tag = pick rng tags
            sb.Append('<').Append(tag) |> ignore
            for _ in 1 .. rng.Next(0, 4) do
                sb.Append(' ').Append(attribute rng) |> ignore
            sb.Append('>') |> ignore
            sb.Append(body rng (depth + 1)) |> ignore
            if rng.NextDouble() < 0.85 then sb.Append("</").Append(tag).Append('>') |> ignore
    sb.ToString()

let tokens =
    [| "<"; ">"; "\""; "'"; "&"; "&#"; "&#x"; ";"; "</"; "/>"; "<b>"; "</b>"; "<p>"; "</p>"; "<div>"; "<svg>"; "<math>"; "<table>"; "<td>"
       "<select>"; "<template>"; "</template>"; "<noscript>"; "<style>"; "<a href="; "<!--"; "-->"; "<![CDATA["; "]]>"; "\n"; "\t"; " "
       "\u0000"; "javascript:"; "http://"; "www."; "@"; "="; " "; "--"; "=\""; "action-text-attachment"; "content=\""; "" |]

let mutate (rng: Random) (input: string) : string =
    let mutable s = input
    for _ in 1 .. rng.Next(1, 7) do
        let at = if s.Length = 0 then 0 else rng.Next(s.Length + 1)
        match rng.Next 4 with
        | 0
        | 1 -> s <- s.Insert(at, pick rng tokens)
        | 2 -> if at < s.Length then s <- s.Remove(at, Math.Min(rng.Next(1, 12), s.Length - at))
        | _ -> s <- s.Insert(at, s.Substring(at, Math.Min(rng.Next(1, 40), s.Length - at)))
    s

/// A body: generated, a fixed piece, or a mutation of either; sometimes inside a Trix div or paragraphs.
let next (rng: Random) : string =
    let core =
        match rng.Next 10 with
        | 0 -> pick rng mxss
        | 1 -> pick rng attachments
        | 2 -> pick rng rawPieces
        | _ -> body rng 0
    let core = if rng.NextDouble() < 0.35 then mutate rng core else core
    match rng.Next 4 with
    | 0 -> "<div>" + core + "</div>"
    | 1 -> "<p>" + core + "</p>"
    | _ -> core

let private quoted (u: string) = u.Replace("\"", "&quot;")
let fixedCases : string[] =
    Array.concat [ mxss; attachments; rawPieces; [| for u in urls -> "<p><a href=\"" + quoted u + "\">x</a><img src=\"" + quoted u + "\"><blockquote cite=\"" + quoted u + "\">q</blockquote></p>" |] ]
