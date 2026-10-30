using Microsoft.AspNetCore.OutputCaching;
using System.Net;
var b = WebApplication.CreateBuilder(args);
b.Logging.ClearProviders();
b.Services.AddOutputCache(o => { o.AddBasePolicy(p => p.AddPolicy<Gate>()); });
b.WebHost.UseUrls("http://127.0.0.1:5077");
var app = b.Build();
app.UseOutputCache();
int renders = 0;
app.MapGet("/page", (HttpContext h) => {
  Interlocked.Increment(ref renders);
  if (h.Request.Query["cookie"] == "1") h.Response.Cookies.Append("last_room", "7");
  if (h.Request.Query["session"] == "1") h.Response.Cookies.Append("session_token", "reissued");
  return Results.Text($"rendered {renders}", "text/html");
});
app.MapGet("/renders", () => renders.ToString());
await app.StartAsync();
var c = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri("http://127.0.0.1:5077") };
async Task Show(string label, string url, string auth = "ok") {
  var req = new HttpRequestMessage(HttpMethod.Get, url); req.Headers.Add("X-Auth", auth);
  var r = await c.SendAsync(req);
  var sc = r.Headers.TryGetValues("Set-Cookie", out var v) ? string.Join(" | ", v) : "-";
  Console.WriteLine($"{label,-40} {(int)r.StatusCode} body='{await r.Content.ReadAsStringAsync()}' set-cookie={sc} age={r.Headers.Age}");
}
await Show("1 miss plain", "/page?a=1");
await Show("2 hit plain", "/page?a=1");
await Show("3 hit, auth revoked (CacheRequest)", "/page?a=1", "revoked");
await Show("4 hit, auth stale (ServeFromCache)", "/page?a=1", "stale");
await Show("5 miss with last_room cookie", "/page?a=2&cookie=1");
await Show("6 again (stored with Set-Cookie?)", "/page?a=2&cookie=1");
await Show("7 miss, policy strips? session cookie", "/page?a=3&session=1");
await Show("8 again", "/page?a=3&session=1");
await Show("9 hit plus per-hit cookie from policy", "/page?a=1", "addcookie");
await Show("10 miss, cookie via OnStarting in CacheRequest", "/page?a=10", "onstarting");
await Show("11 again (stored? cookie replayed?)", "/page?a=10", "onstarting");
await Show("12 again without the OnStarting", "/page?a=10", "ok");
await Show("13 miss, cookie in ServeResponse", "/page?a=13", "serveresp");
await Show("14 again", "/page?a=13", "serveresp");
await Show("15 again plain", "/page?a=13", "ok");
await app.StopAsync();

class Gate : IOutputCachePolicy {
  public ValueTask CacheRequestAsync(OutputCacheContext ctx, CancellationToken ct) {
    var h = ctx.HttpContext;
    var auth = h.Request.Headers["X-Auth"].ToString();
    if (auth == "revoked") { ctx.EnableOutputCaching = false; ctx.AllowCacheLookup = false; ctx.AllowCacheStorage = false; return default; }
    if (auth == "addcookie") h.Response.Headers.Append("Set-Cookie", "last_room=9; path=/");
    if (auth == "onstarting") h.Response.OnStarting(() => { h.Response.Headers.Append("Set-Cookie", "last_room=10; path=/"); return Task.CompletedTask; });
    ctx.EnableOutputCaching = true; ctx.AllowCacheLookup = true; ctx.AllowCacheStorage = true; ctx.AllowLocking = true;
    ctx.CacheVaryByRules.QueryKeys = "*";
    ctx.ResponseExpirationTimeSpan = TimeSpan.FromMinutes(5);
    return default;
  }
  public ValueTask ServeFromCacheAsync(OutputCacheContext ctx, CancellationToken ct) {
    if (ctx.HttpContext.Request.Headers["X-Auth"] == "stale") { ctx.AllowCacheLookup = false; }
    return default;
  }
  public ValueTask ServeResponseAsync(OutputCacheContext ctx, CancellationToken ct) {
    var sc = ctx.HttpContext.Response.Headers.SetCookie;
    if (sc.Any(x => x.StartsWith("session_token"))) ctx.AllowCacheStorage = false;
    if (ctx.HttpContext.Request.Headers["X-Auth"] == "serveresp") ctx.HttpContext.Response.Headers.Append("Set-Cookie", "last_room=13; path=/");
    return default;
  }
}
