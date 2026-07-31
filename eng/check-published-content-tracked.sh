#!/bin/sh
# Every file a .csproj publishes must be tracked by git.
#
# Why this exists: `src/Hms.Hr.Web/vendor-public-key.pem` was declared as <Content> and copied to
# the publish directory, but `.gitignore`'s blanket `*.pem` kept it untracked. Local builds passed
# — the file was sitting on the developer's disk — and the failure surfaced only on the VM, in a
# clean checkout, as MSB3030 halfway through a Docker build. That is an expensive place to learn it.
#
# A file that is published but not committed is, by definition, a file that only exists on the
# machine that created it. Catch it here instead.
set -eu

fail=0

# Content/None items with an explicit path, i.e. the ones naming a real file rather than a glob.
for proj in $(git ls-files '*.csproj'); do
    dir=$(dirname "$proj")
    grep -oE '<(Content|None) Include="[^"*?]+"' "$proj" 2>/dev/null \
        | sed -E 's/.*Include="([^"]+)".*/\1/' \
        | while IFS= read -r rel; do
            [ -n "$rel" ] || continue
            # MSBuild uses backslashes; normalise for the filesystem.
            path="$dir/$(printf '%s' "$rel" | tr '\\' '/')"
            if ! git ls-files --error-unmatch "$path" >/dev/null 2>&1; then
                echo "NOT TRACKED: $path"
                echo "    declared in $proj — it would be missing from a clean checkout."
                exit 1
            fi
        done || fail=1
done

if [ "$fail" -ne 0 ]; then
    echo
    echo "A published file is not committed. Either commit it, or (if it is genuinely a secret)"
    echo "stop publishing it and supply it at deploy time instead."
    exit 1
fi

echo "check-published-content-tracked: OK"
