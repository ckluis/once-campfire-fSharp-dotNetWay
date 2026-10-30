#!/usr/bin/env bash
# The security review's comparison with Rails (plans/design-richtext.md section 11): hostile cases from the harness through the
# reference image's own pipeline (generate.rb), then ours against them. Writes into target/richtext-review/ (gitignored).
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
out="$root/target/richtext-review"
mkdir -p "$out"
cd "$root/reference-tools/richtext/review"
dotnet run -c Release -- emit "${CASES:-4000}" "$out/inputs.json"
# Psych reads JSON as YAML but not every escape System.Text.Json writes, so Ruby converts it first
docker run --rm --env-file "$root/parity/.env.reference" -e RAILS_LOG_LEVEL=error -e DATABASE_URL=sqlite3:/tmp/richtext.sqlite3 \
  -e RICHTEXT_FUZZ_CASES=0 -e RICHTEXT_MUTATION_CASES=0 -e RICHTEXT_INPUT=/corpus/inputs.yml -e RICHTEXT_OUTPUT=/corpus/big.json \
  -v "$root/reference-tools/richtext:/tools:ro" -v "$out:/corpus" --user "$(id -u):$(id -g)" campfire-reference \
  bash -c "ruby -rjson -ryaml -e 'File.write(\"/corpus/inputs.yml\", JSON.parse(File.read(\"/corpus/inputs.json\")).to_yaml)' && bin/rails db:schema:load >/dev/null && bin/rails runner /tools/generate.rb" >/dev/null 2>&1
dotnet run -c Release -- compare "$out/big.json"
