# upstream-quick (mixed profile, 2 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 2,754 (2,717-2,790) | 33,690 (33,350-34,031) **12.23x** | 2,969 (2,931-3,006) **1.08x** |
| messages_page | 3,454 (3,375-3,534) | 41,618 (41,259-41,977) **12.05x** | 6,676 (6,641-6,711) **1.93x** |
| sidebar | 4,257 (4,212-4,301) | 37,851 (37,702-38,001) **8.89x** | 7,481 (7,471-7,491) **1.76x** |
| search | 3,999 (3,965-4,034) | 35,011 (34,958-35,063) **8.75x** | 6,940 (6,898-6,982) **1.74x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `dcc9434` dirty=false  image `sha256:acdc23f914c7`
- seed sha256 `e3a7302e`, harness `f3103c7`

Host:
- before: {"spin_ms": 58.13, "spin_ms_max_cpu": 62.98, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.046} load=5.40 4.16 3.27 1/192 1033316
- after: {"spin_ms": 58.79, "spin_ms_max_cpu": 62.31, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.058} load=6.10 4.79 3.74 1/193 1035193
- commit=dcc9434d3afaed16c1976e817334661c245fa36e image=campfire-dotnetway:app mounted= mixed=1 harness=f3103c7
