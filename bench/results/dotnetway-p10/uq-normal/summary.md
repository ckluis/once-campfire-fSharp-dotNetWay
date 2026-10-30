# upstream-quick (normal profile, 3 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 4,842 (4,770-4,982) | 35,401 (35,397-36,342) **7.31x** | 34,573 (33,947-34,690) **7.14x** |
| messages_page | 4,909 (4,818-5,000) | 41,896 (41,721-42,302) **8.53x** | 33,198 (32,240-33,438) **6.76x** |
| sidebar | 5,242 (5,136-5,244) | 37,691 (36,938-37,990) **7.19x** | 41,719 (40,576-41,982) **7.96x** |
| search | 5,124 (5,067-5,360) | 33,658 (33,566-33,851) **6.57x** | 41,206 (40,464-41,208) **8.04x** |
| post_message | 401 (394-407) | 9,501 (9,401-9,507) **23.68x** | 2,885 (2,838-2,911) **7.19x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `903d2a8` dirty=false  image `sha256:1556e0e413b4`
- seed sha256 `e7b7f2b4`, harness `eb45b63`

Host:
- before: {"spin_ms": 59.08, "spin_ms_max_cpu": 60.98, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.063} load=2.99 2.88 2.80 1/205 2073381
- after: {"spin_ms": 58.72, "spin_ms_max_cpu": 61.81, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.057} load=4.67 4.63 3.76 1/200 2076441
- commit=903d2a819df3d31d771cc6a6d4f28c4cdb8d3aec image=campfire-dotnetway:uq-903d2a8 mounted= mixed=0 harness=eb45b63
