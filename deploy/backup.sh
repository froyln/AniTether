#!/bin/sh
# Daily pg_dump (custom format) into /backups, keeping BACKUP_KEEP_DAYS days. Connects as
# the read-only anitether_backup role (PGUSER/PGPASSWORD from the environment).
# A failed dump never replaces a good one: it's written to a temp file first.
set -u
# Dumps hold password hashes and emails: readable by the owner (root) only.
umask 077

while true; do
  name="/backups/anitether-$(date +%Y-%m-%d_%H%M).dump"
  if pg_dump -Fc -h db -d "$POSTGRES_DB" -f "$name.tmp"; then
    mv "$name.tmp" "$name"
    echo "backup ok: $name"
    find /backups -name 'anitether-*.dump' -mtime +"${BACKUP_KEEP_DAYS:-14}" -delete
  else
    rm -f "$name.tmp"
    echo "backup FAILED at $(date)" >&2
  fi
  sleep "${BACKUP_INTERVAL_SECONDS:-86400}"
done
