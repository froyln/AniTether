#!/bin/sh
# Runs once, on the first start of an empty data directory (docker-entrypoint-initdb.d),
# as the image's superuser ($POSTGRES_USER). Nothing long-running uses that superuser
# afterwards; it creates three least-privilege roles instead:
#   anitether_owner   owns the database and schema; the one-shot `migrate` service runs as it
#   anitether_app     what the API connects as: rows only (SELECT/INSERT/UPDATE/DELETE), no DDL
#   anitether_backup  read-only (pg_read_all_data), for the `backup` service's pg_dump
set -eu

psql -v ON_ERROR_STOP=1 \
  -v owner_password="$OWNER_DB_PASSWORD" \
  -v app_password="$APP_DB_PASSWORD" \
  -v backup_password="$BACKUP_DB_PASSWORD" \
  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
CREATE ROLE anitether_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE anitether_app LOGIN PASSWORD :'app_password';
CREATE ROLE anitether_backup LOGIN PASSWORD :'backup_password' IN ROLE pg_read_all_data;

ALTER DATABASE :"DBNAME" OWNER TO anitether_owner;
ALTER SCHEMA public OWNER TO anitether_owner;
GRANT USAGE ON SCHEMA public TO anitether_app;

-- Not "IN SCHEMA public": schema-scoped defaults are dropped with the schema, and
-- scripts/restore.sh recreates it.
ALTER DEFAULT PRIVILEGES FOR ROLE anitether_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO anitether_app;
ALTER DEFAULT PRIVILEGES FOR ROLE anitether_owner GRANT USAGE, SELECT ON SEQUENCES TO anitether_app;
SQL
