#!/bin/sh
# Puts the release workflow's temporary signing keychain on the user keychain search list, takes it off again,
# and checks that it holds the pinned code-signing identity. codesign finds an identity only in a keychain on that
# list, even with --keychain.
# Usage: sh .github/scripts/signing-keychain.sh add <keychain>
#        sh .github/scripts/signing-keychain.sh remove <keychain>
#        sh .github/scripts/signing-keychain.sh check <keychain> <sha1>
# add puts <keychain> first and keeps the existing keychains in their order; remove keeps all others. check exits 0
# only if find-identity -p codesigning (without -v: the self-signed root is untrusted) lists an identity whose hash
# is <sha1>, ignoring case, and 1 otherwise. A failed security call exits 1, wrong usage exits 2, each with one
# line on stderr. security's output is never printed, and no password is ever passed.
set -u

LC_ALL=C
export LC_ALL

usage() {
  echo "usage: signing-keychain.sh add|remove <keychain> | check <keychain> <sha1>" >&2
  exit 2
}

command="${1-}"
keychain="${2-}"
[ -n "$keychain" ] || usage
case $command in
  add | remove) [ $# -eq 2 ] || usage ;;
  check)
    [ $# -eq 3 ] || usage
    sha1=$(printf '%s' "$3" | tr 'ABCDEF' 'abcdef')
    case $sha1 in
      *[!0123456789abcdef]*) usage ;;
    esac
    [ ${#sha1} -eq 40 ] || usage
    ;;
  *) usage ;;
esac

if [ "$command" = check ]; then
  if ! identities=$(security find-identity -p codesigning "$keychain" 2>/dev/null); then
    echo "security find-identity failed for $keychain" >&2
    exit 1
  fi
  identities=$(printf '%s\n' "$identities" | tr 'ABCDEF' 'abcdef')
  # Identity lines look like:   1) <40 hex> "name" (CSSMERR_TP_NOT_TRUSTED)
  while read -r number hash rest; do
    case $number in
      *[0-9]")")
        if [ "$hash" = "$sha1" ]; then
          exit 0
        fi
        ;;
    esac
  done <<EOF
$identities
EOF
  exit 1
fi

if ! list=$(security list-keychains -d user 2>/dev/null); then
  echo "security list-keychains failed" >&2
  exit 1
fi

# One quoted path per line; each becomes its own argument, so a path with spaces stays whole.
set --
while IFS= read -r line; do
  line=${line#"${line%%[! 	]*}"}
  line=${line%"${line##*[! 	]}"}
  line=${line#\"}
  line=${line%\"}
  if [ -z "$line" ] || [ "$line" = "$keychain" ]; then
    continue
  fi
  set -- "$@" "$line"
done <<EOF
$list
EOF

if [ "$command" = add ]; then
  set -- "$keychain" "$@"
fi
if ! security list-keychains -d user -s "$@" >/dev/null 2>&1; then
  echo "security list-keychains -s failed" >&2
  exit 1
fi
