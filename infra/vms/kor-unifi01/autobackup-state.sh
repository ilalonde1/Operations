#!/bin/bash
# READ-ONLY: UniFi Network auto-backup settings, and where the backup files live on the host.
u=uosserver; uid=$(id -u "$u")
cat > /tmp/a.js <<'EOF'
db = db.getSiblingDB('ace');
db.setting.find({key: {$in: ['super_mgmt', 'autobackup']}}).forEach(s => {
  var keys = Object.keys(s).filter(k => /backup/i.test(k));
  print(s.key + ': ' + keys.map(k => k + '=' + JSON.stringify(s[k])).join(' '));
});
EOF
chmod 644 /tmp/a.js
sudo -u "$u" bash -c "cd /tmp && XDG_RUNTIME_DIR=/run/user/$uid podman cp /tmp/a.js uosserver:/tmp/a.js && XDG_RUNTIME_DIR=/run/user/$uid podman exec uosserver sh -c 'mongo --quiet --port 27117 /tmp/a.js; unlink /tmp/a.js; echo --- in container; ls -la /data/unifi/data/backup/autobackup 2>/dev/null || ls -la /usr/lib/unifi/data/backup 2>/dev/null; find / -xdev -name \"*.unf\" 2>/dev/null | head -20'"
unlink /tmp/a.js
echo "--- volumes on the host"
sudo -u "$u" bash -c "cd /tmp && XDG_RUNTIME_DIR=/run/user/$uid podman volume ls --format '{{.Name}} {{.Mountpoint}}'"
