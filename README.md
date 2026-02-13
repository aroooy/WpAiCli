# WpAiCli - WordPress AI CLI

A CLI tool to streamline WordPress post management using the power of AI.

## Overview

WpAiCli is a .NET-based cross-platform CLI tool designed to interact with the WordPress REST API. It allows you to manage posts, categories, tags, and media, with the ability to securely switch between multiple site connections.

### Key Features:

- **Secure Credential Storage**: Saves authentication credentials (Application Passwords or JWT tokens) in the Windows Credential Manager or macOS/Linux Secret-Tool.
- **Connection Management**: Register, list, delete, and update connection profiles directly from the CLI.
- **Full CRUD Support**: Supports create, read, update, and delete commands for posts, categories, tags, and media.
- **Two-Way Sync & Local Cache**: Provides two-way synchronization for posts and maintains a local cache.
- **Revision Management**: Supports fetching post revisions.
- **Media Uploads**: Includes functionality to upload media files like images.
- **Flexible Output**: Switch output formats using `--format table|json|raw`.

## Installation

You must have the [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or later) installed on your system.

Execute the following single command to make the `wpai` command available system-wide:

```shell
dotnet tool install --global WpAiCli
```

## Global Options

- `--version`, `-V`: Show version information.
- `--help`, `-h`: Show help.

## Initial Setup

When using the tool for the first time, please follow these setup steps.

### 1. Register a Connection

First, register a connection to your WordPress site. The tool supports `ApplicationPassword` (recommended) and `Jwt` authentication methods.

**Recommended: Using ApplicationPassword**
```shell
wpai connections add --name "MyBlog" --base-url "https://example.com/wp-json" --auth-method "ApplicationPassword" --username "your-username" --password "your-application-password"
```

**Using a JWT Token**
```shell
wpai connections add --name "MyBlog" --base-url "https://example.com/wp-json" --auth-method "Jwt" --jwt-token "your-jwt-token"
```

- `--name`: An arbitrary display name (used for switching connections).
- `--base-url`: The base URL of the WordPress REST API (ending in `/wp-json` is recommended).
- `--auth-method`: Specify `ApplicationPassword` or `Jwt`. (Default: `ApplicationPassword`).
- `--username`: Your WordPress username (required for ApplicationPassword).
- `--password`: The application password generated in WordPress (required for ApplicationPassword).
- `--jwt-token`: The token for JWT authentication (required for Jwt).

Authentication credentials are stored securely in your OS's credential store.

### 2. Set the Active Connection

Next, set the "active" connection that commands will target. All commands except for `connections` will be executed against the active connection set here.

**Note:** If you add the first connection when no others are registered, it is automatically set as active, and this step is not necessary.
```
wpai connections active
```
Running the command without arguments will display a list of registered connections and allow you to interactively select the active one. You can also specify it directly by name, like `wpai connections active "BlogName"`.

### 3. Verify Operation

To confirm that the setup is correct, try fetching a list of posts.
```
wpai posts list --status publish --per-page 1
```
If posts from your active site are displayed, the initial setup is complete.

## Common Usage (Workflow Examples)

### 1. Create and Publish a New Post

1. Create a draft post with `wpai posts create --title "New Article" --content "Write the body here" --status draft`.
   - A local cache file (`.md`) is automatically generated at this point.
2. Edit the generated `posts/123-new-article.md` in your editor.
3. Run `wpai posts push 123` to apply your edits to the server.
4. To publish the article, change the `status` in the YAML front-matter at the top of the file to `publish` and run `wpai posts push 123` again.

### 2. Edit an Existing Post

1. Run `wpai posts sync` to fetch the latest state from the server.
2. Edit the local Markdown file (`.md`) for the desired article.
3. Run `wpai posts push <ID>` to apply the changes to the server.

## Recommended Workflow for AI

The following patterns are recommended for AI agents executing tasks.

### Scenario 1: Pushing Local Edits to the Server

When you have finished editing multiple local post or taxonomy files and want to send the results to the server.

- **Recommended Commands:**
  - `posts push --all`
  - `taxonomies sync`
- **Reasoning:**
  This most clearly expresses the intent to "push local changes." While `posts sync` might achieve a similar result, distinguishing between `push` and `sync` makes the AI's operation logs more understandable for humans.

### Scenario 2: Fetching the Latest State Before Editing

When you want to start working with the latest content, pulling in any changes made by other users.

- **Recommended Commands:**
  - `posts sync`
  - `media sync`
- **Reasoning:**
  The `sync` command reliably performs a pull of changes from the server, making it ideal for this purpose.

## Command Reference

For machine integration (like with an AI), JSON mode (`--format json`) is recommended. It is easier to handle in terms of encoding/parsing and avoids character corruption issues compared to text output.

### Connection Management (`connections`)

- `list`: Displays a list of registered connections. `=>` indicates the active connection.
- `active`: Sets the default connection to use as "active," either interactively or by name/number.
  - `wpai connections active` (Interactive mode)
  - `wpai connections active 2` (By number)
  - `wpai connections active "My Blog"` (By name)
- `add`: Registers a new connection.
  - `wpai connections add --name <NAME> --base-url <URL> --auth-method <ApplicationPassword|Jwt> ...`
  - `--cache-path` is optional. If omitted, `wp-cache` in the current execution directory is set as the default cache path.
- `update <name>`: Updates existing connection information. Use this command for settings like cache path and sync configurations.
  - `wpai connections update "BlogName" --cache-path ./new-cache --sync-limit 50 --markdown-conversion server`
- `remove`: Deletes an existing connection interactively.

#### Check Cache Path (`cache`)

- Displays the cache root path for the active connection. `[Cache Change: None]`
  - `wpai cache`
  - Example output: `C:\path\to\wp-cache\<connection-name>`

### Posts (`posts`)

- `sync`: **Performs a two-way sync for posts and taxonomies (categories/tags).** It's a safe process that first syncs taxonomies and proceeds to post synchronization only if successful. `[Cache Effect: Reflects server changes]`
  - `wpai posts sync`
- `organize`: Organizes local post files (`.md`) into subfolders like `publish`, `draft`, etc., based on the status in each file's YAML header. Does not communicate with the server. `[Cache Effect: Local file move]`
  - `wpai posts organize`
- `list`: Lists posts. `[Cache Effect: None]`
  - `wpai posts list [--status <STATUS>] [--per-page <NUM>] [--page <NUM>]`
- `get <id>`: Retrieves a single post by its ID. `[Cache Effect: None]`
  - `wpai posts get 123`
- `create`: Creates a new post. `[Cache Effect: Immediate creation]`
  - `wpai posts create --title <TITLE> --content <CONTENT> | --content-file <PATH> [--status <STATUS>] [--edit-mode <markdown|html>] [--categories <IDs>] [--tags <IDs>] [--featured-media <ID>]`
  - **Note:** `--content` or `--content-file` is required, and its content cannot be empty or whitespace.
  - **Hint for AI Usage:** Do not include the article title in the body content. The title is specified separately with the `--title` option, and including it in the body will cause duplication.
- `push <id> | --all`: Pushes local cache changes to the server. You can push individually by ID or all locally modified posts at once with `--all`. `[Cache Effect: Updated after server reflection]`
  - `wpai posts push 123`
  - `wpai posts push --all`
- `delete <id>`: Deletes a post. `[Cache Effect: Immediate deletion]`
  - `wpai posts delete 123 [--force]`

### Revisions (`revisions`)

Manages post change history (revisions).

- `list --post-id <ID>`: Retrieves a list of revisions for a specified post. `[Cache Effect: None]`
  - `wpai revisions list --post-id 123`
- `fetch --post-id <ID>`: Downloads all revisions for a specified post to the local cache. Ideal for comparison and review. `[Cache Effect: Creates files under `wp-cache/revisions/post_<ID>/`]`
  - `wpai revisions fetch --post-id 123`
- `clean [--post-id <ID>]`: Deletes the locally downloaded revision cache. `[Cache Effect: Deletes files/folders under `wp-cache/revisions/`]`
  - `wpai revisions clean --post-id 123` (Deletes revisions for a specific post)
  - `wpai revisions clean` (Deletes all revision caches)

#### Restoring a Post from a Revision (Workflow)

For safety, this tool recommends the following manual workflow for restoring from a revision.

1.  **Check History:** Run `wpai revisions list --post-id 123` to check revision IDs and dates.
2.  **Bulk Fetch:** Run `wpai revisions fetch --post-id 123` to download all revisions locally.
3.  **Compare:** Use an editor or diff tool to compare the revision files (e.g., `456.md`) in the `wp-cache/revisions/post_123/` directory with the current post file `wp-cache/posts/publish/123.md`.
4.  **Apply Content:** Copy the content from the desired revision file and paste it into the current post file, overwriting it.
5.  **Push to Server:** Run `wpai posts push 123`. The local file changes will be pushed to the server, completing the post restoration.

### Categories (`categories`)

- `list`: Lists categories. `[Cache Effect: None]`
- `get <id>`: Retrieves a single category by its ID. `[Cache Effect: None]`
- `create`: Creates a new category. `[Cache Effect: Immediate creation]`
  - `wpai categories create --name <NAME> [--slug <SLUG>] [--description <DESC>]`
- `push <id>`: Pushes local cache (YAML file) changes to the server. `[Cache Effect: Updates hash only after server reflection]`
  - `wpai categories push 45`
- `delete <id>`: Deletes a category. `[Cache Effect: Immediate deletion]`

### Tags (`tags`)

- `list`: Lists tags. `[Cache Effect: None]`
- `create`: Creates a new tag. `[Cache Effect: Immediate creation]`
  - `wpai tags create --name <NAME> [--slug <SLUG>] [--description <DESC>]`
- `get <id>`: Retrieves a single tag by its ID. `[Cache Effect: None]`
- `push <id>`: Pushes local cache (YAML file) changes to the server. `[Cache Effect: Updates hash only after server reflection]`
  - `wpai tags push 67`
- `delete <id>`: Deletes a tag. `[Cache Effect: Immediate deletion]`

### Taxonomies (`taxonomies`)

- `sync`: Performs a two-way sync for all categories and tags between the local cache and the server. `[Cache Effect: Reflects server changes]`
  - `wpai taxonomies sync`

### Media (`media`)

- `sync`: Syncs media between the local cache and the server. Media deleted on the server will be removed from the local cache. `[Cache Effect: Reflects server changes]`
- `list`: Lists items in the media library. `[Cache Effect: None]`
  - `wpai media list [--per-page <NUM>] [--page <NUM>]`
- `upload <file-path>`: Uploads a file to the media library. `[Cache Effect: Immediate creation]`
  - `wpai media upload <PATH> [--file <PATH>] [--title <TITLE>] [--description <DESC>]`
- `push <id>`: Pushes metadata changes (title, alt text, etc.) from the local cache (YAML file) to the server. `[Cache Effect: Updates metadata only after server reflection]`
  - `wpai media push 89`
- `delete <id>`: Deletes media. `[Cache Effect: Immediate deletion]`
  - `wpai media delete 123 [--force]`

### Conflict Resolution (`resolve`)

If a conflict is detected during `posts sync`, use this command to resolve it manually.

- `resolve <type> <id> --strategy <strategy>`: Resolves a conflict.
  - `<type>`: The type of content that conflicted (`post`, `category`, `tag`).
  - `<id>`: The ID of the conflicted item.
  - `--strategy <strategy>`: Required. Specify one of the following resolution strategies:
    - `local-wins`: Prioritizes local changes, overwriting the server state with the local state.
    - `server-wins`: Prioritizes server changes, overwriting the local state with the server state.

  **Example:**
  ```bash
  # Resolve conflict for post ID 123, prioritizing local changes
  wpai resolve post 123 --strategy local-wins

  # Resolve conflict for category ID 45, prioritizing server changes
  wpai resolve category 45 --strategy server-wins
  ```

  **Note on Conflict Detection in `server` Markdown Conversion Mode**

  When the Markdown conversion setting (`--markdown-conversion`) is set to `server`, conflict detection depends on changes to a special meta field on the server called `_md_source`.

  This means that editing a post using the standard Visual or Code editor in the WordPress admin area will **not** update the `_md_source` field. As a result, **edits from the admin panel will not be detected as a server-side change, and no conflict will occur.** The local changes will simply overwrite the server changes.

  To correctly detect conflicts in `server` mode, the `_md_source` meta field must also be updated on the server side, typically via the REST API.

## Sync Functionality

### Server-Side Setup: Two-Way Markdown Sync

To fully leverage the two-way Markdown sync feature (`editMode: markdown`), you need to place a dedicated plugin on your WordPress server. This allows the REST API to recognize the Markdown source text as a custom field (`_md_source`).

Running the following command will output a `mu-plugins.zip` file in the current directory and display detailed installation instructions.

```shell
wpai export-plugin
```

### Core Sync Concepts: `push` vs. `sync`

This tool has two types of commands, `push` and `sync`, for aligning local and server states. It is important for an AI to understand these differences to express its intent clearly.

- **`push` commands (`posts push --all`, etc.)**
  - **Intent:** "I want to unilaterally apply my local work to the server."
  - **Direction:** Local → Server (One-way)
  - **Use Case:** Ideal for when you have finished several local edits and want to send the result to the server. It does not consider server-side changes.

- **`sync` commands (`posts sync`, etc.)**
  - **Intent:** "I want to compare local and server states and make them identical."
  - **Direction:** Local ↔ Server (Two-way)
  - **Use Case:** Ideal for fetching the latest server state before starting work or for carefully synchronizing in a multi-user environment where conflicts might occur. Internally, both pushes and pulls may happen.

The `posts sync` and `media sync` commands synchronize content (posts, media, categories, tags) between your local filesystem and the WordPress server.

### Configuration

To enable sync, you must first set the path to a cache directory in your connection settings.
```
# Set during a new connection
wpai connections add --name "MyBlog" --base-url <URL> --cache-path ./my-blog-cache

# Update an existing connection
wpai connections update "MyBlog" --cache-path ./my-blog-cache
```

### Sync Execution and Cache Directory Structure

After configuration, running `posts sync` or `media sync` will start the synchronization. The basic structure of the cache directory is as follows:

1.  **Per-Connection Subdirectory**: A subdirectory named after the connection profile is created within the root directory specified by `--cache-path` (e.g., `wp-cache/my-blog/`). This prevents the caches of multiple blogs from interfering with each other.
2.  **Cache File Generation**: The following files and directories are generated within each connection's subdirectory:

    -   `wp-ai-cache.db`: An SQLite database file that manages content metadata. This file is used internally by the application. **Users should not edit this file directly.**
    -   `categories/` directory: **Editable** YAML files for categories are stored individually (`[ID]-[Name].yaml`). The `slug` in the file is saved in a human-readable format, including non-ASCII characters.
    -   `tags/` directory: **Editable** YAML files for tags are stored individually (`[ID]-[Name].yaml`). The `slug` is also saved in a readable format.
    -   `posts/` directory: Subdirectories are created for each post status (`publish`, `draft`, `future`, etc.), and post files are stored within them.
        -   `[ID]-[Title].md`: A single file containing all information for a post. Metadata is at the top as YAML front-matter, followed by the body content.
    -   `media/` directory:
        -   `[ID]-[Filename].[ext]`: The media file itself.
        -   `[ID]-[Filename].yaml`: Editable metadata for the media.

### Sync Rules

- **`posts sync`:** The safest and most comprehensive sync command. It executes in the following order:
  1.  **Two-Way Taxonomy Sync:** First, an equivalent of `taxonomies sync` is performed, pushing local category/tag changes and pulling the latest state from the server. If an error occurs here, the process is aborted for safety.
  2.  **Two-Way Post Sync:** After taxonomy sync succeeds, local and server states for each post are compared, and one of "push," "pull," or "conflict detection" is executed.

- **`taxonomies sync`:** A sync command dedicated to taxonomies (categories/tags). It executes in the following order:
  1.  **Push All Local Changes:** First, changes from all local taxonomy files (`.yaml`) are pushed to the server.
  2.  **Pull All Server Changes:** Next, the latest taxonomy information is pulled from the server, updating the local cache.
  (Therefore, this command effectively also serves the role of `taxonomies push --all`).

- **`media sync`:** A sync command dedicated to media. It follows the same order as `taxonomies sync`.
  1.  **Push All Local Changes:** First, changes from all local media metadata files (`.yaml`) are pushed.
  2.  **Pull All Server Changes:** Next, the latest media information is pulled from the server.

- **Conflict Detection:** If the same post has been modified both locally and on the server, a conflict is detected during `posts sync`, and the sync for that item is skipped for safety. You must manually resolve it using the `resolve` command as indicated in the report.
- **Automatic Cache Cleanup:** When `posts sync` or `media sync` is run, if an old cache file is not included in the sync limit (`--sync-limit`), has already been deleted on the server (404 Not Found), and has not been modified locally, that local cache file is automatically deleted.

- **Command Execution and Automatic Cache Updates:**
  - When `create` (posts, categories, tags) or `upload` (media) is executed successfully, the local cache is **automatically created**. This allows you to start editing locally immediately and apply changes with the `push` command.
  - When `delete` (posts, categories, tags, media) is executed, the local cache is **automatically deleted** upon successful deletion on the server. If the target already doesn't exist on the server (404), the local cache is also cleaned up.
  - Use `push <id>` to apply local file edits to the server, and `sync` to pull server-side changes locally.

### Locally Editable Files

Users should directly edit the following files:

- `categories/[ID]-[Name].yaml`: You can change the `name`, `slug`, and `description` of a category. The public URL is displayed as `url` for reference, but editing this field has no effect.
  - **Creation:** Add a new YAML file to this directory (with `id` as `0` or unspecified) and run `posts sync` to create a new category on the server.
  - **Deletion:** Deleting this file does not delete the category on the server. Use the `categories delete <id>` command for deletion.
- `tags/[ID]-[Name].yaml`: You can change the `name`, `slug`, and `description` of a tag. The public URL is displayed as `url` for reference, but editing it has no effect.
  - **Creation:** Follow the same procedure as for categories to create a new tag.
  - **Deletion:** Deleting this file does not delete the tag on the server. Use the `tags delete <id>` command.
- `media/[ID]-[Filename].yaml`: You can change the `title`, `alt_text`, `caption`, and `description` of media. The URL to the attachment page (`url`) and a direct link to the media file (`source_url`) are displayed for reference, but editing them has no effect. These fields will always be present in the YAML file, even if their value is empty.
- `posts/[ID]-[Title].md`: A single file containing the post's metadata and body. You can change everything about the post by editing this file.
    - **Metadata:** Edit the YAML front-matter block enclosed by `---`. In addition to standard fields like `title`, `slug`, and `status`, you can manage arbitrary custom fields by writing a `meta` block.
        - **`meta` Block Usage:**
            ```yaml
            meta:
              my_custom_field: "some value"
              another_field: 123
            ```
        - **Note:** To save custom fields added in the `meta` block to the server, you must first register them for the REST API on the WordPress side using the `register_post_meta` function.
        - **Reserved Field:** `_md_source` is used internally to store the body content when `editMode: markdown`, so do not set it manually in the `meta` block.
    - **`date` Field:** Specify the time in `yyyy-MM-dd HH:mm:ss` format in your **local timezone**. The timezone is automatically converted when syncing dates with the server. If specified in an invalid format, an error will be detected and the process will be aborted.
    - **Body:** The content after the YAML front-matter block becomes the post's body.
    - **Specifying `categories` and `tags`:** To associate categories or tags with a post, use one of the following formats:
      - **`ID-Name` Format (Recommended):** A format connecting the ID and name with a hyphen, like `142-Action-Cams`. Since files are generated in this format during sync, copying and using this format is the most reliable method.
      - **ID only:** You can also specify just the numeric ID, like `142`.
    - **Input Validation:** When running `push` or `sync`, if a specified ID does not exist in the local cache, an error occurs, and the process is aborted. This prevents unintended categories or tags from being set. Specification by name or slug alone is no longer supported to eliminate ambiguity.
    - **Note:** Creating a new post file locally and running `posts sync` will not create a new post on the server. Use the `posts create` command for new posts.

**Note:** If you delete an item from an editable YAML file (e.g., the `slug:` line), that item is only **excluded from being updated**; it does not clear the value on the server. If you want to clear the value, explicitly set an empty value, like `slug: ''`.

## Output Format

Can be switched with `--format table|json|raw`. The default is `table`.

## Displaying Documentation

`wpai docs` or `wpai --help` will display the contents of this README file.

## Troubleshooting

- "No connections registered": Register a connection with `wpai connections add`.
- 401/403 errors like `rest_forbidden_context`: The authentication credentials (Application Password or JWT token) are incorrect, lack necessary permissions, or have expired. Re-register the connection with new credentials.
- **If you get a 403 error with an Application Password:** This is likely not a client-side issue but a server-side one where the webserver (like Apache) is not passing the `Authorization` header to WordPress. Try adding one of the following settings to the `.htaccess` file in your WordPress root to resolve it.
  - **Solution 1:** `CGIPassAuth On`
  - **Solution 2:** `RewriteEngine On`, `RewriteCond %{HTTP:Authorization} ^(.*)`, `RewriteRule .* - [e=HTTP_AUTHORIZATION:%1]`
- "Sorry, you are not allowed to upload this file type" error on `media upload`: Your ability to upload certain file types may be restricted by a security plugin, theme, or multisite settings in WordPress.
- "Cache path is not configured" error on `posts sync`: Set a cache directory with `wpai connections update <name> --cache-path <PATH>`.

## Completion Scripts

```
# PowerShell
wpai completion --shell powershell | Out-String | Invoke-Expression

# Bash
wpai completion --shell bash > /etc/bash_completion.d/wpai

# Zsh
wpai completion --shell zsh > ~/.zfunc/_wpai
```
Supported shells: bash / zsh / PowerShell.
