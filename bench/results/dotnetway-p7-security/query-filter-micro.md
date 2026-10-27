# HttpLog.filterQuery, dotnet fsi --optimize+ on the Mac (M-series), 2,000,000 calls each after 200,000 warm-up; the script follows.
?before=1234567        49.2 ns/call -> ValueNone   (messages_page's query)
?q=design+review       38.1 ns/call -> ValueNone   (a search-shaped query)
?v=1761111111          27.8 ns/call -> ValueNone   (an avatar's query)
?token=abc&q=x        421.9 ns/call -> ValueSome "?token=[FILTERED]&q=x"

```fsharp
#r "<path>/campfire.dll"
#r "<path>/Microsoft.AspNetCore.HttpLogging.dll"
#r "<path>/Microsoft.AspNetCore.Http.Abstractions.dll"
open System.Diagnostics
let queries = [ "?before=1234567"; "?q=design+review"; "?v=1761111111"; "?token=abc&q=x" ]
for q in queries do
    for _ in 1..200_000 do Campfire.App.Web.HttpLog.filterQuery q |> ignore
    let n = 2_000_000
    let sw = Stopwatch.StartNew()
    for _ in 1..n do Campfire.App.Web.HttpLog.filterQuery q |> ignore
    printfn "%-20s %6.1f ns/call -> %A" q (float sw.Elapsed.TotalMilliseconds * 1e6 / float n) (Campfire.App.Web.HttpLog.filterQuery q)
```
