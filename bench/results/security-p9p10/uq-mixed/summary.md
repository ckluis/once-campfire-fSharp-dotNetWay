# upstream-quick (mixed profile, 3 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 2,928 (2,638-3,322) | 33,565 (32,391-34,308) **11.46x** | 31,968 (31,288-32,599) **10.92x** |
| messages_page | 3,387 (3,365-3,680) | 41,820 (41,356-41,849) **12.35x** | 31,788 (30,873-32,379) **9.39x** |
| sidebar | 4,254 (4,147-4,718) | 37,800 (37,376-37,841) **8.89x** | 38,872 (38,111-39,282) **9.14x** |
| search | 3,988 (3,887-4,287) | 34,736 (34,579-34,869) **8.71x** | 38,352 (37,335-38,614) **9.62x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `fed00c4` dirty=false  image `sha256:6905266f6503`
- seed sha256 `e7b7f2b4`, harness `eb45b63`

Host:
- before: {"spin_ms": 59.75, "spin_ms_max_cpu": 62.95, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.075} load=5.19 5.18 3.68 1/204 24509
- after: {"spin_ms": 58.7, "spin_ms_max_cpu": 60.66, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.056} load=4.86 5.43 4.40 1/204 27303
- commit=fed00c4f61902a2f99dfffdf13a01e6c5ed6e6f5 image=campfire-dotnetway:uq-fed00c4 mounted= mixed=1 harness=eb45b63
