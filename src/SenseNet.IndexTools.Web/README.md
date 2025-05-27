# SenseNet Index Tools Web Application

A web-based front-end for the SenseNet Index Tools, providing a user-friendly interface for managing and troubleshooting Lucene indexes in SenseNet repositories.

## Features

- **Index Validation**: Validate the structure and integrity of Lucene indexes
- **Subtree Checking**: Compare content items in a repository subtree with their index counterparts
- **Last Activity ID Tracking**: Check and monitor the last processed activity ID
- **Content Listing**: List content items from the database or index
- **Report Storage**: Save and view previous operation reports
- **Settings Management**: Configure connection strings and default paths

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- SQL Server instance with a SenseNet content repository
- Lucene index directory accessible from the web server

### Running the Application

1. Clone the repository
2. Navigate to the project directory:
   ```
   cd sensenet-index-tools
   ```
3. Run the web application:
   ```
   dotnet run --project src/SenseNet.IndexTools.Web/SenseNet.IndexTools.Web.csproj
   ```
4. Open a browser and navigate to `https://localhost:5001` or `http://localhost:5000`

### Configuration

1. First-time setup will prompt you to configure connection strings and index paths
2. All settings are stored in `usersettings.json` in the application root
3. You can modify these settings at any time through the Settings page

## Architecture

The application is built using ASP.NET Core with Razor Pages and follows a clean architecture:

- **SenseNet.IndexTools.Core**: Contains all business logic, services, and domain models
- **SenseNet.IndexTools.Web**: Contains the web UI and controllers

## Related Projects

- **sn-index-maintenance-suite**: Command-line tool for index maintenance operations

## License

This project is licensed under the same terms as SenseNet content repository.
