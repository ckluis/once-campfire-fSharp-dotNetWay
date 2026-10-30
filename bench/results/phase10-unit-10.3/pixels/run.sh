cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
for g in 'rooms/show/*' 'messages/*' 'search/*' 'interactions/sidebar/*,interactions/phone_sidebar'; do
  n=$(echo "$g" | tr -c 'a-z' '_')
  echo "== $g" >> /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/progress.txt
  bin/pixel-parity --fast --seeds default --only "$g" --out /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/parity/out/p10-3-$n > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/$n.log 2>&1
  echo "exit $?" >> /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/progress.txt
  tail -6 /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/$n.log >> /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/progress.txt
done
bin/clean-parity > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/clean.log 2>&1
echo done > /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.3/pixels/STATUS
