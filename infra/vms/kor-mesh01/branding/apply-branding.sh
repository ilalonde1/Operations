#!/bin/bash
# Applies KOR Remote's config and branding on KOR-MESH01, then restarts MeshCentral once.
# Run ON the VM as root, from a folder holding: meshcentral-config.json, kor-remote-logo.png, kor-remote-mark.png, custom.css.
#   sudo bash apply-branding.sh
# Backs up the live config first; validates the new JSON BEFORE touching anything; restores the backup and restarts
# on the old config if MeshCentral does not come back.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
data=/opt/meshcentral/meshcentral-data
web=/opt/meshcentral/meshcentral-web/public/styles
stamp=$(date +%Y%m%d-%H%M%S)

for f in meshcentral-config.json kor-remote-logo.png kor-remote-mark.png custom.css custom.js; do
  [ -s "$here/$f" ] || { echo "missing $f beside this script"; exit 1; }
done
python3 -m json.tool "$here/meshcentral-config.json" > /dev/null || { echo "config is not valid JSON: nothing changed"; exit 1; }

cp -p "$data/config.json" "$data/config.json.bak-$stamp"
echo "backed up config.json -> config.json.bak-$stamp"

install -o meshcentral -g meshcentral -m 640 "$here/meshcentral-config.json" "$data/config.json"
install -o meshcentral -g meshcentral -m 644 "$here/kor-remote-logo.png" "$data/kor-remote-logo.png"
install -o meshcentral -g meshcentral -m 644 "$here/kor-remote-mark.png" "$data/kor-remote-mark.png"
install -d -o meshcentral -g meshcentral -m 755 "$web"
install -o meshcentral -g meshcentral -m 644 "$here/custom.css" "$web/custom.css"
install -d -o meshcentral -g meshcentral -m 755 "$(dirname "$web")/images"
for icon in "$here"/icons/*.png; do install -o meshcentral -g meshcentral -m 644 "$icon" "$(dirname "$web")/images/"; done
install -d -o meshcentral -g meshcentral -m 755 "$(dirname "$web")/scripts"
install -o meshcentral -g meshcentral -m 644 "$here/custom.js" "$(dirname "$web")/scripts/custom.js"

systemctl restart meshcentral
for i in $(seq 1 30); do
  sleep 2
  code=$(curl -sk -o /dev/null -w '%{http_code}' https://127.0.0.1/ || true)
  [ "$code" = "200" ] && { echo "MeshCentral back: HTTP 200 after $((i*2)) s"; exit 0; }
done

echo "MeshCentral did not answer 200 in 60 s: restoring the previous config"
journalctl -u meshcentral -n 30 --no-pager || true
cp -p "$data/config.json.bak-$stamp" "$data/config.json"
systemctl restart meshcentral
exit 1
