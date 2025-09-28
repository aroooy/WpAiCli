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
- `delete <id>`: カテゴリを削除します。

### タグ (`tags`)
- `list`: タグを一覧表示します。
- `create`: 新しいタグを作成します。
  - `wpai tags create --name "New Tag" --description "..."`
- `get <id>`: 指定したIDのタグを1件取得します。
- `delete <id>`: タグを削除します。

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

---
本ファイルは `dotnet build` 時に出力ディレクトリへコピーされます。