#!/bin/bash
# KOR-MESH01: install MeshCentral as a locked-down systemd service. Run with sudo. Idempotent.
# Usage: sudo bash install-meshcentral.sh <admin-email> <admin-password-file>
set -euo pipefail
ADMIN_EMAIL="$1"
PASS_FILE="$2"
HOST=kor-mesh01.int.korstructural.com
DIR=/opt/meshcentral

echo "--- Node.js 22 LTS (NodeSource apt repo: keeps getting security fixes on 24.04)"
if ! command -v node >/dev/null || ! node -v | grep -q '^v22\.'; then
  curl -fsSL https://deb.nodesource.com/setup_22.x -o /tmp/nodesource_setup.sh
  bash /tmp/nodesource_setup.sh >/dev/null
  apt-get install -y nodejs >/dev/null
fi
echo "node $(node -v), npm $(npm -v)"
# let unattended-upgrades patch Node as well as Ubuntu
cat > /etc/apt/apt.conf.d/52kor-nodesource <<'EOF'
Unattended-Upgrade::Origins-Pattern { "origin=Node Source"; "origin=nodesource"; };
EOF

echo "--- service account and folder"
id meshcentral >/dev/null 2>&1 || useradd --system --home-dir "$DIR" --shell /usr/sbin/nologin meshcentral
mkdir -p "$DIR/meshcentral-data"
chown -R meshcentral:meshcentral "$DIR"
chmod 750 "$DIR"

echo "--- MeshCentral from npm"
cd "$DIR"
sudo -u meshcentral -H npm install --no-fund --no-audit --loglevel=error meshcentral >/dev/null
echo "meshcentral $(sudo -u meshcentral node -p "require('$DIR/node_modules/meshcentral/package.json').version")"

echo "--- config"
cat > "$DIR/meshcentral-data/config.json" <<EOF
{
  "\$schema": "https://raw.githubusercontent.com/Ylianst/MeshCentral/master/meshcentral-config-schema.json",
  "settings": {
    "cert": "$HOST",
    "port": 443,
    "redirPort": 0,
    "selfUpdate": false,
    "allowFraming": false,
    "cookieIpCheck": true,
    "agentPong": 300,
    "noAgentUpdate": false,
    "_note": "KOR NetworkOps remote screen. LAN + VPN only (ufw). Built 2026-09-30."
  },
  "domains": {
    "": {
      "title": "KOR Remote",
      "title2": "NetworkOps",
      "newAccounts": false,
      "userNameIsEmail": true,
      "passwordRequirements": { "min": 16, "force2factor": true, "hint": false },
      "agentConfig": [ "webSocketMaskOverride=1" ]
    }
  }
}
EOF
chown meshcentral:meshcentral "$DIR/meshcentral-data/config.json"
chmod 640 "$DIR/meshcentral-data/config.json"

echo "--- admin account (break-glass; day-to-day sign-in will be Entra)"
systemctl stop meshcentral 2>/dev/null || true
if ! sudo -u meshcentral -H node "$DIR/node_modules/meshcentral" --listuserids 2>/dev/null | grep -qi "user//${ADMIN_EMAIL,,}"; then
  sudo -u meshcentral -H node "$DIR/node_modules/meshcentral" --createaccount "$ADMIN_EMAIL" --pass "$(cat "$PASS_FILE")" --email "$ADMIN_EMAIL" >/dev/null
fi
sudo -u meshcentral -H node "$DIR/node_modules/meshcentral" --adminaccount "$ADMIN_EMAIL" >/dev/null
shred -u "$PASS_FILE"

echo "--- systemd"
cat > /etc/systemd/system/meshcentral.service <<EOF
[Unit]
Description=MeshCentral (KOR remote screen)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=meshcentral
Group=meshcentral
WorkingDirectory=$DIR
ExecStart=/usr/bin/node $DIR/node_modules/meshcentral
Restart=always
RestartSec=10
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
NoNewPrivileges=true
ProtectSystem=full
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now meshcentral >/dev/null 2>&1
for i in $(seq 1 60); do
  if ss -ltn | grep -q ':443 '; then break; fi
  sleep 2
done
echo "--- state"
systemctl is-active meshcentral
ss -ltnp | grep ':443 ' || echo "NOT LISTENING on 443"
journalctl -u meshcentral --no-pager -n 15
