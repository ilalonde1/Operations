#!/bin/bash
# KOR Remote's real certificate (Let's Encrypt) for kor-mesh01.int.korstructural.com -- daily, from kor-mesh-cert.timer.
#
# The name is internal, so Let's Encrypt cannot reach it over HTTP: it proves the name through DNS (DNS-01). Register.ca
# has no API, so ONE public record delegates just the proof to an acme-dns account (setup-cert.sh made it):
#     _acme-challenge.kor-mesh01.int.korstructural.com  CNAME  <ACMEDNS_FULLDOMAIN>
# Until that CNAME is visible in public DNS this does nothing but say it is waiting (no failed orders against Let's
# Encrypt's rate limits). Once it is: issue (RSA 2048 -- MeshCentral reads keys with node-forge), stage the files, and
# kor-mesh-cert-install.sh puts them into MeshCentral and proves they work, or rolls back. After that, renewals (every
# ~60 days) run the same install.
set -euo pipefail
NAME=kor-mesh01.int.korstructural.com
HOME_DIR=/opt/acme.sh
CONF=/etc/kor-mesh-cert
LOG=/var/log/kor-mesh-cert.log
log() { echo "$(date -Is) daily: $*" | tee -a "$LOG"; }

set -a; . "$CONF/acmedns.env"; set +a      # ACMEDNS_BASE_URL / _USERNAME / _PASSWORD / _SUBDOMAIN / _FULLDOMAIN
ACME="$HOME_DIR/acme.sh --home $HOME_DIR --config-home $CONF/acme"

if [ ! -f "$CONF/issued" ]; then
    # Public DNS (Cloudflare's resolver over HTTPS, so DC01's internal zones cannot answer for it).
    seen=$(curl -s -m 20 -H 'accept: application/dns-json' "https://cloudflare-dns.com/dns-query?name=_acme-challenge.$NAME&type=CNAME" || true)
    case "$seen" in
        *"$ACMEDNS_FULLDOMAIN"*) log "the CNAME is in public DNS: issuing" ;;
        *) log "waiting for the CNAME at Register.ca: _acme-challenge.kor-mesh01.int -> $ACMEDNS_FULLDOMAIN"; exit 0 ;;
    esac
    $ACME --issue --server letsencrypt --dns dns_acmedns -d "$NAME" --keylength 2048 >> "$LOG" 2>&1 || { log "issue FAILED (see above)"; exit 1; }
    mkdir -p "$CONF/staged"; chmod 700 "$CONF/staged"
    $ACME --install-cert -d "$NAME" --cert-file "$CONF/staged/cert.pem" --key-file "$CONF/staged/key.pem" --ca-file "$CONF/staged/ca.pem" \
        --reloadcmd /usr/local/sbin/kor-mesh-cert-install >> "$LOG" 2>&1 || { log "install FAILED (see above)"; exit 1; }
    touch "$CONF/issued"
    log "issued and installed"
else
    # Renews only when due (acme.sh's own 60-day rule); a renewal runs the same install, with the same proof and rollback.
    $ACME --cron >> "$LOG" 2>&1 || { log "renewal run FAILED (see above)"; exit 1; }
    log "renewal check done; serving $(openssl x509 -in "$CONF/staged/cert.pem" -noout -enddate | cut -d= -f2) expiry"
fi
