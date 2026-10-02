#!/bin/bash
# KOR Remote: puts the certificate acme.sh just issued or renewed into MeshCentral, restarts it, and PROVES it took:
# MeshCentral must serve exactly the new certificate, and the agents must come back. Otherwise the previous certificate is
# put back and MeshCentral restarted on it -- the site is never left on a certificate that did not work.
# Run by acme.sh as its --reloadcmd (root). Layout is MeshCentral 1.2.5's own (certoperations.js): leaf in
# webserver-cert-public.crt, key in webserver-cert-private.key, intermediates in webserver-cert-chain1.crt. The name must
# equal config.json "cert" (kor-mesh01.int.korstructural.com) or MeshCentral regenerates its own certificate instead.
set -euo pipefail
NAME=kor-mesh01.int.korstructural.com
DATA=/opt/meshcentral/meshcentral-data
STAGED=/etc/kor-mesh-cert/staged
LOG=/var/log/kor-mesh-cert.log
log() { echo "$(date -Is) install: $*" | tee -a "$LOG"; }
agents() { ss -Htn state established '( sport = :443 or sport = :4445 )' | wc -l; }
served() { echo | timeout 10 openssl s_client -connect 127.0.0.1:443 -servername "$NAME" 2>/dev/null | openssl x509 -noout -fingerprint -sha256 2>/dev/null || true; }

# The staged files must be a whole, matching, current certificate for this name before anything is touched.
openssl x509 -in "$STAGED/cert.pem" -noout -checkend 604800 >/dev/null || { log "REFUSED: staged certificate expires within 7 days"; exit 1; }
openssl x509 -in "$STAGED/cert.pem" -noout -ext subjectAltName | grep -q "DNS:$NAME" || { log "REFUSED: staged certificate is not for $NAME"; exit 1; }
[ "$(openssl x509 -in "$STAGED/cert.pem" -noout -pubkey | sha256sum)" = "$(openssl pkey -in "$STAGED/key.pem" -pubout | sha256sum)" ] || { log "REFUSED: staged key does not match the certificate"; exit 1; }
[ -s "$STAGED/ca.pem" ] || { log "REFUSED: no intermediate chain staged"; exit 1; }
want=$(openssl x509 -in "$STAGED/cert.pem" -noout -fingerprint -sha256)
if [ "$(served)" = "$want" ]; then log "already serving $want: nothing to do"; exit 0; fi

before=$(agents)
backup="$DATA/certbackup-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$backup"
cp -p "$DATA/webserver-cert-public.crt" "$DATA/webserver-cert-private.key" "$backup/"
cp -p "$DATA"/webserver-cert-chain*.crt "$backup/" 2>/dev/null || true
log "backed up the current certificate to $backup; $before agent/browser connections before"

restore() {
    log "ROLLING BACK: $1"
    rm -f "$DATA"/webserver-cert-chain*.crt
    cp -p "$backup"/* "$DATA/"
    systemctl restart meshcentral
    sleep 20
    log "rolled back; serving $(served)"
    exit 1
}

install -o meshcentral -g meshcentral -m 644 "$STAGED/cert.pem" "$DATA/webserver-cert-public.crt"
install -o meshcentral -g meshcentral -m 640 "$STAGED/key.pem" "$DATA/webserver-cert-private.key"
rm -f "$DATA"/webserver-cert-chain*.crt
install -o meshcentral -g meshcentral -m 644 "$STAGED/ca.pem" "$DATA/webserver-cert-chain1.crt"
systemctl restart meshcentral
sleep 20
[ "$(served)" = "$want" ] || restore "MeshCentral is not serving the new certificate (it may have regenerated its own: name mismatch?)"
log "serving the new certificate $want"

# Agents reconnect with back-off (up to ~2 minutes). Judge only when there were enough to judge by.
sleep 160
after=$(agents)
log "$after connections after (was $before)"
if [ "$before" -ge 6 ] && [ "$after" -lt $(( before / 2 )) ]; then restore "fewer than half the connections came back ($after of $before)"; fi
log "DONE: KOR Remote is on the new certificate, valid until $(openssl x509 -in "$STAGED/cert.pem" -noout -enddate | cut -d= -f2)"
