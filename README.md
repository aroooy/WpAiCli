# WpAiCli Usage Guide

## 概要
WpAiCli は WordPress REST API と連携するためのクロスプラットフォーム CLI です。記事やカテゴリ、タグの管理に加えて、複数サイトの接続情報を安全に切り替えながら利用できます。

主な特徴:
- Windows Credential Manager に Bearer トークンを保存
- 接続プロファイルの登録 / 一覧 / 削除を CLI から実行
- 投稿・カテゴリ・タグ・メディア取得の各コマンドをサポート
- `--format table|json|raw` で出力形式を切り替え

## 初期設定
以下の手順をおすすめします。

### 1. 接続情報を登録
```
wpai connections add --name "BlogName" --base-url "https://example.com/?rest_route=/wp/v2/" --token <BearerToken>
```
- `--name`: 任意の表示名 (接続切り替え時に使用)
- `--base-url`: WordPress REST API のベース URL (`?rest_route=/wp/v2/` 形式がおすすめ)
- `--token`: WordPress で発行した Bearer トークン。Credential Manager に安全に保存されます。

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
## 機械連携向けのヒント
AI など機械連携では JSON モード (\--format json\) を推奨します。テキスト出力よりもエンコーディング／解析面で扱いやすく、文字化けも避けられます。


### 接続管理 (`connections`)
- `wpai connections list`
  - 登録済み接続の一覧を表示します。
- `wpai connections add --name <名称> --base-url <URL> --token <Bearer>`
  - 新しい接続を登録します。既存名と重複する場合は拒否されます。
- `wpai connections remove`
  - 接続一覧から削除対象を番号で選択→削除確認(`y`)→Credential Manager と設定ファイル両方から削除します。

接続情報は実行モジュールと同じフォルダの `connections.json` に保存され、Bearer トークンは Windows Credential Manager (`WpAiCli/<名称>`) に格納されます。

### 投稿 (`posts`)
- `list`: `wpai posts list --status publish --format json`
- `get`: `wpai posts get --id 123`
- `create`: `wpai posts create --title "Title" --content-file ./body.txt --status draft`
- `update`: `wpai posts update 123 --title "New" --status publish`
- `delete`: `wpai posts delete 123 --force false`

### カテゴリ (`categories`)
- `wpai categories list --format table`

### タグ (`tags`)
- `wpai tags list --format json`

## 出力形式
`--format table|json|raw` で切り替え可能です。省略時は `table`。

## ドキュメント表示
`wpai docs` または `wpai --help` で、実行フォルダの HOWTO.md が表示されます。

## トラブルシューティング
- 「No connections registered」: `wpai connections add` で接続を登録してください。
- `rest_forbidden_context` などの 401 エラー: トークンに必要な権限が無い、または期限切れです。新しいトークンで接続を再登録してください。
- `wpai posts list` で複数接続があり指定がない場合: `--connection <name>` で明示指定してください。

## 補完スクリプト
```
wpai completion --shell bash       > /etc/bash_completion.d/wpai
wpai completion --shell zsh        > ~/.zfunc/_wpai
wpai completion --shell powershell | Out-String | Invoke-Expression
```
対応シェル: bash / zsh / PowerShell。

---
本ファイルは `dotnet build` 時に出力ディレクトリへコピーされます。
