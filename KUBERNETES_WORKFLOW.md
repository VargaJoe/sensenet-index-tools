# Local Index Tools with Kubernetes

## Overview
Run the SenseNet Index Tools locally on your development machine, using `kubectl cp` to transfer index files between your Kubernetes cluster and local storage.

## Prerequisites
- kubectl configured to access your Kubernetes cluster
- .NET 8.0 SDK installed locally
- SenseNet Index Tools cloned locally

## Workflow

### 1. Copy Index from Kubernetes Pod
```bash
# Create local directory for the index
mkdir .\temp-index

# Copy index from SenseNet pod to local
kubectl cp your-namespace/your-sensenet-pod:/path/to/index .\temp-index -n your-namespace
```

### 2. Run Index Tools Locally

#### Option A: Web Application
```bash
# Navigate to web app directory
cd src/WebApp/WebApp

# Run the web app
dotnet run

# Access at http://localhost:5000 (or configured port)
# Configure index path as: D:\devgit\joe\sensenet-index-tools\temp-index
```

#### Option B: Command Line Interface
```bash
# Validate index
dotnet run -- validate --path ".\temp-index"

# Check subtree
dotnet run -- check-subtree --index-path ".\temp-index" --connection-string "your-connection-string" --repository-path "/Root"

# Other operations...
```

### 3. Copy Modified Index Back (if needed)
For operations that modify the index:
```bash
# Stop SenseNet deployment temporarily
kubectl scale deployment your-sensenet-deployment --replicas=0 -n your-namespace

# Copy back to pod
kubectl cp .\temp-index your-namespace/your-sensenet-pod:/path/to/index -n your-namespace

# Restart SenseNet
kubectl scale deployment your-sensenet-deployment --replicas=1 -n your-namespace
```

## Advantages
- **No deployment complexity**: Run tools on familiar local environment
- **Full tool access**: All CLI and web features available locally
- **Easy debugging**: Local development and testing
- **Flexible operations**: Mix local and remote operations as needed

## For Read-Only Operations
For validation, subtree checking, and other read-only operations, you can copy the index without stopping the SenseNet application.

## Automation Script
Use the provided PowerShell script to automate index copying:

```bash
# Copy from pod to local
.\copy-index.ps1 -PodName your-sensenet-pod -Namespace your-namespace -IndexPathInPod /path/to/index -CopyToLocal

# Copy from local to pod
.\copy-index.ps1 -PodName your-sensenet-pod -Namespace your-namespace -IndexPathInPod /path/to/index -CopyToPod
```

Parameters:
- `PodName`: Name of the SenseNet pod
- `Namespace`: Kubernetes namespace (default: default)
- `IndexPathInPod`: Path to index directory inside the pod
- `LocalIndexPath`: Local directory for index (default: .\temp-index)
- `CopyToLocal`: Copy from pod to local
- `CopyToPod`: Copy from local to pod

## Real-World Example

Successfully tested with production SenseNet deployment:

### Index Copy Process
```bash
# Get deployment info
kubectl --kubeconfig "$env:USERPROFILE\.kube\config-sn-prod" get deployment manfredrepo-staging-sensenet-cloud -n standalone-manfredrepo-staging

# Find pod
kubectl --kubeconfig "$env:USERPROFILE\.kube\config-sn-prod" get pods -n standalone-manfredrepo-staging

# List index contents (find dated folder)
kubectl exec [pod-name] -- ls -la /app/App_Data/LocalIndex/

# Copy latest index
kubectl cp [namespace]/[pod-name]:/app/App_Data/LocalIndex/[latest-folder] ./temp-index-prod
```

### Validation Results
✅ **Index validation passed** - All structure checks successful
✅ **Index integrity verified** - No corruption detected  
✅ **SenseNet fields present** - Compatible with SenseNet requirements

### Database Operations
⚠️ **Network limitation** - Database connections require VPN/cluster access
- Index-only operations work perfectly
- Database comparison operations need network connectivity to SQL Server