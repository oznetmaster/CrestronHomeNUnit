#!/bin/bash
# Copyright (c) 2026 Neil Colvin. MIT licensed.
# Usage: bash with-appium.sh /private/job-appium.plist command [arguments...]
# Existing plist must bind Appium to 127.0.0.1:4727 and Mac2 to 127.0.0.1:10100.
set -euo pipefail
if [ "$#" -lt 2 ]; then echo 'Provide a job-owned LaunchAgent plist and test command.' >&2; exit 2; fi
plist="$1"; shift
label=$(/usr/libexec/PlistBuddy -c 'Print :Label' "$plist")
/usr/bin/python3 - "$plist" <<'PY'
import plistlib, sys
with open(sys.argv[1], 'rb') as stream:
    arguments = plistlib.load(stream)['ProgramArguments']
for flag, expected in [('--address', '127.0.0.1'), ('--port', '4727')]:
    if arguments.count(flag) != 1 or arguments[arguments.index(flag) + 1] != expected:
        raise SystemExit('LaunchAgent must bind Appium explicitly to 127.0.0.1:4727')
PY
if [[ ! "$label" =~ ^[a-zA-Z0-9.-]+$ ]]; then echo 'Invalid job label.' >&2; exit 2; fi
domain="gui/$(id -u)"
if [ "$(stat -f '%Su' /dev/console)" != "$(id -un)" ]; then echo 'Run as the logged-in GUI user.' >&2; exit 2; fi
if /usr/sbin/ioreg -n Root -d1 | grep -q 'CGSSessionScreenIsLocked"=Yes'; then echo 'Unlock the Mac console before testing.' >&2; exit 2; fi
if /bin/launchctl print "$domain/$label" >/dev/null 2>&1; then echo 'Job service is already active; refusing to take ownership.' >&2; exit 2; fi
if /usr/sbin/lsof -nP -iTCP:4727 -iTCP:10100 -sTCP:LISTEN >/dev/null 2>&1; then echo 'Automation ports are already in use.' >&2; exit 2; fi
# Only the creator removes this guard. A crash intentionally leaves it for reconciliation.
guard="${plist}.job-lock"
mkdir "$guard" || { echo 'Existing job lock requires reconciliation.' >&2; exit 2; }
owned=0
awake_pid=''
cleanup() {
  result=$?
  trap - EXIT
  if [ -n "$awake_pid" ]; then kill "$awake_pid" 2>/dev/null || true; wait "$awake_pid" 2>/dev/null || true; fi
  if [ "$owned" = 1 ]; then
    /bin/launchctl bootout "$domain/$label" >/dev/null 2>&1 || result=1
    for attempt in 1 2 3 4 5; do
      if ! /usr/sbin/lsof -nP -iTCP:4727 -iTCP:10100 -sTCP:LISTEN >/dev/null 2>&1; then break; fi
      sleep 1
    done
    if /usr/sbin/lsof -nP -iTCP:4727 -iTCP:10100 -sTCP:LISTEN >/dev/null 2>&1; then
      echo 'Automation shutdown unconfirmed; job lock retained.' >&2; exit 1
    fi
  fi
  rmdir "$guard"
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT TERM
# Keep this job awake without changing system settings. Assertions also expire if
# this shell exits unexpectedly. This cannot unlock an already locked desktop.
/usr/bin/caffeinate -di -w "$$" &
awake_pid=$!
/bin/launchctl bootstrap "$domain" "$plist"
owned=1
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10; do
  if /usr/bin/curl --fail --silent --max-time 2 http://127.0.0.1:4727/status >/dev/null; then ready=1; break; fi
  sleep 1
done
if [ "$ready" != 1 ]; then echo 'Appium did not become ready.' >&2; exit 1; fi
"$@"
