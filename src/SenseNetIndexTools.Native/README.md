# SenseNet Native Index Creation Tool (Experimental)

This is an experimental CLI tool for creating SenseNet indexes using SenseNet's native indexing infrastructure instead of manual Lucene operations.

## ⚠️ Experimental Status

**This tool is experimental and under development.** It uses SenseNet's native indexing mechanisms and may not work correctly in all scenarios. Use at your own risk.

## Purpose

This tool addresses the scenario where:
- You have a SenseNet database but no index files
- The existing index files are corrupted
- You need to create a clean index from scratch

## Key Differences from Main Tool

| Feature | Main Tool (`sn-index-maintenance-suite`) | Native Tool (`sensenet-create-index-native`) |
|---------|------------------------------------------|---------------------------------------------|
| **Indexing Method** | Manual Lucene document creation | SenseNet's native `IIndexPopulator.ClearAndPopulateAllAsync()` |
| **Field Processing** | Custom field mapping | SenseNet's built-in field processing pipeline |
| **Provider Integration** | Bypassed | Full SenseNet provider system integration |
| **Security** | Manual | SenseNet's security system |
| **Content Types** | Manual type handling | SenseNet's content type system |
| **Dependencies** | Minimal (Lucene.Net only) | Full SenseNet dependencies |

## Prerequisites

1. **SenseNet Database**: A properly configured SenseNet database
2. **Permissions**: Database user with appropriate permissions
3. **Clean Index Directory**: Empty directory for the new index (recommended)

## Installation

1. Build the solution:
   ```bash
   dotnet build sensenet-index-tools.sln
   ```

2. The tool will be available as `sensenet-create-index-native.exe` in the output directory.

## Usage

### Basic Usage

```bash
sensenet-create-index-native --connection-string "Server=localhost;Database=SenseNet;User Id=user;Password=password;"
```

### Full Example

```bash
sensenet-create-index-native \
  --connection-string "Server=localhost;Database=SenseNet;User Id=user;Password=password;" \
  --repository-path "/Root" \
  --output-path "C:\SenseNet\Index" \
  --create-subfolder \
  --verbose \
  --output-report "index-report.md"
```

### Parameters

| Parameter | Description | Default | Required |
|-----------|-------------|---------|----------|
| `--connection-string` | SQL Server connection string to SenseNet database | - | Yes |
| `--repository-path` | Repository path to index | `/Root` | No |
| `--output-path` | Directory for the new index | Current directory + `IndexOutput` | No |
| `--recursive` | Process all content recursively | `true` | No |
| `--batch-size` | Items to process per batch | `100` | No |
| `--max-items` | Maximum items to index (0 = unlimited) | `0` | No |
| `--create-subfolder` | Create timestamped subfolder | `false` | No |
| `--format` | Report format (`md` or `html`) | `md` | No |
| `--output-report` | Save report to file | - | No |
| `--force-reindex` | Force recreation if index exists | `false` | No |
| `--verbose` | Enable verbose logging | `false` | No |

## How It Works

1. **Repository Initialization**: Creates a SenseNet `RepositoryBuilder` programmatically from CLI arguments
2. **Provider Setup**: Initializes SenseNet's provider system (`Providers.Instance`)
3. **Index Populator**: Gets the `IIndexPopulator` from `Providers.Instance.SearchManager.GetIndexPopulator()`
4. **Native Indexing**: Calls `ClearAndPopulateAllAsync()` to create index using SenseNet's native mechanisms
5. **Report Generation**: Creates detailed reports about the indexing process

## Expected Output

```
🔬 SenseNet Native Index Creation Tool (Experimental)
==================================================
Repository: /Root
Connection: Server=localhost;Database=SenseNet;User Id=...
Output: C:\SenseNet\Index

🔧 Initializing SenseNet Repository...
✅ SenseNet Repository initialized successfully
🔍 Getting Index Populator from providers...
📊 Index Populator type: DocumentPopulator
🏗️ Starting index creation using SenseNet's native ClearAndPopulateAllAsync...
✅ Index creation completed via SenseNet native method
✅ Index creation completed successfully!
📁 Index path: C:\SenseNet\Index\SenseNetIndex_20250916123456
📊 Processed items: 1,250
⏱️ Duration: 45.2 seconds
```

## Troubleshooting

### Common Issues

1. **Repository Initialization Fails**
   - Check database connection string
   - Verify database permissions
   - Ensure SenseNet database schema is correct

2. **Index Creation Fails**
   - Check available disk space
   - Verify output directory permissions
   - Check SenseNet configuration compatibility

3. **Performance Issues**
   - Large databases may take significant time
   - Consider using `--max-items` for testing
   - Monitor database and system resources

### Verbose Logging

Use `--verbose` flag for detailed logging:
```bash
sensenet-create-index-native --connection-string "..." --verbose
```

## Comparison with Manual Approach

### Advantages of Native Approach
- ✅ **Correct Field Processing**: Uses SenseNet's field processing pipeline
- ✅ **Security Integration**: Respects SenseNet's security model
- ✅ **Content Type Handling**: Proper content type processing
- ✅ **Index Compatibility**: Creates indexes compatible with SenseNet installations
- ✅ **Future-Proof**: Works with SenseNet updates and configuration changes

### Disadvantages of Native Approach
- ❌ **Complex Dependencies**: Requires full SenseNet dependency chain
- ❌ **Configuration Complexity**: Needs proper SenseNet configuration
- ❌ **Resource Intensive**: May require more memory and processing
- ❌ **Experimental**: May have compatibility issues

## Development Status

This tool is in **experimental phase**. Key areas under development:

- [ ] Repository configuration from CLI arguments
- [ ] Error handling and recovery
- [ ] Performance optimization
- [ ] Compatibility testing with different SenseNet versions
- [ ] Integration with existing tool ecosystem

## Contributing

When this tool proves stable, it may be merged back into the main CLI tool as a hybrid option.

## Related Documentation

- [SenseNet Native Indexing Plan](../docs/sensenet-native-indexing-plan.md)
- [Main Tool Documentation](../README.md)