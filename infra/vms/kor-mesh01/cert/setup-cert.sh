#!/bin/bash
# One-time setup on KOR-MESH01 for KOR Remote's Let's Encrypt certificate (kor-mesh-cert.sh says how it works).
# Run as root from the folder holding these files. Safe to run again: it never re-registers an acme-dns account that exists
# (the CNAME Ian adds points at THAT account) and never touches MeshCentral -- only kor-mesh-cert-install.sh does, after a
# certificate is issued.
set -euo pipefail
TAG=3.1.6                      # acme.sh release, pinned (commit 807da649..., 2026-09-20)
CONF=/etc/kor-mesh-cert
cd "$(dirname "$0")"

# acme.sh, from its pinned release
if [ ! -x /opt/acme.sh/acme.sh ]; then
    rm -rf /opt/acme.sh-src
    git clone -q --depth 1 --branch "$TAG" https://github.com/acmesh-official/acme.sh.git /opt/acme.sh-src
    (cd /opt/acme.sh-src && ./acme.sh --install --home /opt/acme.sh --config-home "$CONF/acme" --nocron --noprofile >/dev/null)
fi
/opt/acme.sh/acme.sh --home /opt/acme.sh --config-home "$CONF/acme" --set-default-ca --server letsencrypt >/dev/null
/opt/acme.sh/acme.sh --home /opt/acme.sh --config-home "$CONF/acme" --register-account --server letsencrypt >/dev/null

# The acme-dns account the public CNAME delegates to (auth.acme-dns.io). Its password is in a root-only file, nowhere else.
mkdir -p "$CONF"; chmod 700 "$CONF"
if [ ! -s "$CONF/acmedns.env" ]; then
    reg=$(curl -s -m 30 -X POST https://auth.acme-dns.io/register)
    python3 - "$reg" > "$CONF/acmedns.env" <<'PY'
import json, sys
r = json.loads(sys.argv[1])
print("ACMEDNS_BASE_URL=https://auth.acme-dns.io")
print("ACMEDNS_USERNAME=" + r["username"])
print("ACMEDNS_PASSWORD=" + r["password"])
print("ACMEDNS_SUBDOMAIN=" + r["subdomain"])
print("ACMEDNS_FULLDOMAIN=" + r["fulldomain"])
PY
    chmod 600 "$CONF/acmedns.env"
fi

install -m 755 kor-mesh-cert.sh /usr/local/sbin/kor-mesh-cert
install -m 755 kor-mesh-cert-install.sh /usr/local/sbin/kor-mesh-cert-install
install -m 644 kor-mesh-cert.service kor-mesh-cert.timer /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now kor-mesh-cert.timer >/dev/null

. "$CONF/acmedns.env"
echo "THE ONE RECORD for Register.ca (zone korstructural.com):"
echo "  host  _acme-challenge.kor-mesh01.int   type CNAME   value  $ACMEDNS_FULLDOMAIN"
