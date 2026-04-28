# atlas-cli

A .NET CLI tool for interacting with Jira, Confluence, and Bitbucket Cloud.

## Features

- **Jira** — work items (issues) and projects
- **Confluence** — spaces and pages
- **Bitbucket** — workspaces, repositories, and pipelines (status, steps, logs, run/stop)
- **Auth** — token-based authentication management with multi-account, scoped Bitbucket login, and per-repo access tokens
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

# Bitbucket
atlas-cli bitbucket repo list      --workspace WS
atlas-cli bitbucket pipeline list  --workspace WS --repo REPO
atlas-cli bitbucket pipeline view  --workspace WS --repo REPO --id 42
atlas-cli bitbucket pipeline log   --workspace WS --repo REPO --id 42 --tail 200

# Output format
atlas-cli --format json jira workitem list
```

## Bitbucket Authentication

Bitbucket Cloud rejects Atlassian API tokens that were not created with Bitbucket scopes. atlas-cli supports three independent ways to authenticate to Bitbucket — in increasing specificity:

1. **Shared Atlassian credentials** — works only if the API token used in `auth login` was created with Bitbucket scopes (`read:repository:bitbucket`, `read:pipeline:bitbucket`, etc.). No extra setup needed.
2. **Account-level Bitbucket credentials** — a separate Atlassian API token with Bitbucket scopes (Basic), or any Bitbucket access token (Bearer):
   ```bash
   atlas-cli auth bitbucket-login --mode basic  --email you@co.com --token <ATATT…> --workspace WS
   atlas-cli auth bitbucket-login --mode bearer --token <ATCTT…> --workspace WS
   atlas-cli auth bitbucket-set-workspace WS    # set or change the default workspace
   atlas-cli auth bitbucket-logout              # clear Bitbucket creds (Atlassian creds preserved)
   ```
3. **Per-repo access tokens** — for environments where IT restricts org/workspace tokens but you can mint repo-scoped ones (Bitbucket → Repository settings → Access tokens):
   ```bash
   atlas-cli auth bitbucket-add-repo-token    --workspace WS --repo REPO --token <ATCTT…>
   atlas-cli auth bitbucket-remove-repo-token --workspace WS --repo REPO
   atlas-cli auth bitbucket-list-tokens       # show all Bitbucket creds + per-repo tokens
   ```

When a per-repo token is registered for the workspace+repo of a request, it is preferred over the account-level Bitbucket credentials, which are in turn preferred over the shared Atlassian credentials. After setting a default workspace via `bitbucket-set-workspace`, the `--workspace` flag becomes optional on every `bitbucket repo`/`bitbucket pipeline` command.

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

**Bitbucket:**

| Tool | Description |
|------|-------------|
| `bitbucket_workspace_list` | List workspaces accessible to the current user |
| `bitbucket_repo_list` | List repositories in a workspace |
| `bitbucket_repo_view` | View a repository |
| `bitbucket_pipeline_list` | List pipeline runs (filter by branch/state) |
| `bitbucket_pipeline_view` | View a pipeline run by build number or UUID |
| `bitbucket_pipeline_steps` | List steps for a pipeline run |
| `bitbucket_pipeline_log` | Fetch raw log for a pipeline step (auto-targets first failed step; supports `tail`) |
| `bitbucket_pipeline_stop` | Stop a running pipeline |
| `bitbucket_pipeline_run` | Trigger a new pipeline run on a branch and/or commit |

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
