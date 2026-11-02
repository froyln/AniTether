#!/bin/sh
# Replace the database contents with a backup.
# Usage: scripts/restore.sh backups/anitether-YYYY-MM-DD_HHMM.dump
#
# The schema is dropped and recreated first, so restoring an older dump onto a newer
# schema (rolling back an update) leaves no newer tables behind. Everything — drop,
# recreate, restore — runs in ONE transaction: any error rolls back to the database as it
# was. The API is stopped meanwhile and started again at the end either way.
set -eu

dump="${1:?usage: scripts/restore.sh <file.dump>}"
compose="${COMPOSE:-docker compose -f docker-compose.prod.yml}"

$compose stop api
trap '$compose start api' EXIT

$compose exec -T db sh -c '
  set -eu
  cat > /tmp/restore.dump
  # Convert first: if the dump is unreadable this fails here, before anything is dropped.
  pg_restore --no-owner --no-acl -f /tmp/restore.sql /tmp/restore.dump
  {
    echo "DROP SCHEMA public CASCADE;"
    echo "CREATE SCHEMA public AUTHORIZATION anitether_owner;"
    echo "GRANT USAGE ON SCHEMA public TO anitether_app;"
    # Objects are created as the owner, so default privileges give the app its rights.
    echo "SET ROLE anitether_owner;"
    grep -v "^CREATE SCHEMA public;$" /tmp/restore.sql
  } | psql -v ON_ERROR_STOP=1 --single-transaction -q -U "$POSTGRES_USER" -d "$POSTGRES_DB"
  rm -f /tmp/restore.dump /tmp/restore.sql
' < "$dump"
echo "restored $dump"
