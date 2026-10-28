bench/quick, 3 reps interleaved (control = campfire-dotnetway:p10ctl, label dcc9434; new = :app at fd285c9). req/s median (min-max)

| route | c | control p10ctl | new 10.2 | new/control | new median below control min? |
|---|---|---|---|---|---|
| room_show | 1 | 2,165 (1,887-2,199) | 2,466 (2,461-2,506) | 1.139x | no |
| room_show | 16 | 7,083 (4,126-7,246) | 9,811 (9,747-9,962) | 1.385x | no |
| messages_page | 1 | 1,992 (1,732-2,038) | 2,289 (2,282-2,304) | 1.149x | no |
| messages_page | 16 | 6,949 (3,735-7,084) | 9,613 (9,520-9,620) | 1.383x | no |
| sidebar | 1 | 2,090 (1,899-2,126) | 2,422 (2,391-2,452) | 1.159x | no |
| sidebar | 16 | 7,224 (4,641-7,379) | 10,226 (9,935-10,419) | 1.416x | no |
| search | 1 | 2,005 (1,980-2,009) | 2,125 (2,102-2,132) | 1.060x | no |
| search | 16 | 7,002 (6,798-7,046) | 8,566 (8,528-8,611) | 1.224x | no |
| post_message | 1 | 1,089 (1,074-1,092) | 1,246 (1,236-1,257) | 1.144x | no |
| post_message | 16 | 2,156 (2,132-2,174) | 2,400 (2,398-2,433) | 1.113x | no |
