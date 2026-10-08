# Kubernetes index workflow

Run the tools locally with .NET 8 and kubectl. See [README](README.md#kubernetes-input) for the current capabilities and all options.

```powershell
dotnet run --project src/MainProgram -- lastactivityid-get --auto-copy-index --namespace example --deployment repository --container sensenet
dotnet run --project src/MainProgram -- validate --auto-copy-index --namespace example --deployment repository --container sensenet --output validation.html --format html
dotnet run --project src/MainProgram -- check-subtree --auto-copy-index --namespace example --deployment repository --container sensenet --connection-string "<SQL connection string>" --repository-path /Root/Content
```

Omit `--kubeconfig` to use the current context, or specify an explicit config. Check that context before copying. The pod/container must support `ls`, `test` and `tar`.

The tool reads the deployment selector, chooses a running ready pod, selects the latest 14/15-digit directory starting with `20` under `/app/App_Data/LocalIndex`, and copies it into a unique local `IndexBackups` directory. `--index-path-in-pod` and `--copy-output-path` override these locations. The same container is used for discovery and copying. Multi-container pods need `--container` unless one is named `sensenet`.

Copies are first stored with a `.partial` suffix. Only a successfully opened Lucene index is promoted to the final directory and passed to the command. Failed partials remain for diagnosis. Every operation uses a separate copy and never changes the process's current directory.

A live `kubectl cp` is not an atomic snapshot: source commits/merges can change files during transfer. Quiesce the source writer or use a consistent volume snapshot before relying on comparison or repair results. The reader check only proves local readability. Current regression tests simulate kubectl and use synthetic Lucene data; a live cluster acceptance run remains required.

`lastactivityid-set`, `lastactivityid-init` and non-dry-run `clean-orphaned` modify only the local copy and require `--offline`. They do not upload it to Kubernetes. Replacing a production index requires a separate, explicitly planned operational procedure.
