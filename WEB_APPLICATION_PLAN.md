# SenseNet Index Maintenance Web Application

## Overview

This document outlines the plan to expand the SenseNet Index Maintenance Suite with a web-based interface, allowing users to run index maintenance operations and view reports through a browser.

## Purpose

The web application will provide the following benefits:

1. **Web-based Access**: Access index maintenance tools from any browser
2. **Interactive Reports**: View rich, formatted reports directly in the browser
3. **Report History**: Access previous report runs without re-running operations
4. **User-friendly Interface**: Configure operations using simple forms
5. **Centralized Management**: Run operations and view results from a single interface

## Architecture

The application will follow a simple, maintainable architecture that preserves all existing CLI functionality:

1. **Existing CLI Application**: Maintained as-is for command-line usage
2. **New Shared Library**: Common code extracted from CLI for reuse in both applications
3. **New Web Application**: Razor Pages app for browser-based access

### Project Structure

```
sensenet-index-tools/
├── src/
│   ├── MainProgram/                  # Existing CLI application
│   ├── SenseNet.IndexTools.Core/     # New shared library
│   ├── SenseNet.IndexTools.Web/      # New web application
│   ├── TestIndexLoader/              # Existing test project
│   └── TestSubtreeChecker/           # Existing test project
└── ...
```

## Technology Stack

- **Platform**: .NET 8.0
- **Web Framework**: ASP.NET Core Razor Pages
- **UI**: HTML, CSS, minimal JavaScript
- **Data Storage**: Local file-based storage for reports (JSON format)
- **Dependencies**: Reuse all existing NuGet packages

## Features

### Core Features

1. **Dashboard**
   - Overview of available tools
   - Quick access to recent reports
   - System status indicators

2. **Operation Pages**
   - LastActivityId Management
     - Get current LastActivityId
     - Set new LastActivityId
     - Initialize LastActivityId
   - Index Validation
     - Basic and detailed validation options
   - Content Listing
     - List items from index and/or database
   - Subtree Checking
     - Check if database content exists in index

3. **Report Viewing**
   - Formatted display of operation results
   - Download reports in various formats (JSON, CSV, etc.)
   - Compare reports (where applicable)

4. **Settings Management**
   - Configure default index paths
   - Configure database connections
   - Set backup preferences

### Technical Implementation

1. **Code Sharing**
   - Extract core functionality to shared library
   - Use dependency injection for services
   - Create clean interfaces for operations

2. **Web Application**
   - Razor Pages for each operation type
   - Form-based input for configuration
   - AJAX for progress reporting on long-running operations
   - Simple, responsive CSS

3. **Report Storage**
   - Store reports as JSON files
   - Organize by operation type and timestamp
   - Include metadata for search/filtering

## Implementation Plan

### Phase 1: Project Setup

1. Create shared library project
2. Create web application project
3. Extract core code from CLI app
4. Establish basic project structure

### Phase 2: Core Operations

1. Implement LastActivityId operations
2. Implement Validation operations
3. Implement basic reporting

### Phase 3: Advanced Features

1. Implement Content Listing
2. Implement Subtree Checking
3. Enhance reporting capabilities

### Phase 4: Polish & Refinement

1. Improve UI/UX
2. Add report comparison
3. Optimize performance
4. Add authentication (if needed)

## UI Mockups

### Dashboard
```
+--------------------------------------------+
|  SenseNet Index Maintenance Suite          |
+--------------------------------------------+
| [Dashboard] [Operations] [Reports] [Config]|
+--------------------------------------------+
|                                            |
| Quick Actions:                             |
| - Get LastActivityId                       |
| - Validate Index                           |
| - Check Subtree                            |
|                                            |
| Recent Reports:                            |
| - Validation Report (2025-05-25 14:30)     |
| - Subtree Check (2025-05-24 09:15)         |
| - LastActivityId (2025-05-23 16:45)        |
|                                            |
+--------------------------------------------+
```

### Operation Form (Example: Validate Index)
```
+--------------------------------------------+
|  SenseNet Index Maintenance Suite          |
+--------------------------------------------+
| [Dashboard] [Operations] [Reports] [Config]|
+--------------------------------------------+
|                                            |
| Validate Index                             |
|                                            |
| Index Path:                                |
| [                                       ] |
|                                            |
| Options:                                   |
| [x] Detailed Validation                    |
| [x] Create Backup                          |
|                                            |
| Backup Path (optional):                    |
| [                                       ] |
|                                            |
| Sample Size:                               |
| [    100     ] (0 for full validation)     |
|                                            |
| [         Run Validation         ]         |
|                                            |
+--------------------------------------------+
```

### Report View (Example: Validation Report)
```
+--------------------------------------------+
|  SenseNet Index Maintenance Suite          |
+--------------------------------------------+
| [Dashboard] [Operations] [Reports] [Config]|
+--------------------------------------------+
|                                            |
| Validation Report (2025-05-25 14:30)       |
|                                            |
| Index Path: D:\path\to\index               |
| Options: Detailed, Backup Created          |
|                                            |
| Summary:                                   |
| - Index Version: 2.9.4                     |
| - Documents: 12,345                        |
| - Fields: 67                               |
| - Health: Good                             |
|                                            |
| Issues:                                    |
| - 3 documents with missing NodeId          |
| - 1 document with invalid Version format   |
|                                            |
| [Download Report] [Run Again] [Compare]    |
|                                            |
+--------------------------------------------+
```

## Benefits

1. **Improved Accessibility**: Browser-based access from any location
2. **Better Visualization**: Rich report formatting and visualization
3. **Historical Analysis**: Access to past reports for comparison
4. **Simplified Operation**: User-friendly forms for configuration
5. **Maintenance Efficiency**: Faster problem identification and resolution

## Conclusion

The web application will significantly enhance the usability and value of the SenseNet Index Maintenance Suite while preserving all existing command-line functionality. The Razor Pages approach provides a lightweight, maintainable solution that can be easily extended with additional features in the future.
