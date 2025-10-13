# WpAiCli Usage Guide

## 概要
WpAiCli は WordPress REST API と連携するためのクロスプラットフォーム CLI です。投稿、カテゴリ、タグ、メディアの管理に加えて、複数サイトの接続情報を安全に切り替えながら利用できます。

主な特徴:
- Windows Credential Manager または macOS/Linux の Secret-Tool に Bearer トークンを保存
- 接続プロファイルの登録 / 一覧 / 削除 / 更新を CLI から実行
- 投稿、カテゴリ、タグ、メディアの作成、取得、更新、削除の各コマンドをサポート
- 投稿のローカルキャッシュと双方向同期に対応
- 投稿リビジョンの取得にも対応
- メディア（画像など）のアップロード機能を搭載
- `--format table|json|raw` で出力形式を切り替え

## グローバルオプション
- `--connection <name>`: 特定の接続プロファイルを指定してコマンドを実行します。
- `--version`, `-V`: バージョン情報を表示します。
- `--help`, `-h`: ヘルプを表示します。

## 初期設定
以下の手順をおすすめします。

### 1. 接続情報を登録
```
wpai connections add --name "BlogName" --base-url "https://example.com/?rest_route=/wp/v2/" --token <BearerToken>
```
- `--name`: 任意の表示名 (接続切り替え時に使用)
- `--base-url`: WordPress REST API のベース URL (`?rest_route=/wp/v2/` 形式がおすすめ)
- `--token`: WordPress で発行した Bearer トークン。OSの資格情報ストアに安全に保存されます。
- `--cache-path <PATH>`: (任意) 同期機能で利用するローカルキャッシュの保存先ディレクトリを指定します。
- `--sync-limit <NUMBER>`: (任意) 一度の同期でチェックする最大投稿数を指定します (デフォルト: 30)。
- `--markdown-conversion <client|server>`: (任意) MarkdownからHTMLへの変換をどこで行うかを指定します (デフォルト: `client`)。
  - `client`: CLIツール側で変換します。
  - `server`: サーバー側のプラグイン(Jetpackなど)での変換を期待し、Markdown原文をそのまま送信します。

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

## 一般的な使い方（ワークフロー例）

### 1. 新規投稿を作成し、公開する
1. `wpai posts create --title "新しい記事" --status draft` で下書きを作成します。
   - この時点でローカルにキャッシュファイルが自動生成されます。
2. 生成された `posts/123-new-article_content.md` と `_editable.yaml` をエディタで編集します。
3. `wpai posts push 123` を実行し、編集内容をサーバーに反映します。
4. 記事を公開するには、`_editable.yaml` の `status` を `publish` に変更し、再度 `wpai posts push 123` を実行します。

### 2. 既存の投稿を編集する
1. `wpai posts sync` を実行し、サーバーから最新の状態を取得します。
2. 編集したい記事のローカルファイルを編集します。
3. `wpai posts push <ID>` を実行し、変更をサーバーに反映します。

## コマンド一覧
AI など機械連携では JSON モード (`--format json`) を推奨します。テキスト出力よりもエンコーディング／解析面で扱いやすく、文字化けも避けられます。

### 接続管理 (`connections`)
- `list`: 登録済み接続の一覧を表示します。
- `add`: 新しい接続を登録します。
  - `wpai connections add --name <名称> --base-url <URL> --token <Bearer> [--cache-path <PATH>] [--sync-limit <NUMBER>] [--markdown-conversion <client|server>]`
- `update <name>`: 既存の接続情報を更新します。
  - `wpai connections update "BlogName" --cache-path ./new-cache --sync-limit 50 --markdown-conversion server`
- `remove`: 対話形式で既存の接続を削除します。

### 投稿 (`posts`)
- `sync`: ローカルキャッシュとサーバー上の投稿のみを双方向で同期します。 `[キャッシュへの影響: サーバー変更を反映]`
  - `wpai posts sync`
- `list`: 投稿を一覧表示します。 `[キャッシュへの影響: なし]`
  - `wpai posts list [--status <STATUS>] [--per-page <NUM>] [--page <NUM>]`
- `get <id>`: 指定したIDの投稿を1件取得します。 `[キャッシュへの影響: なし]`
  - `wpai posts get 123`
    - `create`: 新しい投稿を作成します。 `[キャッシュへの影響: 即時作成]`
      - `wpai posts create --title <TITLE> --content <CONTENT> | --content-file <PATH> [--status <STATUS>] [--edit-mode <markdown|html>] [--categories <IDs>] [--tags <IDs>] [--featured-media <ID>]`
      - **注意:** `--content` または `--content-file` は必須で、内容は空や空白のみにはできません。- `push <id>`: ローカルキャッシュの変更（本文、メタデータ）をサーバーに一括で反映（プッシュ）します。 `[キャッシュへの影響: サーバー反映後に更新]`
  - `wpai posts push 123`
- `delete <id>`: 投稿を削除します。 `[キャッシュへの影響: 即時削除]`
  - `wpai posts delete 123 [--force]`
- `revisions <id>`: 指定した投稿のリビジョン一覧を取得します。 `[キャッシュへの影響: なし]`
  - `wpai posts revisions 123`
- `revision <post-id> <revision-id>`: 特定のリビジョンの詳細を取得します。 `[キャッシュへの影響: なし]`
  - `wpai posts revision 123 456`

### カテゴリ (`categories`)
- `list`: カテゴリを一覧表示します。 `[キャッシュへの影響: なし]`
- `get <id>`: 指定したIDのカテゴリを1件取得します。 `[キャッシュへの影響: なし]`
- `create`: 新しいカテゴリを作成します。 `[キャッシュへの影響: 即時作成]`
  - `wpai categories create --name <NAME> [--slug <SLUG>] [--description <DESC>]`
- `push <id>`: ローカルキャッシュ（YAMLファイル）の変更をサーバーに反映（プッシュ）します。 `[キャッシュへの影響: サーバー反映後にハッシュのみ更新]`
  - `wpai categories push 45`
- `delete <id>`: カテゴリを削除します。 `[キャッシュへの影響: 即時削除]`

### タグ (`tags`)
- `list`: タグを一覧表示します。 `[キャッシュへの影響: なし]`
- `create`: 新しいタグを作成します。 `[キャッシュへの影響: 即時作成]`
  - `wpai tags create --name <NAME> [--slug <SLUG>] [--description <DESC>]`
- `get <id>`: 指定したIDのタグを1件取得します。 `[キャッシュへの影響: なし]`
- `push <id>`: ローカルキャッシュ（YAMLファイル）の変更をサーバーに反映（プッシュ）します。 `[キャッシュへの影響: サーバー反映後にハッシュのみ更新]`
  - `wpai tags push 67`
    - `delete <id>`: タグを削除します。 `[キャッシュへの影響: 即時削除]`

### タクソノミ (`taxonomies`)
- `sync`: ローカルキャッシュとサーバー上のすべてのカテゴリとタグを双方向で同期します。 `[キャッシュへの影響: サーバー変更を反映]`
  - `wpai taxonomies sync`

### メディア (`media`)- `sync`: ローカルキャッシュとサーバー上のメディアを同期します。 `[キャッシュへの影響: サーバー変更を反映]`
- `list`: メディアライブラリの項目を一覧表示します。 `[キャッシュへの影響: なし]`
  - `wpai media list [--per-page <NUM>] [--page <NUM>]`
- `upload <file-path>`: ファイルをメディアライブラリにアップロードします。 `[キャッシュへの影響: 即時作成]`
  - `wpai media upload <PATH> [--title <TITLE>] [--description <DESC>]`
- `push <id>`: ローカルキャッシュ（YAMLファイル）のメタデータ変更（タイトル、代替テキスト等）をサーバーに反映（プッシュ）します。 `[キャッシュへの影響: サーバー反映後にメタデータのみ更新]`
  - `wpai media push 89`
- `delete <id>`: メディアを削除します。 `[キャッシュへの影響: 即時削除]`
  - `wpai media delete 123 [--force]`

### 競合の解決 (`resolve`)
`posts sync` を実行した際に競合が検出された場合、このコマンドを使って手動で競合を解決します。

- `resolve <type> <id> --strategy <strategy>`: 競合を解決します。
  - `<type>`: 競合したコンテンツの種類 (`post`, `category`, `tag`)。
  - `<id>`: 競合したアイテムのID。
  - `--strategy <strategy>`: 必須。以下のいずれかの解決戦略を指定します。
    - `local-wins`: ローカルの変更を正とし、サーバーの状態をローカルの状態で上書きします。
    - `server-wins`: サーバーの変更を正とし、ローカルの状態をサーバーの状態で上書きします。

  **実行例:**
  ```bash
  # 投稿ID 123 の競合を、ローカルの変更を優先して解決
  wpai resolve post 123 --strategy local-wins

  # カテゴリID 45 の競合を、サーバーの変更を優先して解決
  wpai resolve category 45 --strategy server-wins
  ```

  **注意: Markdownの `server` 変換モードにおける競合検出**

  Markdownの変換設定 (`--markdown-conversion`) を `server` に設定している場合、同期の競合検出はサーバー上の `_md_source` という特別なメタフィールドへの変更に依存します。

  これは、WordPressの管理画面で通常のビジュアルエディタやコードエディタを使って投稿を編集しても、この `_md_source` フィールドは更新されないことを意味します。その結果、**管理画面からの編集はサーバー側の変更として検出されず、競合が発生しません。** ローカルの変更がサーバーの変更を上書きしてしまいます。

  `server` モードで正しく競合を検出させるには、サーバー側でもREST API経由で `_md_source` メタフィールドを更新する必要があります。

## 同期機能

本ツールにおける「同期」には、役割の異なる2つの主要なコマンドがあります。

- **`push <id>`**: **ローカル → サーバー**への単方向同期。指定した一つのアイテムのローカルでの変更（MarkdownやYAMLファイルの編集結果）をサーバーに反映します。
- **`sync`**: **サーバー ⇔ ローカル**の双方向同期。主にサーバー上の変更をローカルに反映（プル）し、ローカルとサーバーの差分を検出します。`posts sync`, `media sync`, `taxonomies sync` のように、対象リソースごとにコマンドが分かれています。

`posts sync` および `media sync` コマンドは、ローカルのファイルシステムとWordPressサーバー上のコンテンツ（投稿、メディア、カテゴリ、タグ）を同期する機能です。

### 設定

同期を有効にするには、まず接続情報にキャッシュディレクトリのパスを設定する必要があります。
```
# 新規接続時に設定
wpai connections add --name "MyBlog" --base-url <URL> --token <TOKEN> --cache-path ./my-blog-cache

# 既存の接続を更新
wpai connections update "MyBlog" --cache-path ./my-blog-cache
```

### 同期の実行とキャッシュディレクトリ構造

設定後、`posts sync` または `media sync` を実行すると同期が開始されます。キャッシュディレクトリの基本的な構造は以下の通りです。

1.  **接続ごとのサブディレクトリ**: `--cache-path` で指定したルートディレクトリ内に、接続プロファイル名のサブディレクトリが作成されます (例: `wp-cache/my-blog/`)。これにより、複数のブログのキャッシュが互いに干渉することなく管理されます。
2.  **キャッシュファイルの生成**: 各接続のサブディレクトリ内に、以下のファイルとディレクトリが生成されます。

    -   `wp-ai-cache.db`: コンテンツのメタ情報を管理するSQLiteデータベースファイルです。このファイルはアプリケーションが内部的に使用します。**ユーザーが直接編集しないでください。**
    -   `categories/` ディレクトリ: **編集可能な**カテゴリのYAMLファイルが個別に保存されます (`[ID]-[名前].yaml`)。
    -   `tags/` ディレクトリ: **編集可能な**タグのYAMLファイルが個別に保存されます (`[ID]-[名前].yaml`)。
    -   `posts/` ディレクトリ:
        -   `[ID]-[slug]_content.md`: 編集可能な投稿の本文です。
        -   `[ID]-[slug]_editable.yaml`: 編集可能な投稿のメタデータです。
    -   `media/` ディレクトリ:
        -   `[ID]-[ファイル名].[拡張子]`: メディアファイルの本体です。
        -   `[ID]-[ファイル名].yaml`: 編集可能なメディアのメタデータです。

### 同期のルール

- **投稿とタクソノミ:** `posts sync` を実行すると、まずローカルの `categories/` と `tags/` ディレクトリ内にあるYAMLファイルの変更（名前やスラッグの編集）がサーバーにプッシュされます。その後、サーバーから最新の投稿とタクソノミーの情報が取得され、ローカルのファイル (`.md`, `.yaml`) とデータベース (`cache.db`) が更新されます。
- **メディア:** `media sync` を実行すると、まずローカルの `media/` ディレクトリ内にあるYAMLファイルの変更（タイトル、代替テキスト、キャプション、説明）がサーバーにプッシュされます。その後、サーバーからメディアの情報とファイル本体がダウンロードされ、ローカルキャッシュが更新されます。
- ローカルで `posts/` ディレクトリ内の投稿ファイルを編集してから `posts sync` を実行すると、変更がサーバーにプッシュされます。
- サーバー側で投稿が変更された場合、`posts sync` を実行するとローカルのファイルが更新されます。
- ローカルとサーバーの両方で同じ投稿が変更されていた場合、コンフリクト（競合）が検出され、安全のためその同期はスキップされます。レポートに表示される案内に従って `resolve` コマンドで手動解決が必要です。
- **キャッシュの自動クリーンアップ:** `posts sync` や `media sync` を実行した際、同期対象の上限（`--sync-limit`）に含まれない古いキャッシュファイルが、サーバー上で既に削除されており（404 Not Found）、かつローカルでも変更されていない場合、そのローカルキャッシュファイルは自動的に削除されます。

- **コマンド実行とキャッシュの自動更新:**
      - `create` (posts, categories, tags) や `upload` (media) を実行すると、成功と同時にローカルキャッシュが **自動的に作成されます**。これにより、作成後すぐにローカルで編集を開始し、`push` コマンドで変更を反映できます。
      - `delete` (posts, categories, tags, media) を実行すると、サーバーでの削除成功時にローカルキャッシュも **自動的に削除されます**。サーバー上で対象が既に存在しない（404）場合も、ローカルキャッシュはクリーンアップされます。
      - ローカルファイルの編集内容をサーバーに反映するには `push <id>` を、サーバー側の変更をローカルに取り込むには `sync` を使用します。

### ローカルで編集可能なファイル

ユーザーが直接編集するのは以下のファイルです。

- `categories/[ID]-[名前].yaml`: カテゴリの `name`, `slug`, `description` を変更できます。
  - **新規作成:** このディレクトリに新しいYAMLファイルを追加し（`id`は`0`か未指定）、`posts sync`を実行すると、サーバーに新しいカテゴリが作成されます。
  - **削除:** このファイルを削除しても、サーバー上のカテゴリは削除されません。削除は `categories delete <id>` コマンドを使用してください。
- `tags/[ID]-[名前].yaml`: タグの `name`, `slug`, `description` を変更できます。
  - **新規作成:** `categories` と同様の手順で新しいタグを作成できます。
  - **削除:** このファイルを削除しても、サーバー上のタグは削除されません。削除は `tags delete <id>` コマンドを使用してください。
- `media/[ID]-[ファイル名].yaml`: メディアの `title`, `alt_text`, `caption`, `description` を変更できます。
- `posts/[ID]-[slug]_content.md`: 投稿の本文。
- `posts/[ID]-[slug]_editable.yaml`: 投稿のメタデータ。このファイルを編集することで、以下の項目を変更できます。
    - `title`, `slug`, `status`, `date`, `excerpt` など。
    - `editMode`: `markdown` または `html` を指定します。`_content.md` ファイルの内容をどちらとして扱うかを `posts sync` 時に決定します。
      - `posts sync` で初めて投稿をキャッシュする際、サーバーの `_md_source` カスタムフィールドの有無に応じて自動設定されます。
      - この値を `html` から `markdown` に変更すると、次回 `posts sync` 時に `_content.md` の内容がMarkdownとして扱われます。
    - `categories` や `tags` には、IDだけでなく、`categories/` や `tags/` ディレクトリ内に存在する名前やスラッグで指定できます。
    - **注意:** ローカルで新しい投稿ファイルセットを作成して `posts sync` を実行しても、サーバーに新規投稿として作成することはできません。新規投稿は `posts create` コマンドを使用してください。

**注意:** `_editable.yaml` から項目（例: `slug:` の行）を削除した場合、その項目は**更新対象から外れる**だけで、サーバー上の値が空になるわけではありません。値を空にしたい場合は `slug: ''` のように明示的に空の値を設定してください。

## 出力形式
`--format table|json|raw` で切り替え可能です。省略時は `table`。

## ドキュメント表示
`wpai docs` または `wpai --help` で、このREADMEファイルの内容が表示されます。

## トラブルシューティング
- 「No connections registered」: `wpai connections add` で接続を登録してください。
- `rest_forbidden_context` などの 401/403 エラー: トークンに必要な権限が無い、または期限切れです。新しいトークンで接続を再登録してください。
- `media upload` で「このファイルタイプをアップロードする権限がありません」エラー: WordPressのセキュリティプラグインやテーマ、マルチサイト設定などで、アップロード可能なファイルの種類が制限されている可能性があります。
- `posts sync` で「Cache path is not configured」エラー: `wpai connections update <name> --cache-path <PATH>` でキャッシュディレクトリを設定してください。

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

