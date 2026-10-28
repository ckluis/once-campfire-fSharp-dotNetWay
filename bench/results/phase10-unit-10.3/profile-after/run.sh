cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
for r in room_show sidebar; do
 FSHARP_IMAGE=campfire-dotnetway:app bench/breakdown profile --routes $r --secs 8 --out /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/profile-after >> /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/profile-after/log.txt 2>&1
 cp /var/tmp/diag/trace.speedscope.json /var/tmp/p10-3a-$r.speedscope.json
done
echo 0 > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/profile-after/STATUS
