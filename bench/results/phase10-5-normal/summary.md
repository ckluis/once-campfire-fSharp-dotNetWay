# upstream-quick (normal profile, 2 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 4,904 (4,902-4,906) | 36,066 (35,723-36,410) **7.35x** | 34,958 (34,454-35,462) **7.13x** |
| messages_page | 4,801 (4,684-4,919) | 41,854 (41,489-42,218) **8.72x** | 33,317 (33,103-33,530) **6.94x** |
| sidebar | 5,139 (5,044-5,233) | 38,295 (38,210-38,381) **7.45x** | 41,412 (41,260-41,564) **8.06x** |
| search | 5,064 (4,920-5,209) | 34,208 (34,176-34,242) **6.75x** | 41,059 (40,367-41,750) **8.11x** |
| post_message | 395 (395-395) | 9,488 (9,431-9,544) **24.0x** | 2,816 (2,756-2,876) **7.12x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `a993bc8` dirty=false  image `sha256:00021c7cd8db`
- seed sha256 `e3a7302e`, harness `f3103c7`

Host:
- before: {"spin_ms": 58.72, "spin_ms_max_cpu": 64.7, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.057} load=1.11 1.09 1.77 1/204 1460530
- after: {"spin_ms": 59.91, "spin_ms_max_cpu": 61.87, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.078} load=5.79 4.08 2.90 1/200 1462582
- commit=a993bc80d5c069cc4bab9ccee5a012343e4cacdd image=campfire-dotnetway:uq-a993bc8 mounted= mixed=0 harness=f3103c7
