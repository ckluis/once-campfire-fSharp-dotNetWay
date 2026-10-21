// plant: hybridcache
let a (cache: HybridCache) = cache.GetOrCreateAsync("k", fun _ -> ValueTask.FromResult 1)
