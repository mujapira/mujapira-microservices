#!/bin/sh
set -eu

# variáveis com fallback
DB_NAME=${LOG_DB_NAME:-LogServiceDb}
USER=${LOG_DB_USER:-logservice}
PASS=${LOG_DB_PASSWORD:-senhaLogServiceSegura!}

# cria o usuário limitado se não existir
mongosh --username "$MONGO_ROOT_USERNAME" --password "$MONGO_ROOT_PASSWORD" --authenticationDatabase admin <<EOF
use $DB_NAME
if (db.getUser("$USER") == null) {
  db.createUser({
    user: "$USER",
    pwd: "$PASS",
    roles: [{ role: "readWrite", db: "$DB_NAME" }]
  });
} else {
  print("User '$USER' already exists in $DB_NAME");
}
EOF
