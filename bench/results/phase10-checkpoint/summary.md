# upstream-quick (normal profile, 2 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 4,882 (4,762-5,002) | 35,632 (35,604-35,660) **7.3x** | 34,514 (34,382-34,645) **7.07x** |
| messages_page | 4,864 (4,842-4,886) | 42,317 (41,888-42,746) **8.7x** | 33,177 (33,016-33,339) **6.82x** |
| sidebar | 5,322 (5,246-5,399) | 37,658 (36,921-38,395) **7.08x** | 15,891 (15,619-16,163) **2.99x** |
| search | 5,158 (5,144-5,172) | 33,951 (33,483-34,418) **6.58x** | 17,538 (17,346-17,729) **3.4x** |
| post_message | 400 (400-401) | 9,484 (9,482-9,486) **23.68x** | 2,887 (2,862-2,911) **7.21x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `b42dec7` dirty=false  image `sha256:2629af249873`
- seed sha256 `e3a7302e`, harness `f3103c7`

Host:
- before: {"spin_ms": 57.8, "spin_ms_max_cpu": 60.05, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.04} load=3.41 2.56 2.05 1/213 1456077
- after: {"spin_ms": 60.21, "spin_ms_max_cpu": 61.07, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.084} load=5.23 4.08 2.86 1/199 1458132
- commit=b42dec790d49b130166a565654ded2459411228c image=campfire-dotnetway:uq-b42dec7 mounted= mixed=0 harness=f3103c7
