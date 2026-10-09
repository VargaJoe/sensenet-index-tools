# Docker runtime

The .NET 8 image contains the CLI and Blazor server. It uses pinned SDK/runtime
image digests and NuGet lock files. The supported source format is SenseNet
Lucene 2.9 (`SenseNet.Search.Lucene29` 7.5.1). Newer Lucene formats require a
compatible reader; this image does not convert them or create a new repository.

## Private configuration

1. Copy `docker/example.env` to `.private/operator.env` and
   `docker/runtime.example.json` to `.private/runtime/runtime.json`.
2. Write the SQL connection string to `.private/runtime/sql-connection-string`
   and an API key to `.private/runtime/api-key`. Alternatively configure a
   bearer-token file and set `ApiKeyFile` to null. Store a random web access
   token of at least 32 characters in `.private/runtime/web-token`.
3. Set the host's source directory, existing Docker network and private config
   directory in the env file. All paths in `runtime.json` refer to paths inside
   the container. Use a different state volume for each independent deployment.
4. On Linux, grant the container UID/GID read access to source files and secrets.
   The default UID/GID is `1654:1654`. A root-owned secret directory with group
   1654, mode 0750, and files mode 0640 keeps credentials private. Preserve
   ownership of the repository source. Provision permissions on a storage
   snapshot if the source is otherwise inaccessible.

The `.private/`, `.env*`, `secrets/` and local state paths are Git-ignored.
The Docker build context permits only source, static assets and build inputs;
it excludes runtime data, credentials, indexes, reports and archived binaries.
Never put credentials in Dockerfile arguments, the committed env example,
CLI arguments, URLs, build logs or a PR. Disable shell tracing around secret
creation. Connection strings in saved web configurations are protected with
ASP.NET Data Protection. Linux key files require filesystem protection and
must be backed up together with the configuration data.

## Build and start

```sh
docker build -t index-tools:local .
docker compose --env-file .private/operator.env config --quiet
docker compose --env-file .private/operator.env up -d --no-build --wait web
docker compose --env-file .private/operator.env run --rm cli cli --help
```

Set `INDEXTOOLS_IMAGE` to the same immutable image reference on both hosts.
An image built once can also be moved with `docker save` / `docker load`.
The Compose definition stays identical; only private configuration differs.
No socket mount, privileged mode, repository-container restart or repository
configuration change is required. The source bind mount and application root
filesystem are read-only. The application drops Linux capabilities and runs
with `no-new-privileges`.

Default web binding is host loopback port 5187 to container port 8080. Open
`http://localhost:5187/login` through a local connection or SSH tunnel and enter
the web token. All operation pages, static assets and Blazor connections require
the protected session cookie. `/healthz` exposes only process readiness text.
Docker health checks use the bundled CLI probe and do not query SQL or the API.
The source read check and state-directory write probes run at web startup.

For a TLS reverse proxy, configure the specific bind address, allowed hostnames
and **exact proxy IP addresses** in `TRUSTED_PROXIES` (semicolon-separated).
Forwarded client/protocol headers from other peers are ignored. Configure the
proxy for WebSockets and pass the original Host and X-Forwarded-Proto headers.
Terminate TLS at the proxy; Compose disables application HTTPS redirection.
Cookies are Secure when a trusted proxy supplies the HTTPS scheme. Do not expose
plain HTTP on a public interface. Local non-container execution retains the
loopback access restriction unless explicitly configured; remote mode always
requires the token file.

## Configuration reference

`INDEXTOOLS_CONFIG_FILE` selects a private JSON file. Environment variables below
override its values. Existing CLI options override the configured defaults.

| JSON property | Environment variable | Purpose |
| --- | --- | --- |
| SourcePath | INDEXTOOLS_SOURCE_PATH | Index directory or parent of dated directories |
| SnapshotDirectory | INDEXTOOLS_SNAPSHOT_DIRECTORY | Verified isolated commit copies |
| OutputDirectory | INDEXTOOLS_OUTPUT_DIRECTORY | Reports and integration summaries |
| BackupDirectory | INDEXTOOLS_BACKUP_DIRECTORY | Backups before copy-only mutations |
| DataDirectory | INDEXTOOLS_DATA_DIRECTORY | Saved web configurations and report catalog |
| KeyDirectory | INDEXTOOLS_KEY_DIRECTORY | Persistent Data Protection key ring |
| RepositoryUrl | INDEXTOOLS_REPOSITORY_URL | HTTP(S) repository base URL |
| SqlConnectionStringFile | INDEXTOOLS_SQL_CONNECTION_STRING_FILE | Read-only SQL secret file |
| ApiKeyFile | INDEXTOOLS_API_KEY_FILE | Read-only API key file |
| BearerTokenFile | INDEXTOOLS_BEARER_TOKEN_FILE | Optional bearer-token file |

Compose parameters: `INDEXTOOLS_IMAGE`, `INDEX_SOURCE`,
`PRIVATE_CONFIG_DIRECTORY`, `DOCKER_NETWORK`, `STATE_VOLUME`, `RUN_UID`,
`RUN_GID`, `WEB_BIND_ADDRESS`, `WEB_PORT`, `WEB_ALLOWED_HOSTS`, `TRUSTED_PROXIES`.
Container state is persisted under `/state`: `data`, `reports`, `keys`, `copies`
and `backups`. The configuration/secret directory is an external read-only bind.
Back up the state volume and secret directory through the operator's normal
backup process. `docker compose down` preserves the state volume; `down -v`
deletes it.

## Index snapshots and operations

`SourcePath` accepts an index or its parent. For a parent, the newest nonempty
14-digit `yyyyMMddHHmmss` directory is selected. A corrupt selected directory
fails validation rather than silently selecting an older index.

```sh
docker compose --env-file .private/operator.env run --rm cli cli snapshot
docker compose --env-file .private/operator.env run --rm cli cli lastactivityid-get
docker compose --env-file .private/operator.env run --rm cli cli validate \
  --path /state/copies/<snapshot-directory> --detailed --sample-size 0 \
  --output /state/reports/validation.md
docker compose --env-file .private/operator.env run --rm cli cli compare \
  --index-path /state/copies/<snapshot-directory> \
  --repository-path /Root/Content/SelectedTestItem --recursive false \
  --output /state/reports/comparison.md
```

`snapshot` opens a read-only reader, enumerates **one commit's exact file set**,
copies immutable files into a unique partial directory, verifies source/copy
SHA-256 values and reopens the copy to check its commit and document counts.
It omits `write.lock`, uncommitted files and arbitrary source metadata. A
manifest stores hashes and counts. Live writer deletion races are retried up to
three times, then fail. A plain directory `cp` cannot provide this guarantee.
Use a filesystem/storage snapshot if the source churn prevents a successful
commit copy. This mechanism does not pause or modify the source writer.

All setters and cleanup operations must use an isolated snapshot path, with
`--offline` and the existing backup/dry-run controls. The configured source is
explicitly protected in addition to the read-only mount. Select an isolated copy
from `/state/copies` in the web UI before any write. LastActivityId reads use
commit metadata without opening an indexing engine or writer.

## Real repository acceptance

First confirm the repository container's network/alias, index bind or volume,
source format, SQL database and API authentication using private infrastructure
inventory and read-only `docker inspect` queries. Avoid dumping environment
values or full Compose expansions to the terminal. Mount the actual source
directory, not an assumed path. With a named source volume, use a private Compose
override declaring that external volume and mounting it read-only at the same
container path.

```sh
# Read-only snapshot validation, scoped SQL/index parity and activity reads:
docker compose --env-file .private/operator.env run --rm cli cli verify-repository \
  --repository-path /Root/Content/SelectedTestItem

# Explicit write only for the designated test content, never its subtree:
docker compose --env-file .private/operator.env run --rm cli cli verify-repository \
  --repository-path /Root/Content/SelectedTestItem --rebuild
```

The opt-in rebuild verifies REST/SQL content identity, sends `IndexOnly` with
`Recursive=false`, finds the selected node's new SQL `Rebuild` activity, waits
for a newer index commit that has processed that activity without a gap, captures
a fresh snapshot, checks SQL/index parity and reads the REST content again.
HTTP acceptance without the activity/readback checks fails the command. JSON
summaries contain counts and activity IDs; source names and secrets are excluded.
Compare the repository containers' IDs, StartedAt values and restart counts
before/after testing. Store actual host routing and raw reports privately.

See [integration results](docker-integration-results.md) for the anonymized
per-host evidence and outstanding checks.

## Regression and Docker smoke checks

```sh
dotnet restore sensenet-index-tools.sln --locked-mode
dotnet test test/IndexTools.Tests/IndexTools.Tests.csproj -c Release
INDEXTOOLS_SMOKE_EXPORT="$PWD/.private/smoke" dotnet test \
  test/IndexTools.Tests/IndexTools.Tests.csproj -c Release --no-build \
  --filter FullyQualifiedName~NativeActivityMetadataReads
docker build -t index-tools:smoke .
sh docker/smoke.sh
```

Use a fresh export directory. The smoke script uses a synthetic source fixture,
its own Docker network/state volume and loopback port; it checks web login,
health, CLI validation/activity reads, snapshots, isolated writes, rejection of
source writes and cookie/key persistence after web restart. Cleanup only removes
that smoke stack's network and volume. CI runs Windows/Linux regressions, Linux
publishes, the container build and the smoke script. Real SQL/API acceptance
uses private credentials and remains an operator integration check.
