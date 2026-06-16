#!/bin/sh
set -eu

if [ ! -f /data/users.yml ]; then
  set -- docker run --rm amir20/dozzle:latest generate "${DOZZLE_ADMIN_USERNAME:-admin}" --password "${DOZZLE_ADMIN_PASSWORD}" --email "${DOZZLE_ADMIN_EMAIL:-me@email.net}" --name "${DOZZLE_ADMIN_NAME:-Admin}"

  if [ -n "${DOZZLE_ADMIN_FILTER:-}" ]; then
    set -- "$@" --user-filter "${DOZZLE_ADMIN_FILTER}"
  fi

  if [ -n "${DOZZLE_ADMIN_ROLES:-}" ]; then
    set -- "$@" --user-roles "${DOZZLE_ADMIN_ROLES}"
  fi

  "$@" > /data/users.yml
fi
