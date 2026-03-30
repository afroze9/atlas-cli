# atlas-cli

A .NET CLI tool for interacting with Jira and Confluence Cloud.

## Features

- **Jira** — work items (issues) and projects
- **Confluence** — spaces and pages
- **Auth** — token-based authentication management
- **Permissions** — manage allowed Confluence spaces

## Installation

```bash
dotnet tool install --global atlas-cli
```

## Usage

```bash
# Authenticate
atlas-cli auth login

# Jira
atlas-cli jira workitem list
atlas-cli jira project list

# Confluence
atlas-cli confluence space list
atlas-cli confluence page list

# Output format
atlas-cli --format json jira workitem list
```

## Build

Requires .NET 10 SDK.

```bash
dotnet build
dotnet pack
dotnet tool install --global --add-source ./nupkg atlas-cli
```
