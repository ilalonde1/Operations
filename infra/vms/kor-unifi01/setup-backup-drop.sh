#!/bin/bash
# ON KOR-UNIFI01 (as root). Idempotent. The read-only drop that NetworkOps pulls UniFi backups from.
#   korbackup  system user: no shell, no password, key only, SFTP only, READ-ONLY, chrooted to /srv/sftp/korbackup
#   timer      03:15 daily: copy new UniFi auto-backups (.unf) into the drop; keep 14 days there
set -euo pipefail
PUBKEY="$1"
id korbackup >/dev/null 2>&1 || useradd --system --no-create-home --home-dir /srv/sftp/korbackup --shell /usr/sbin/nologin korbackup
passwd -l korbackup >/dev/null
install -d -o root -g root -m 755 /srv/sftp /srv/sftp/korbackup           # chroot root must be root-owned, not group-writable
install -d -o root -g korbackup -m 750 /srv/sftp/korbackup/unifi
printf '%s\n' "restrict $PUBKEY" > /etc/ssh/korbackup_authorized_keys
chmod 644 /etc/ssh/korbackup_authorized_keys

cat > /etc/ssh/sshd_config.d/60-korbackup.conf <<'EOF'
# NetworkOps (KOR-APP01) pulls UniFi backups: SFTP only, read-only, jailed, key only.
Match User korbackup
    AuthorizedKeysFile /etc/ssh/korbackup_authorized_keys
    ChrootDirectory /srv/sftp/korbackup
    ForceCommand internal-sftp -R
    PasswordAuthentication no
    KbdInteractiveAuthentication no
    AllowTcpForwarding no
    AllowAgentForwarding no
    X11Forwarding no
    PermitTTY no
EOF
sshd -t
systemctl reload ssh

cat > /usr/local/sbin/kor-unifi-backup-stage <<'EOF'
#!/bin/bash
# Copies new UniFi auto-backups into the read-only drop NetworkOps pulls from; keeps 14 days there.
set -euo pipefail
src=/home/uosserver/.local/share/containers/storage/volumes/uosserver_var_lib_unifi/_data/backup/autobackup
dst=/srv/sftp/korbackup/unifi
n=0
shopt -s nullglob
for f in "$src"/*.unf; do
    b=$(basename "$f")
    if [ ! -f "$dst/$b" ]; then install -o root -g korbackup -m 640 -p "$f" "$dst/$b"; n=$((n+1)); fi
done
find "$dst" -name '*.unf' -type f -mtime +14 -delete
logger -t kor-unifi-backup "staged $n new backup(s); $(ls "$dst"/*.unf 2>/dev/null | wc -l) in drop"
EOF
chmod 750 /usr/local/sbin/kor-unifi-backup-stage

cat > /etc/systemd/system/kor-unifi-backup-stage.service <<'EOF'
[Unit]
Description=Stage UniFi auto-backups for NetworkOps to pull
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/kor-unifi-backup-stage
EOF
cat > /etc/systemd/system/kor-unifi-backup-stage.timer <<'EOF'
[Unit]
Description=Stage UniFi auto-backups daily at 03:15 (UniFi backs up at 01:00; NetworkOps pulls at 04:00)
[Timer]
OnCalendar=*-*-* 03:15:00
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now kor-unifi-backup-stage.timer >/dev/null
/usr/local/sbin/kor-unifi-backup-stage

echo "--- effective sshd for korbackup:"
sshd -T -C user=korbackup,host=kor-app01,addr=192.168.1.32 | grep -E '^(chrootdirectory|forcecommand|passwordauthentication|allowtcpforwarding|permittty|authorizedkeysfile)'
echo "--- timer:"; systemctl list-timers kor-unifi-backup-stage.timer --no-pager | sed -n 2p
echo "--- drop:"; ls -la /srv/sftp/korbackup/unifi
echo "--- host key fingerprint (for pinning):"; ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
