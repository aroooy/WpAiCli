# WpAiCli - Guidelines for AI Agents

This document defines operational rules and workflows for AI agents (Claude, Gemini, Codex, etc.) interacting with WordPress through **WpAiCli** or working within this repository.

---

## 1. Core Architecture & Philosophy

- **Local-First & Git-Centric**: WordPress content is mirrored into a structured local directory (`wp-cache/<connection>/posts/[status]/[ID]-[Title].md`).
- **Internal SQLite Cache**: A local SQLite database (`wp-ai-cache.db`) tracks server IDs, hashes, and synchronization states.
- **Dual Interface**:
  - **MCP (Model Context Protocol)**: Structured, type-safe JSON tools (`CreatePost`, `PushPost`, `PullPosts`, etc.) designed for direct AI consumption without shell quoting issues.
  - **CLI (`wpai`)**: Terminal interface for humans and script execution.

---

## 2. Content Creation Workflow (Golden Rules)

When instructed to create, draft, or publish a new article:

### Pattern A: If MCP Tools are Available (Preferred)
1. Call `CreatePost` with `title`, `content` (Markdown body), and `status="draft"` (or `"publish"`).
2. The MCP tool handles validation, WordPress API creation, and local cache placement automatically.
3. No shell escaping or temporary file handling is required.

### Pattern B: If Using CLI Commands
Follow the **3-Step Frame-and-Edit Workflow**:
1. **Create Post Frame**:
   ```bash
   wpai posts create --title "Your Article Title" --status draft --categories <id>
   ```
   *Do NOT pass long multiline content via `--content` on the command line to avoid shell quotation issues.*
   The CLI outputs the generated canonical cache file:
   `wp-cache/<connection>/posts/draft/<ID>-<sanitized-title>.md`
2. **Edit Canonical Cache File Directly**:
   Use file-writing/editing tools to write your Markdown body and metadata directly into that generated `.md` file.
3. **Push Changes**:
   ```bash
   wpai posts push <ID>
   ```
   The CLI syncs your content to WordPress and moves the file if status changed.

---

## 3. Strict Prohibitions (Never Do This)

1. **NO Throwaway / Temporary Markdown Files**:
   - **NEVER** create temporary files like `draft.md`, `temp.md`, or `article.md` in the repository root or project folders to pass via `--content-file`.
   - Always edit the canonical cache file in `wp-cache/` or use MCP tools.
2. **NEVER Move or Rename Files in `wp-cache/` Manually**:
   - Do NOT run `mv`, `rename`, or use file system tools to relocate files between `posts/draft/` and `posts/publish/`.
   - The CLI/MCP tool automatically manages file locations based on the YAML front-matter `status` field during `posts push <id>` and `posts organize`.
   - Manual moves corrupt the SQLite cache and lead to synchronization conflicts.

---

## 4. Editing Existing Posts

1. **Check/Inspect**: `wpai posts list` or `wpai posts get <id>`
2. **Refresh**: `wpai posts pull` (ensures local cache matches server state)
3. **Edit**: Modify `wp-cache/<connection>/posts/[status]/[ID]-[Title].md`
4. **Push**: `wpai posts push <ID>`
