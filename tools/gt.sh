#!/bin/sh
# Test helpers for The Last Witness (GT started with only this mod, from C:\mctag\testbep-witness).
#   tools/gt.sh restart          close my GT copy, install the fresh build, start GT, wait until ready
#   tools/gt.sh cmd "LINE"...    send test commands (see Plugin.TestHooks), print new log lines
#   tools/gt.sh shot NAME        save shots/NAME.png (scaled to 960 wide)
D="$LOCALAPPDATA/TheLastWitness"
B=/c/mctag/testbep-witness/BepInEx
L=$B/LogOutput.log
case "$1" in
restart)
  # only my test copy (started with testbep-witness), never the player's own game
  powershell -c "Get-CimInstance Win32_Process -Filter \"Name='Gorilla Tag.exe'\" | Where-Object { \$_.CommandLine -like '*testbep-witness*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId }"
  sleep 4
  cp /c/mctag/TheLastWitness/gorillatag/bin/Release/net472/TheLastWitness.dll $B/plugins/TheLastWitness/ || exit 1
  [ -f /c/mctag/TheLastWitness/unity/Build/witness.bundle ] && cp /c/mctag/TheLastWitness/unity/Build/witness.bundle $B/plugins/TheLastWitness/
  rm -f "$L"
  "/c/Program Files (x86)/Steam/steam.exe" -applaunch 1533390 --doorstop-target-assembly "C:\mctag\testbep-witness\BepInEx\core\BepInEx.Preloader.dll" &
  for i in $(seq 1 100); do sleep 3; grep -q "TheLastWitness: ready" "$L" 2>/dev/null && break; done
  sleep 2
  grep -a "TheLastWitness\|Exception" "$L" | head -20
  ;;
cmd)
  shift
  n=$(wc -l < "$L")
  for c in "$@"; do echo "$c"; done > "$D/cmd.req"
  sleep 2
  tail -n +$((n + 1)) "$L" | grep -a "TheLastWitness\|Exception" | head -30
  ;;
shot)
  rm -f "$D/$2.png"
  echo "snap $2" > "$D/cmd.req"
  for i in $(seq 1 20); do sleep 1; [ -f "$D/$2.png" ] && break; done
  sleep 1
  mkdir -p /c/mctag/TheLastWitness/shots
  /c/mctag/tools/watchbin/ffmpeg.exe -y -loglevel error -i "$D/$2.png" -vf scale=960:-1 "/c/mctag/TheLastWitness/shots/$2.png" && echo "C:\\mctag\\TheLastWitness\\shots\\$2.png"
  ;;
esac
