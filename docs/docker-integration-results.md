# Docker integration evidence

The latest local operator acceptance is recorded in the
[2026-10-16 section](#local-operator-workflow-2026-10-16). Earlier remote results
below describe the application version tested on their stated dates; the new
local workflow has not been deployed to those hosts.

Initial checks on 2026-10-09, starting from develop `c1e035f`.
The current milestone is single-host Docker operation against the existing
repository. Additional hosts/legs are a separate feature; their unverified
status below does not block this milestone.
Actual host identities, routing, source paths and operator configuration are
recorded in private project/infrastructure memory. Host A and Host B below are
stable labels for those targets; no credential or customer routing is needed
to reproduce the application build.

| Check | Local Docker / regression fixture | Host A | Host B |
| --- | --- | --- | --- |
| .NET Release regressions | 43 passed | Not run | Same built application |
| Pinned Docker build and CLI/web publish | Passed | Not transferred | Same exported image loaded |
| CLI/web startup and health | Passed | Blocked by SSH identity check | Passed |
| Token login and persistent cookie/key ring | Passed | Not checked | Passed on isolated smoke stack |
| Non-root, read-only root/source, isolated writes | Passed | Not checked | Passed on isolated smoke stack |
| Live source storage/network/format | Synthetic Lucene 2.9 | Not checked | Existing network and bind verified; dated Lucene 2.9 index, approximately 1.8 GiB |
| Live snapshot validation / LastActivityId | Fixture activity 42 | Not checked | Passed; 0 validation errors |
| SQL/index comparison | Regression fixtures | Not checked | Selected item: 1 match; additional selected subtree: 37 matches, 0 differences |
| Targeted REST rebuild and readback | Request regressions | Not checked | HTTP 204; selected-node Rebuild activity 20720 processed by source index; fresh SQL/index and REST identity checks passed |
| Existing repository runtime unchanged | Separate test stack | No connection or changes | Container ID, start time and restart count unchanged |

Host A's SSH server presented a different host key from the saved identity.
Its integration remains pending until the infrastructure owner confirms the
new identity. No identity bypass, repository deployment or service restart was
performed there. Multi-host acceptance is deferred to its own feature.

## Single-host recheck (2026-10-14)

The existing tools web remained healthy without restarting either application
or repository. A loopback SSH tunnel exposed the web to the operator's local
browser; health returned HTTP 200 and protected pages redirected to login.
Fresh read-only container acceptance passed with index and SQL LastActivityId
20944, one selected item, zero mismatches and zero validation errors (the same
manifest warning). No REST write was needed for this recheck.

The current milestone covers the verified single host. Multi-host operation
and owner-facing browser acceptance are tracked separately from these backend
checks. Web access continues to require its private token.

## Web interaction recheck (2026-10-15)

An operator found that the configured index path left the initial activity-read
button disabled. The page initialized its path but did not initialize the
validation state. It now validates the configured path before its first render;
the SQL connection field is masked by default and has a show/hide eye button.
Two render regressions cover configured and
absent sources, bringing the suite to 45 passing tests.

An authenticated headless browser reproduced the disabled controls before the
fix. On the updated tools web, both read and compare were enabled without editing
the path. Clicking the index read displayed 20968; clicking SQL/index comparison
displayed 20968 on both sides and confirmed synchronization. The existing
repository container ID, start time and restart count were unchanged.

Host B used the committed Compose definition with private network, source,
SQL/API secret-file and loopback-port parameters. The application requires
neither a Docker socket nor elevated container privileges. Administrative SSH
was used only to inspect topology and provision the separate tools stack.

The live source remained read-only to the tools. Snapshot creation copied a
verified commit while the repository writer was active. Initial snapshot and
SQL activity values both read 20716; the later rebuild check started at 20719
and confirmed activity 20720 with no processing gap. These values are evidence
from a moving test repository, not expected constants for later runs.

Validation used detailed sampling (10 documents) and all normal structural
checks. One warning classified the generated snapshot manifest as an additional
file; it was not a corrupt index file. The SQL comparisons cover the selected
item and a 37-item subtree, not the entire repository. The REST request was
non-recursive `IndexOnly` for the selected test content. Write tests changed
only synthetic copies (activity 42 to 43) and confirmed that the source stayed
at 42. Cleanup guards were regression-tested on isolated fixtures.

Private JSON integration summaries and comparison reports persist in the state
volume. Raw host inspection and logs stay outside Git. Secret-value scans cover
application logs and generated integration/comparison reports.

CI performs Windows/Linux regressions, CLI/web publish, Docker build and the
isolated container smoke test. It does not contain live repository credentials;
real SQL/API acceptance must be run by an operator as described in
[Docker runtime](docker.md).

## Local operator workflow (2026-10-16)

The workspace-started lab uses a new disposable SQL Server database, an unmodified
pinned SenseNet SQL repository image and the current tools image in separate
containers. All three use an isolated local Docker network and durable volumes.
The repository and tools bind only to loopback; SQL has no published host port.
No existing application, remote repository or SenseNet source was changed.

| Check | Actual local result |
| --- | --- |
| Pinned test/runtime image builds | Passed |
| Regular regressions | 64 passed, no failures or skips |
| Native SQL/OData/Lucene workflow | Passed as UID 1654 with read-only repository storage |
| Native online backup | Passed; completed source selected only after native status and Lucene verification; recovery and failure paths also covered by regressions |
| Live LastActivityId and index validation | Passed from the browser; 0 validation errors and warnings |
| SQL/subtree comparison | Fresh backup: all 3 fixture contents matched |
| Report-selected OData repair | Selected non-recursive IndexOnly rebuild processed; fresh SQL/index parity passed; SQL content timestamps unchanged |
| Orphan repair | Exactly 1 injected orphan removed from the working copy; a real version outside the subtree preserved |
| Source preservation | Original backup file hashes unchanged; source writes and repeated/stale cleanup rejected |
| Offline source copy | Active writer rejected; copy passed after stopping only the disposable local repository; repository restarted and API rechecked |
| Browser acceptance | Backup, source selection, copy, activity, validation, comparison, selected REST repair and orphan cleanup passed |
| Legacy activity page safety | Live-source set/initialize controls remained disabled |
| Generic Compose smoke | Read-only source with a writer-lock file validated; isolated writes, health/auth and persistent session/key ring passed |
| Container permissions | Tools UID/GID 1654, read-only root/source, no privileged mode or Docker socket; tool SQL login has db_datareader only |
| Secret handling | Actual private credential values absent from the checked local/live service logs and changed/new public files |

Run `python docker/local/check.py --browser` to repeat the native and browser
acceptance, with the documented Playwright dependency configured. The successful
run built both images and exercised the stopped-writer path before returning
the repository to service. Browser defects were injected only into a working
copy. Structured acceptance evidence, detailed logs and the operator screenshot
remain under Git-ignored `.private/local/`.

The read-only browser test exposed and fixed a validation bug: the old lock probe
attempted write access to an existing `write.lock`. Validation now only inspects
the file's presence, without acquiring, deleting or modifying writer locks.
Offline copying separately checks for an active writer using read-only access.

Pre-merge review added two regression-backed protections: old backup target names
cannot be reused while present in native backup history, and authenticated CLI/web
repository clients refuse HTTP redirects instead of forwarding API credentials.
The redirect regression uses a real HTTP endpoint and checks that the redirect
target receives no request.

The new manual CI workflow repeats native lab acceptance. Existing PR CI includes
regressions and Docker smoke. This section records local acceptance; remote CI
results are recorded on the PR. This operator workflow has not been deployed
to the earlier remote test hosts.
Automatic installation of a repaired copy, retention and multiple repository
profiles remain outside this workflow. See [local operation](local-operations.md)
for startup, configuration, CLI parity and operator steps.
