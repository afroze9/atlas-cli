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

## MCP Server

atlas-cli includes a built-in [Model Context Protocol](https://modelcontextprotocol.io/) server, allowing AI assistants (Claude Desktop, Cursor, etc.) to interact with Jira and Confluence directly.

```bash
# Start the MCP server over stdio
atlas-cli mcp
```

### Claude Desktop Configuration

Add to your Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "atlas-cli": {
      "command": "atlas-cli",
      "args": ["mcp"],
      "env": {
        "ATLAS_CLI_DOMAIN": "mycompany",
        "ATLAS_CLI_EMAIL": "user@example.com",
        "ATLAS_CLI_API_TOKEN": "<your-api-token>",
        "ATLAS_CLI_SKIP_ALLOWLIST": "true"
      }
    }
  }
}
```

### Available MCP Tools

**Jira:**

| Tool | Description |
|------|-------------|
| `jira_view` | View a work item by key |
| `jira_search` | Search with JQL |
| `jira_create` | Create a work item |
| `jira_edit` | Edit a work item |
| `jira_transition` | Transition to a new status |
| `jira_assign` | Assign/unassign a work item |
| `jira_comment_list` | List comments on a work item |
| `jira_comment_create` | Add a comment |
| `jira_project_list` | List projects |
| `jira_project_view` | View project details |

**Confluence:**

| Tool | Description |
|------|-------------|
| `confluence_page_list` | List pages |
| `confluence_page_view` | View a page |
| `confluence_page_create` | Create a page |
| `confluence_page_update` | Update a page |
| `confluence_space_list` | List spaces |
| `confluence_space_view` | View a space |

**Admin:**

| Tool | Description |
|------|-------------|
| `permissions_list` | List allowed projects/spaces |
| `permissions_allow` | Add/update allowed project/space |
| `permissions_remove` | Remove from allowed list |
| `auth_status` | Show authentication status |
| `auth_switch` | Switch active account |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `ATLAS_CLI_DOMAIN` | Atlassian domain (e.g. `mycompany`) |
| `ATLAS_CLI_EMAIL` | Account email |
| `ATLAS_CLI_API_TOKEN` | API token |
| `ATLAS_CLI_SKIP_ALLOWLIST` | Set to `true` to bypass permission checks |
| `ATLAS_CLI_STORY_POINTS_FIELD` | Custom field ID for story points |
| `ATLAS_CLI_START_DATE_FIELD` | Custom field ID for start date |

## Build

Requires .NET 10 SDK.

```bash
dotnet build
dotnet pack
dotnet tool install --global --add-source ./nupkg atlas-cli
```
