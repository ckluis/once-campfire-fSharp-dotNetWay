# upstream-quick (normal profile, 2 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | dotnetway |
|---|---|---|
| sidebar | 5,474 (5,414-5,535) | 3,579 (3,471-3,687) **0.65x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- dotnetway: `25ac9bd` dirty=false  image `sha256:cc2643266d83`
- seed sha256 `e3a7302e`, harness `f3103c7`

Host:
- before: {"spin_ms": 59.33, "spin_ms_max_cpu": 61.0, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.068} load=1.80 3.25 3.30 1/215 1039020
- after: {"spin_ms": 59.54, "spin_ms_max_cpu": 62.09, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.071} load=3.92 3.60 3.42 1/214 1040165
- commit=25ac9bd4ede485bc6d474b6315d020259f8bd86c image=campfire-dotnetway:uq-25ac9bd mounted= mixed=0 harness=f3103c7
