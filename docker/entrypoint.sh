#!/bin/sh
set -eu
umask 077
case "${1:-web}" in
  web) shift; exec dotnet /app/web/WebApp.dll "$@" ;;
  cli) shift; exec dotnet /app/cli/sn-index-maintenance-suite.dll "$@" ;;
  *) exec dotnet /app/cli/sn-index-maintenance-suite.dll "$@" ;;
esac
