# WpAiCli Usage Guide

## 概要
WpAiCli は WordPress REST API と連携するためのクロスプラットフォーム CLI です。投稿、カテゴリ、タグ、メディアの管理に加えて、複数サイトの接続情報を安全に切り替えながら利用できます。

主な特徴:
- Windows Credential Manager または macOS/Linux の Secret-Tool に Bearer トークンを保存
- 接続プロファイルの登録 / 一覧 / 削除を CLI から実行
- 投稿、カテゴリ、タグ、メディアの作成、取得、更新、削除の各コマンドをサポート
- 投稿リビジョンの取得にも対応
- メディア（画像など）のアップロード機能を搭載
- `--format table|json|raw` で出力形式を切り替え

## 初期設定
以下の手順をおすすめします。

### 1. 接続情報を登録
```
wpai connections add --name "BlogName" --base-url "https://example.com/?rest_route=/wp/v2/" --token <BearerToken>
```
- `--name`: 任意の表示名 (接続切り替え時に使用)
- `--base-url`: WordPress REST API のベース URL (`?rest_route=/wp/v2/` 形式がおすすめ)
- `--token`: WordPress で発行した Bearer トークン。OSの資格情報ストアに安全に保存されます。

### 2. 接続の確認
```
wpai connections list
```
登録済みプロファイルが番号付きで表示され、`*` が最後に利用した接続を示します。

### 3. 投稿一覧を取得
```
wpai posts list --status publish --format table
```
`--connection <name>` を付けると特定の接続を直接指定できます。省略時は最後に使用した接続が利用されます。

## コマンド一覧
AI など機械連携では JSON モード (`--format json`) を推奨します。テキスト出力よりもエンコーディング／解析面で扱いやすく、文字化けも避けられます。

### 接続管理 (`connections`)
- `list`: 登録済み接続の一覧を表示します。
- `add`: 新しい接続を登録します。
  - `wpai connections add --name <名称> --base-url <URL> --token <Bearer>`
- `remove`: 対話形式で既存の接続を削除します。

### 投稿 (`posts`)
- `list`: 投稿を一覧表示します。
  - `wpai posts list --status publish --format json`
- `get <id>`: 指定したIDの投稿を1件取得します。
  - `wpai posts get 123`
- `create`: 新しい投稿を作成します。
  - `wpai posts create --title "Title" --content-file ./body.txt --status draft`
- `update <id>`: 既存の投稿を更新します。
  - `wpai posts update 123 --title "New Title" --status publish`
- `delete <id>`: 投稿を削除します。
  - `wpai posts delete 123 --force false`
- `revisions <id>`: 指定した投稿のリビジョン一覧を取得します。
  - `wpai posts revisions 123`
- `revision <post-id> <revision-id>`: 特定のリビジョンの詳細を取得します。
  - `wpai posts revision 123 456`

### カテゴリ (`categories`)
- `list`: カテゴリを一覧表示します。
- `get <id>`: 指定したIDのカテゴリを1件取得します。
  - `wpai categories get 42`
- `delete <id>`: カテゴリを削除します。
  - `wpai categories delete 42`

### タグ (`tags`)
- `list`: タグを一覧表示します。
- `create`: 新しいタグを作成します。
  - `wpai tags create --name "New Tag" --description "..."`
- `get <id>`: 指定したIDのタグを1件取得します。
  - `wpai tags get 55`
- `delete <id>`: タグを削除します。
  - `wpai tags delete 55`

### メディア (`media`)
- `list`: メディアライブラリの項目を一覧表示します。
- `upload <file-path>`: ファイルをメディアライブラリにアップロードします。
  - `wpai media upload ./image.jpg --title "My Image"`

## 出力形式
`--format table|json|raw` で切り替え可能です。省略時は `table`。

## ドキュメント表示
`wpai docs` または `wpai --help` で、このREADMEファイルの内容が表示されます。

## トラブルシューティング
- 「No connections registered」: `wpai connections add` で接続を登録してください。
- `rest_forbidden_context` などの 401/403 エラー: トークンに必要な権限が無い、または期限切れです。新しいトークンで接続を再登録してください。
- `media upload` で「このファイルタイプをアップロードする権限がありません」エラー: WordPressのセキュリティプラグインやテーマ、マルチサイト設定などで、アップロード可能なファイルの種類が制限されている可能性があります。
- **Gemini CLIでのリスト表示に関する注意:** Gemini CLI環境では、出力の表示領域に厳しい制限があるため、リスト形式で複数の項目を表示しようとすると、2件目以降が自動的に省略（トランケート）されてしまう場合があります。このため、AIアシスタントはリスト表示の際に、全項目を1行にまとめたプレーンテキスト形式で出力するよう最適化されています。これにより、一覧の全項目を確実に確認できます。

## 補完スクリプト
```
# PowerShell
wpai completion --shell powershell | Out-String | Invoke-Expression

# Bash
wpai completion --shell bash > /etc/bash_completion.d/wpai

# Zsh
wpai completion --shell zsh > ~/.zfunc/_wpai
```
対応シェル: bash / zsh / PowerShell。

## Tips: AIアシスタントとの連携
この `wpai` ツールをAIアシスタントと連携して使用する際の、推奨プロンプト（指示）のテンプレートです。セッションの最初にこの指示を与えることで、AIの動作を最適化し、ブログの執筆やメンテナンス作業をスムーズに進めることができます。

```
SYSTEM INSTRUCTION: `wpai` Tool Operation Protocol (Template)

1.0 Tool Identification
- This session concerns the CLI tool `wpai`.
- The tool's executable path and commands should be provided or discoverable by the AI agent.

2.0 Data Retrieval Protocol
- Rule 2.1 (Default Format): When executing any `wpai` command that retrieves data, ALWAYS append the `--format json` argument.
- Rationale: To ensure structured, complete, and unambiguous data is available for internal processing and subsequent tasks.

3.0 Data Presentation Protocol
- Rule 3.1 (No Raw Output): NEVER display the raw JSON output from the tool directly to the user.
- Rule 3.2 (Mandatory Formatting): ALWAYS parse the internal JSON data and present a summarized, human-readable version to the user (e.g., natural language, lists, tables).
- Rule 3.3 (Handling Large Content): When presenting an item with a significant text body (e.g., a post's content), DO NOT display the full content by default. Instead, provide a concise summary of the content. ALWAYS include the public link/URI for the item so the user can view the full details in a browser. The full content should only be displayed if explicitly requested by the user.

4.0 List Interaction Protocol
- Rule 4.1 (Indexing): When presenting a list of items, assign a 1-based serial number to each item.
- Rule 4.2 (Session Memory): Internally, map each serial number to its corresponding unique ID for the duration of the session.
- Rule 4.3 (Reference Resolution): If the user refers to an item by its serial number, use the session map to resolve the correct ID before executing a command.

5.0 Content & Maintenance Protocol

5.1 Content Generation Persona
- Tone: `[ここにブログの文体を指定。例: 専門的かつ簡潔に、親しみやすくユーモアを交えて]`
- Target Audience: `[ここにターゲット読者を指定。例: IT初心者、写真愛好家]`
- SEO: When focus keywords are provided, incorporate them naturally into the content.
- Default Workflow: When instructed to "write a new post" or "rewrite," first generate a complete draft including `title`, `content`, suggested `categories` (IDs), and suggested `tags` (names). Present this draft for user approval before executing `wpai posts create` or `update`.

5.2 Maintenance Tasks
- When given a general maintenance instruction (e.g., "Do some maintenance"), you may proactively propose and execute the following checks:
  - Check 1 (Missing Tags): Analyze all posts and list those with no associated tags.
  - Check 2 (Missing Featured Image): Analyze all posts and list those where `featured_media` is `0`.
  - Check 3 (Outdated Content): Analyze all posts and list those older than a user-specified duration (e.g., 2 years), suggesting them for a content refresh.
```

---
本ファイルは `dotnet build` 時に出力ディレクトリへコピーされます。
