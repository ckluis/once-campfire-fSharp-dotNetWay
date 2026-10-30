Control, 1 rep each, same session, alternating images: the port's campfire-fsharp:app (a78972f) and campfire-dotnetway:app (same src). req/s

| workload | c | port image | dotnetway image | ratio |
|---|---|---:|---:|---:|
| room_show | 1 | 6,159 | 5,753 | 0.934x |
| room_show | 16 | 18,462 | 16,829 | 0.912x |
| messages_page | 1 | 5,965 | 4,593 | 0.770x |
| messages_page | 16 | 16,489 | 16,916 | 1.026x |
| sidebar | 1 | 5,962 | 5,683 | 0.953x |
| sidebar | 16 | 18,708 | 24,456 | 1.307x |
| search | 1 | 4,636 | 4,842 | 1.045x |
| search | 16 | 21,194 | 27,702 | 1.307x |
| post_message | 1 | 1,988 | 2,077 | 1.045x |
| post_message | 16 | 3,902 | 3,437 | 0.881x |
