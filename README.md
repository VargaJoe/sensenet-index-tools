# SenseNet Index Maintenance Suite

.NET 8 CLI and Blazor Server tools for examining SenseNet Lucene 2.9 indexes, comparing their documents with SQL Server data, and performing explicit repairs on offline copies.

[Docker runtime and private configuration](docs/docker.md) describes the shared
CLI/web image, read-only source mounts, commit snapshots, persistent state and
scoped real repository acceptance. See the [integration results](docs/docker-integration-results.md)
for per-host verification.

## Start

```powershell
dotnet restore sensenet-index-tools.sln
dotnet build sensenet-index-tools.sln
dotnet test test/IndexTools.Tests/IndexTools.Tests.csproj
dotnet run --project src/MainProgram -- --help
dotnet run --project src/WebApp/WebApp -- --urls http://localhost:5168
```

An old checkout with stale NuGet assets may need `dotnet restore sensenet-index-tools.sln -p:RestoreFallbackFolders= --source https://api.nuget.org/v3/index.json`.

## Capabilities

| Command | Purpose | Index option |
| --- | --- | --- |
| `snapshot` | Capture and hash-verify one committed Lucene index into an isolated copy | `--path` |
| `verify-repository` | Validate a snapshot, compare selected content with SQL and optionally verify its REST rebuild | `--path` |
| `lastactivityid-get` | Read activity metadata | `--path` |
| `lastactivityid-set`, `lastactivityid-init` | Set or initialize activity metadata; require `--offline`, backup by default | `--path` |
| `validate` | Read index integrity and produce reports; no backup by default | `--path` |
| `list-index` | List index documents under a repository path | `--index-path` |
| `list-db` | List SQL repository content | None |
| `compare`, `check-subtree` | Compare each node/version and both timestamps; produce Markdown/HTML reports | `--index-path` |
| `clean-orphaned` | Preview/remove exact index documents absent from SQL; dry run by default, `--offline` required for deletion | `--index-path` |
| `rebuild-index` | Request rebuild through the SenseNet REST API | None |

Use `<command> --help` for the complete options. `list-items` and `--live-index` are not registered commands/options.

```powershell
dotnet run --project src/MainProgram -- validate --path "D:\Indexes\copy" --output validation.html --format html
dotnet run --project src/MainProgram -- list-index --index-path "D:\Indexes\copy" --repository-path /Root/Content
dotnet run --project src/MainProgram -- lastactivityid-set --path "D:\Indexes\copy" --id 42 --offline
```

Comparison pairs path, type, node ID and version ID. Published/draft versions and duplicate index documents remain visible. A matching node timestamp with a missing/different version timestamp is not reported as `Match`.

## Kubernetes input

All CLI commands that read a local index accept `--auto-copy-index` instead of their local path option, including validation, comparison, subtree checks and orphan cleanup. `kubectl` must be on PATH; the selected container must provide `ls`, `test` and `tar` for `kubectl cp`.

```powershell
dotnet run --project src/MainProgram -- validate --auto-copy-index --namespace example --deployment repository --container sensenet --copy-output-path "D:\IndexBackups"
dotnet run --project src/MainProgram -- check-subtree --auto-copy-index --namespace example --deployment repository --connection-string "<SQL connection string>" --repository-path /Root/Content
```

Options: `--kubeconfig` (optional; omitted uses the current kubectl context), `--namespace` (default `default`), `--deployment` (required), `--container` (optional for a single-container pod or one named `sensenet`), `--index-path-in-pod` (default `/app/App_Data/LocalIndex`), `--copy-output-path`.

The deployment's actual label selector selects a running, ready pod. The latest numeric directory beginning with `20` (14 or 15 digits) is copied to a unique `.partial` directory. A copy becomes usable only after Lucene can open it; failed copies retain `.partial` for diagnosis. Modifying commands change the local copy only; there is no upload-back operation.

`kubectl cp` is not an atomic snapshot of a changing index. Use a quiesced source or a consistent volume snapshot for reliable comparisons/repairs. Successful parsing does not prove all source files came from the same commit. Live cluster, SQL and repository API behavior require an environment integration test; the automated suite uses local synthetic indexes and simulated kubectl/API responses. Kubernetes input is currently a CLI capability; the web interface operates on server-local paths.

## REST rebuild

Exactly one of `--content-path`, `--content-id` or `--file-path` is required. Batch files contain one `/Root/...` path or positive numeric ID per line, with optional `#` comments. Empty/error batches fail. `--repo-url` is the repository origin and `--api-key` is supplied explicitly.

```powershell
dotnet run --project src/MainProgram -- rebuild-index --repo-url https://repository.invalid --api-key "<API key>" --content-id 123 --recursive false
```

The CLI and web interface share the OData URL builder and send the key through the `apikey` header. See [SenseNet addressing](https://docs.sensenet.com/api-docs/basic-concepts/01-entry/) and [API-key authentication](https://docs.sensenet.com/tutorials/authentication/how-to-authenticate-apikey/). Keys/connection strings must not be committed. The IndexFix PowerShell scripts are legacy helpers requiring explicit endpoint and key input.

## Web interface

Validation, subtree checks, activity metadata, REST rebuild, reports and saved configurations are available. The content-listing page is still a placeholder. Rebuild batch files are specified by server-local path.

The app accepts direct loopback requests by default. Remote access requires explicit `AllowRemoteAccess=true` configuration and an authenticated external proxy; the app has no built-in user authentication. Directory browsing of source files is disabled.

Saved connection strings use ASP.NET Data Protection; legacy plaintext files are encrypted on the next save. Back up both `Data` and `App_Data/keys`. Windows keys are protected for the executing user's account; on other platforms protect the key directory with filesystem permissions. Configuration/report files are intended for one running app process; in-process concurrent writes are serialized and JSON replacement is atomic.

Offline index writes refuse existing writer locks. Stop the writer and investigate stale locks explicitly; the tool does not unlock a running index automatically. Activity IDs must fit the SenseNet Int32 range.

## Tests and publishing

The xUnit regression suite covers comparison versions/timestamps, >10,000 documents, exact orphan deletion, metadata writes/locks, Kubernetes selection/copy failures, REST requests, web startup/pages and local storage. CI runs it on Windows and Linux with .NET 8. The legacy TestSubtreeChecker launcher invokes the comparison tests and propagates failures.

```powershell
dotnet build sensenet-index-tools.sln -c Release
dotnet test test/IndexTools.Tests/IndexTools.Tests.csproj -c Release --no-build
dotnet publish src/MainProgram/sn-index-maintenance-suite.csproj -c Release -o publish/cli
dotnet publish src/WebApp/WebApp/WebApp.csproj -c Release -o publish/web
```

`create-index` belongs to the separate experimental create-index PR and is not part of this branch. It must not be treated as a production-compatible native SenseNet builder before its schema, overwrite safeguards and full-repository validation are completed.
