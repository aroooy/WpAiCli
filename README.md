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
  - `wpai connections add --name <名称> --base-url <URL> --token <Bearer> [--cache-path <PATH>] [--sync-limit <NUMBER>]`
- `update <name>`: 既存の接続情報を更新します。
  - `wpai connections update "BlogName" --cache-path ./new-cache --sync-limit 50`
- `remove`: 対話形式で既存の接続を削除します。

### 投稿 (`posts`)
- `sync`: ローカルキャッシュとサーバー上の投稿を双方向で同期します。
  - `wpai posts sync`
- `list`: 投稿を一覧表示します。
  - `wpai posts list [--status <STATUS>] [--per-page <NUM>] [--page <NUM>]`
- `get <id>`: 指定したIDの投稿を1件取得します。
  - `wpai posts get 123`
- `create`: 新しい投稿を作成します。
  - `wpai posts create --title <TITLE> [--content <CONTENT> | --content-file <PATH>] [--status <STATUS>] [--categories <IDs>] [--tags <IDs>] [--featured-media <ID>]`
- `update <id>`: 既存の投稿を更新します。
  - `wpai posts update 123 [--title <TITLE>] [--content <CONTENT> | --content-file <PATH>] [--status <STATUS>] [--categories <IDs>] [--tags <IDs>] [--featured-media <ID>]`
- `delete <id>`: 投稿を削除します。
  - `wpai posts delete 123 [--force]`
- `revisions <id>`: 指定した投稿のリビジョン一覧を取得します。
  - `wpai posts revisions 123`
- `revision <post-id> <revision-id>`: 特定のリビジョンの詳細を取得します。
  - `wpai posts revision 123 456`

### カテゴリ (`categories`)
- `list`: カテゴリを一覧表示します。
- `get <id>`: 指定したIDのカテゴリを1件取得します。
- `delete <id>`: カテゴリを削除します。

### タグ (`tags`)
- `list`: タグを一覧表示します。
- `create`: 新しいタグを作成します。
  - `wpai tags create --name <NAME> [--slug <SLUG>] [--description <DESC>]`
- `get <id>`: 指定したIDのタグを1件取得します。
- `delete <id>`: タグを削除します。

### メディア (`media`)
- `list`: メディアライブラリの項目を一覧表示します。
  - `wpai media list [--per-page <NUM>] [--page <NUM>]`
- `upload <file-path>`: ファイルをメディアライブラリにアップロードします。
  - `wpai media upload <PATH> [--title <TITLE>] [--description <DESC>]`

## 同期機能
`posts sync` コマンドは、ローカルのファイルシステムとWordPressサーバー上の投稿を双方向で同期する機能です。

### 設定
同期を有効にするには、まず接続情報にキャッシュディレクトリのパスを設定する必要があります。
```
# 新規接続時に設定
wpai connections add --name "MyBlog" --base-url <URL> --token <TOKEN> --cache-path ./my-blog-cache

# 既存の接続を更新
wpai connections update "MyBlog" --cache-path ./my-blog-cache
```

### 同期の実行
設定後、`posts sync` を実行すると同期が開始されます。
```
wpai posts sync
```
- `posts list` を実行すると、取得した投稿が自動的にキャッシュされます。
- ローカルで `_content.md` や `_editable.yaml` ファイルを編集してから `posts sync` を実行すると、変更がサーバーにプッシュされます。
- サーバー側で投稿が変更された場合、`posts sync` を実行するとローカルのファイルが更新されます。
- ローカルとサーバーの両方で同じ投稿が変更されていた場合、コンフリクト（競合）が検出され、安全のためその投稿の同期はスキップされます。

### ローカルで編集可能なファイル
キャッシュディレクトリ内の各投稿は、複数のファイルで構成されています。このうち、ユーザーが編集するのは以下の2つです。

- `[ID]-[slug]_content.md`: 投稿の本文です。Markdown形式で自由に編集できます。
- `[ID]-[slug]_editable.yaml`: 投稿のメタデータを管理します。このファイルを編集することで、以下の項目を変更できます。
    - `title`: 投稿のタイトル
    - `slug`: URLスラッグ
    - `status`: `publish` (公開), `draft` (下書き), `pending` (レビュー待ち), `future` (予約投稿) など
    - `date`: 投稿日時 (ISO 8601形式: `YYYY-MM-DDTHH:MM:SS`)。`status: future` と組み合わせることで予約投稿が可能です。
    - `excerpt`: 投稿の抜粋
    - `featured_media`: アイキャッチ画像のメディアID
    - `comment_status`: コメントの受付状態 (`open` または `closed`)
    - `ping_status`: ピンバック/トラックバックの受付状態 (`open` または `closed`)

**注意:** `_editable.yaml` から項目（例: `slug:` の行）を削除した場合、その項目は**更新対象から外れる**だけで、サーバー上の値が空になるわけではありません。値を空にしたい場合は `slug: ''` のように明示的に空の値を設定してください。

**注意:** 現在のバージョンでは、ローカルで新しいファイルを作成して `posts sync` を実行しても、サーバーに新規投稿として作成することはできません。新規投稿は `posts create` コマンドを使用してください。

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

