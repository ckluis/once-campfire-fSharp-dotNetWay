// plans/design-kit-auth.md section 4 (the migration rule): a legacy action as a Web action.
//
// `wrap` is the existing `Adapter.dispatch` path: it builds a `Ctx` from the `HttpContext`, runs the old action and
// writes its `Response`. The route table holds both kinds of rows until unit 2.13 deletes this file.
namespace Campfire.App.Web

open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Campfire.Kit

module Legacy =
    let wrap (old: ActionFn) : Action =
        fun http ->
            let kit = http.RequestServices.GetRequiredService<Kit>()
            Task.FromResult(Ok(Adapter.dispatch kit old))
