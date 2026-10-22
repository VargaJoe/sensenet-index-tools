# Local operator workflow

## Two-repository Docker lab

For multi-repository UI and container acceptance, run from the repository root:

```sh
python docker/local/multi_repository.py
```

Python 3 and Docker Desktop with Linux containers are required. This starts two
unmodified SenseNet containers, separate `IndexToolsRepoA` / `IndexToolsRepoB`
databases and separate index/backup volumes on one lab SQL Server. It builds the
current Index Tools source into its own container. Existing Docker projects are
unaffected. The first installation and build can take several minutes.

- Tools: `http://127.0.0.1:5192/operations`
- Repo A: `http://127.0.0.1:5193/`
- Repo B: `http://127.0.0.1:5194/`

The script saves **Repo A** and **Repo B** profiles with the correct index path,
read-only SQL connection, API key, default subtree and both backup roots. The
application's own `ConfigurationService` and persistent DataProtection keys protect
the stored credentials. Only the active choice belongs to the browser tab. The
setup helper is a separate, network-disabled SDK container; it adds no endpoint or
startup import behavior to the application.

All concrete Compose configuration, random credentials, assembly copies and logs
are generated under ignored `.private/multi-repository-lab/`. Keep that directory
together with the Docker volumes. Subsequent runs preserve the databases, profile
IDs, keys, backups, reports and working copies. The script refreshes only the two
profiles tagged `local-multirepo-lab`; it refuses to replace an unrelated profile
with the same name. The tools stop briefly while profiles are updated.

The tool sees `/source/repo-a` and `/source/repo-b` through read-only mounts. Each
repository writes its own `/app/App_Data/IndexBackups`; the tool sees those as
`/source/repo-a/IndexBackups` and `/source/repo-b/IndexBackups`. Copies, safety
snapshots, reports and keys stay on the writable `tool-state` volume. The tool runs
as UID1654 with a read-only root filesystem and without Docker socket access. All
published ports bind to loopback; SQL has no host port.

```sh
python docker/local/multi_repository.py --no-build   # reuse the last tool image
python docker/local/multi_repository.py --no-browser
python docker/local/multi_repository.py --stop       # preserve all data
python docker/local/multi_repository.py --no-browser --check  # regressions + both native workflows
```

To change ports, edit the generated private `ports.json` and rerun. To reopen an
authenticated browser without rebuilding, use `--no-build`. The browser handoff
does not print tokens or put them in URLs. Do not publish this disposable lab.

Optional browser acceptance requires Node, Playwright and Chromium (or set
`INDEXTOOLS_BROWSER_CHANNEL=msedge`). Run:

```sh
node docker/local/multi-repository-check.cjs
```

`INDEXTOOLS_PLAYWRIGHT_MODULE` may point to an installed Playwright module. The
check creates real native backups and working copies for both repositories, reads
SQL/index activity numbers, checks subtree parity, changes only working-copy
LastActivityId metadata, validates both indexes, downloads HTML/Markdown and checks
independent browser contexts. Evidence stays in the private lab directory.

The optional `--check` builds the current test image and runs the regular regression
suite plus native SQL/Lucene/backup/repair acceptance for each isolated repository.
The manually dispatched **Local repository integration** GitHub workflow uses this
same command and uploads only the credential-free acceptance summary.

## Earlier single-repository lab

The workspace includes an isolated SQL Server, an unmodified SenseNet repository
image, and the CLI/web tools image. This is a local test environment with its own
database, Docker network and persistent volumes. Existing applications and remote
repositories are not used. Docker Desktop (Linux containers) and Python 3 are
required; the first installation can take several minutes.

The operator UI uses server-side Blazor. The unused WebAssembly host registration
and its server package were removed; no WebAssembly client runtime is needed to
operate the tool.

From the repository root:

```sh
python docker/local/start.py
```

This creates random credentials in Git-ignored `.private/local`, starts the
repository, builds and starts the tools, and opens an authenticated browser through
a one-use loopback cookie handoff. No credential is placed in a URL or printed.
SQL administrator files are mounted only into the SQL setup services; the tools
receive a separate read-only SQL login and the local repository's API credential.
The host secret directory is restricted to its owner. Do not publish this lab.

* Repository: `http://127.0.0.1:5186/`
* Operator UI: `http://127.0.0.1:5189/operations`
* `--no-build` reuses the existing tools image.
* `--no-browser` starts without the login/browser handoff.

Set `LAB_REPOSITORY_PORT` and `LAB_TOOLS_PORT` in `.private/local/compose.env`
(or the shell environment) if the default loopback ports are occupied.

The local image digests are pinned in `docker/local/compose.yaml`. SenseNet's
source is not modified. The repository installs a fresh `IndexToolsLab` database;
this is not a restore of customer data. The SQL login used by the tools has
`db_datareader` only. OData backup/rebuild uses the repository administrator key
generated by SenseNet for this isolated database.

To stop the lab and retain all data:

```sh
docker compose --env-file .private/local/compose.env -f docker/local/compose.yaml --profile tools down
```

Do not add `-v` unless intentionally discarding the entire local test database,
repository data, reports, working copies and key ring. Private configuration must
be retained alongside existing volumes; replacing passwords does not reset an
existing SQL database. The initializer preserves existing credentials.

## Operator steps

The repository selector and index source at the top of every page belong to the
current browser tab. Navigation and reload preserve that tab's selection; another
browser or tab keeps its own selection. Session storage contains a server-protected
value, not plaintext credentials. Saved profile
definitions in Settings are shared, but selecting or editing manual connection
values affects only the operator's session. A profile switch replaces all connection
fields and clears previous results and confirmations.

The common panel supports the default runtime repository, saved profiles, and
manual session values. Use **Manage** to edit saved profiles and **Back to operation**
to return. API credentials and backup roots belong to the profile; an incomplete
profile does not inherit credentials from another repository.

LastActivityId reads and compares activity numbers and supports an operator-entered
number for metadata corrections, including debugging. These changes use a managed
working copy, create a safety snapshot, and read the number back. They do not send
rebuild requests. Content reindexing remains a separate feature. Gap analysis is
outside this UI consolidation.

Validation and subtree reports offer HTML and Markdown downloads from the same
diagnostic result. HTML previews are sandboxed frames, so report styles cannot
change the application layout. Saved reports retain both exports and source
metadata; Dashboard shows the actual recent reports.

1. **Prepare the source.** For a running repository, select **Create repository
   backup**. The API writes an empty, unique run folder inside the configured
   repository target root. The tool polls `QueryIndexBackup`, matches the requested
   target, waits for completion and checks its Lucene index before selecting it.
   A folder name already present in the repository's backup history is rejected,
   even if its old files were manually removed, so an old completion cannot be
   mistaken for the new request.
   A failed or unrelated backup never replaces the selected source. **Discover
   completed backups** can recover a completed request after interrupted waiting,
   using the repository's available backup history.
2. **Inspect.** Read LastActivityId, validate, and compare a selected subtree.
   Reports include source identity, capture time and scope. SQL remains live: a
   historical backup can differ from changes committed after capture. Live index
   reads are supported, but use a completed backup for stable, lengthy diagnostics.
3. **Preview repository repairs.** A saved comparison exposes rebuild candidates
   that exist in SQL and differ from the index. Select individual contents and
   confirm. Each OData call is non-recursive `IndexOnly`; REST identity is checked
   against the reported SQL node first. Acceptance is verified through that node's
   new Rebuild activity, processed index activity/gaps and fresh SQL/index parity.
   HTTP acceptance alone is not reported as completed repair.
4. **Repair index files.** Create a separate working copy from a completed backup
   and compare that copy. Orphan preview checks node/version existence in the
   entire SQL database, including outside the selected subtree. Confirmed cleanup
   deletes exact previewed Lucene documents only; a changed working copy requires
   a fresh comparison. Original backup and live index remain read-only.
5. **Verify and retain evidence.** Cleanup produces a fresh comparison and action
   report. Repository repairs retain receipts, including interrupted verification.
   Create a new repository backup to inspect the live result in a stable source.

For an offline repository, copying the live source requires explicit confirmation
that **all index writers have stopped** and refuses an active writer lock. The
tool does not stop containers or remove writer locks. Both paths produce managed
working copies in the configured copies directory.

LastActivityId set/initialize are available under the working-copy controls.
They edit indexing metadata, not content documents. Existing values cannot be
overwritten using initialize. The content table supports database/index/difference
views and full structured download; reports reopen the original repair preview.
Manual REST rebuild and line-based target files remain available at `/rebuild-index`.

Cleanup of a working copy does not install it into the repository. Automated
restore/cutover, full index creation, backup retention policies, scheduling and
multiple repository profiles are outside this workflow. Any later installation
of a corrected index needs its own offline restore and activity catch-up process.
The corrected directory remains in the tools' persistent state volume. A host
operator can retrieve a specific copy without granting Docker access to the app:

```sh
docker compose --env-file .private/local/compose.env -f docker/local/compose.yaml cp tools:/state/copies/<copy-directory> .private/repaired-index
```

## CLI parity

The `workspace` command uses the same preparation, comparison, candidate and
repair implementation as the UI. For the local stack:

```sh
docker compose --env-file .private/local/compose.env -f docker/local/compose.yaml exec tools dotnet /app/cli/sn-index-maintenance-suite.dll workspace sources
docker compose --env-file .private/local/compose.env -f docker/local/compose.yaml exec tools dotnet /app/cli/sn-index-maintenance-suite.dll workspace backup
```

Use the returned IDs with `workspace copy --source <source-id>` and
`workspace compare --source <source-id> --repository-path /Root/Content`.
`workspace cleanup --report <report-id>` and `workspace repair --report <report-id>`
are previews. Mutation requires `--confirm`; repository repairs additionally need
explicit `--node-id` selections. `workspace recover` discovers completed API backups.
Existing standalone CLI commands continue to work with their current options.

## Repeatable verification

```sh
python docker/local/check.py
```

This builds the test/runtime images, runs the unit regressions, then verifies
native backup, SQL parity, report-driven REST rebuild with activity/readback,
and exact orphan deletion in a working copy. It injects defects only into that
copy and verifies that a real version outside the subtree is preserved, source
backup hashes are unchanged and content SQL timestamps are unchanged by IndexOnly
rebuild. The fixture refuses any configuration other than this disposable lab.
The native integration runner uses UID1654 and a read-only repository mount.

For browser acceptance, install Playwright in a private dependency directory,
set `INDEXTOOLS_PLAYWRIGHT_MODULE` to that package's absolute path, install its
Chromium browser, then use `python docker/local/check.py --browser`. Optionally
set `INDEXTOOLS_BROWSER_CHANNEL=chrome` for an installed Chrome. The browser test
clicks backup, copy, validation, comparison, selected REST repair and orphan cleanup.
`--no-build` reuses previously built matching images. The separate manual
**Local repository integration** CI workflow runs the native acceptance; normal
PR CI still runs Windows/Linux regressions and Docker CLI/web smoke, including
validation of a read-only source containing a writer-lock file.

## Connecting another repository

Use [the generic runtime configuration](docker.md) with private URL, SQL/API secret
files and index mounts. The supported index is SenseNet Lucene 2.9; SQL comparison
currently uses SQL Server. Memory-only repositories have no equivalent on-disk
SQL/Lucene source. Deployment on a different host does not require code changes,
Docker socket access or a privileged tool container.

Repository target and tool source are different path namespaces. For example:

| Purpose | Container path |
| --- | --- |
| Repository writes the backup | `/app/App_Data/IndexBackups/<run-id>` |
| Tools read the same host storage | `/source/backups/<run-id>` |
| Tools repair an isolated copy | `/state/copies/index-<run-id>` |

Mount the repository backup storage read-only into the tool using the optional
`docker/compose.backups.yaml` overlay and `INDEX_BACKUP_SOURCE`. Configure
`RepositoryBackupRoot` and `BackupSourceRoot` to match. The repository needs write
permission; the tools' UID/GID needs read/traverse permission. Backup status and
creation require repository API permissions for those actions. An API 403 must be
resolved in the target's authentication/authorization configuration, not bypassed
by changing SenseNet code or exposing Docker administration to the tool.

Sources and comparison records are bound to repository URL and SQL server/database
identity. Reports from another configuration cannot submit repairs. Credential
values are excluded from these records and their binding hash.
Authenticated repository requests do not follow HTTP redirects. Configure the
final repository URL, including its scheme and proxy base path, directly.
