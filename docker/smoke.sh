#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
test -d .private/smoke/source || { echo "Export the synthetic fixture with INDEXTOOLS_SMOKE_EXPORT first." >&2; exit 1; }
umask 077
mkdir -p .private/smoke/config
printf '{}\n' > .private/smoke/config/runtime.json
openssl rand -hex 32 | tr -d '\n' > .private/smoke/config/web-token
# Keep the invoking user as owner and grant only the application group read access.
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) ;; # Docker Desktop provides the bind mount permissions.
  *)
    if [ "$(id -u)" = 0 ]; then chgrp -R 1654 .private/smoke/config; else sudo chgrp -R 1654 .private/smoke/config; fi
    chmod 750 .private/smoke/config
    chmod 640 .private/smoke/config/* ;;
esac
export INDEXTOOLS_IMAGE="${INDEXTOOLS_IMAGE:-index-tools:smoke}"
export INDEX_SOURCE="$PWD/.private/smoke/source"
export PRIVATE_CONFIG_DIRECTORY="$PWD/.private/smoke/config"
export DOCKER_NETWORK="index-tools-smoke-$$"
export STATE_VOLUME="index-tools-smoke-$$"
export WEB_PORT="${WEB_PORT:-15187}"
project="index-tools-smoke-$$"
dc() { docker compose -p "$project" "$@"; }
cleanup() {
  dc down --volumes >/dev/null 2>&1 || true
  docker network rm "$DOCKER_NETWORK" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
docker network create "$DOCKER_NETWORK" >/dev/null
dc config --quiet
dc up -d --no-build --wait --wait-timeout 90 web
test "$(curl --fail --silent "http://127.0.0.1:$WEB_PORT/healthz")" = healthy
test "$(curl --silent --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:$WEB_PORT/settings")" = 302
curl --fail --silent --cookie-jar .private/smoke/cookies \
  --data-urlencode token@.private/smoke/config/web-token "http://127.0.0.1:$WEB_PORT/login" >/dev/null
test "$(curl --silent --cookie .private/smoke/cookies --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:$WEB_PORT/settings")" = 200
dc run --rm --no-deps cli cli --help >/dev/null
dc run --rm --no-deps cli cli lastactivityid-get > .private/smoke/activity-source.log
grep -q 'Last activity ID: 42' .private/smoke/activity-source.log
dc run --rm --no-deps cli cli validate --detailed --sample-size 0 --output /state/reports/validation.md
dc run --rm --no-deps cli cli snapshot > .private/smoke/snapshot.log
copy=$(tail -1 .private/smoke/snapshot.log)
dc run --rm --no-deps cli cli lastactivityid-set --path "$copy" --id 43 --offline
dc run --rm --no-deps cli cli lastactivityid-get --path "$copy" > .private/smoke/activity-copy.log
grep -q 'Last activity ID: 43' .private/smoke/activity-copy.log
dc run --rm --no-deps cli cli lastactivityid-get > .private/smoke/activity-source.log
grep -q 'Last activity ID: 42' .private/smoke/activity-source.log
if dc run --rm --no-deps cli cli lastactivityid-set --id 44 --offline >.private/smoke/denied-write.log 2>&1; then
  echo 'Source write unexpectedly succeeded' >&2; exit 1
fi
id=$(dc ps -q web)
test "$(docker inspect --format '{{.Config.User}} {{.HostConfig.Privileged}} {{.HostConfig.ReadonlyRootfs}}' "$id")" = '1654:1654 false true'
dc restart web >/dev/null
dc up -d --no-build --wait --wait-timeout 90 web
test "$(curl --silent --cookie .private/smoke/cookies --output /dev/null --write-out '%{http_code}' "http://127.0.0.1:$WEB_PORT/settings")" = 200
dc logs > .private/smoke/container.log
if grep -q -F -f .private/smoke/config/web-token .private/smoke/container.log; then
  echo 'Access token appeared in logs' >&2; exit 1
fi
echo 'Docker CLI/web, read-only source, isolated writes, persistent key ring and auth checks passed.'
