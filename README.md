# WpAiCli 使用ガイド

## 概要
WpAiCli は WordPress REST API とやり取りするための CLI です。投稿/カテゴリ/タグ/メディアの作成・取得・更新・削除に加え、ローカルキャッシュとの同期をサポートします。

- Windows 資格情報マネージャに Bearer トークンを保存
- ローカルキャッシュ（Markdown/YAML + SQLite）とサーバーの双方向同期
- 単一投稿のキャッシュ反映（push）
- 競合検出と手動解決

## グローバルオプション
- `--connection <name>` 接続プロファイルの選択
- `--version` / `-V` バージョン表示
- `--help` / `-h` ヘルプ表示

## はじめに（接続の登録）
```
wpai connections add --name "MyBlog" --base-url "https://example.com/?rest_route=/wp/v2" --token <BearerToken> --cache-path ./wp-cache
```
- `--sync-limit <NUMBER>` 同期時にチェックする最大件数（既定 30）
- `--markdown-conversion <client|server>` Markdown→HTML 変換の実行場所（既定 client）

## 投稿（posts）
- `sync` ローカルキャッシュと投稿を双方向同期
  - `wpai posts sync`
- `list` 投稿一覧
  - `wpai posts list [--status <STATUS>] [--per-page <NUM>] [--page <NUM>]`
- `get <id>` 投稿取得
  - `wpai posts get 123`
- `create` 新規作成（作成直後にキャッシュへも保存されます）
  - `wpai posts create --title <TITLE> [--content <TEXT> | --content-file <PATH>] [--status <STATUS>] [--edit-mode <markdown|html>] [--categories <IDs>] [--tags <IDs>] [--featured-media <ID>]`
- `push <id>` 単一投稿のキャッシュ（`_content.md` + `_editable.yaml`）をそのままサーバーへ反映
  - `wpai posts push 123`
- `delete <id>` 削除（成功時はローカルキャッシュも即時削除、サーバー404でもキャッシュ削除）
  - `wpai posts delete 123 [--force]`
- `revisions <id>` リビジョン一覧
  - `wpai posts revisions 123`
- `revision <post-id> <revision-id>` 指定リビジョン取得
  - `wpai posts revision 123 456`

## カテゴリ（categories）
- `list` / `get <id>` / `create` / `update <id>` / `delete <id>` をサポート
- `delete` は成功時/404時にローカルキャッシュ（`categories/<ID>-*.yaml`）も即時削除

## タグ（tags）
- `list` / `get <id>` / `create` / `update <id>` / `delete <id>` をサポート
- `delete` は成功時/404時にローカルキャッシュ（`tags/<ID>-*.yaml`）も即時削除

## メディア（media）
- `sync` ローカルのメタ（YAML）をプッシュ後、サーバーからメディア情報とファイルを取得
  - `wpai media sync`
- `list` / `upload <file>` / `delete <id>` をサポート
- `delete` は成功時/404時にローカルキャッシュ（`media/<ID>-*`）も即時削除

## 同期とキャッシュの仕様
- 個別コマンドは基本的にサーバーのみ変更し、キャッシュ反映は `posts sync` / `media sync` が担当
  - 例外: `posts create` は作成直後にキャッシュへ書き込み
  - 例外: `delete`（posts/categories/tags/media）は成功時/404時にキャッシュを即時削除
- Top-N（`--sync-limit`）外のローカル項目で、サーバーが 404 かつローカル未編集なら、同期時にローカルキャッシュを自動削除（投稿・メディア）
- ローカルで新規投稿ファイルを置くだけでは新規作成されません。新規作成は `posts create` を使用してください
- `posts push <id>` はローカルの `_content.md` + `_editable.yaml` を丸ごと適用します（editMode と MarkdownConversion を尊重）

## 競合の解決
- `wpai resolve post <id> --strategy <local-wins|server-wins>`
- 競合は同期レポートに対象IDが表示されます。必要に応じて個別解決してください

## キャッシュ構造（例）
```
wp-cache/
  <connection>/
    wp-ai-cache.db
    categories/
      <ID>-<Name>.yaml
    tags/
      <ID>-<Name>.yaml
    posts/
      <ID>-<slug>_content.md
      <ID>-<slug>_editable.yaml
    media/
      <ID>-<filename>.<ext>
      <ID>-<filename>.yaml
```
