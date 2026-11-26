#!/bin/sh
# Prints the pinned release signing certificate's SHA-1 fingerprint from the file given (signing-cert.sha1 at
# the repo root), or nothing when the file is missing, which means signing is off.
# Usage: sh .github/scripts/signing-cert.sh signing-cert.sha1
# The file must be a regular file (not a symlink) holding exactly 40 hex characters (0-9a-fA-F) and at most one
# trailing line break. A file that is present but invalid exits 1, so a typo never turns signing off.
# The same rule as fingerprint_file in scripts/launch-agent.sh and scripts/updater.sh.
set -u

LC_ALL=C
export LC_ALL

file="${1-}"
if [ -z "$file" ]; then
  echo "usage: signing-cert.sh <file>" >&2
  exit 2
fi
if [ ! -e "$file" ] && [ ! -L "$file" ]; then
  exit 0
fi
invalid() {
  echo "$file must be a regular file holding one 40-character SHA-1 fingerprint" >&2
  exit 1
}
if [ ! -f "$file" ] || [ -L "$file" ]; then
  invalid
fi
size=$(wc -c <"$file") || invalid
case $((size + 0)) in
  40) ;;
  41) [ "$(tail -c 1 "$file" | od -An -tx1 | tr -d ' \n')" = 0a ] || invalid ;;
  *) invalid ;;
esac
fingerprint=$(head -c 40 "$file") || invalid
case $fingerprint in
  *[!0123456789abcdefABCDEF]*) invalid ;;
esac
[ ${#fingerprint} -eq 40 ] || invalid
printf '%s\n' "$fingerprint"
