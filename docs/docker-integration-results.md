# Docker integration evidence

Checked on 2026-10-09, starting from develop `c1e035f`.
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
performed there. Two-host acceptance is therefore **incomplete**.

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
