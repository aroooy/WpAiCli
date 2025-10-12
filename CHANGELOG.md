# WpAiCli 変更履歴（作業まとめ）

このドキュメントは、今回一連の作業で加えた主な変更点を、運用面が分かる粒度で整理したものです。各項目には、目的・振る舞い・関係ファイル/コマンドを記載しています。

## 概要（ハイライト）
- posts create 実行直後にローカルキャッシュを自動生成
- posts update を廃止し、posts push <id> を新設（単一投稿をキャッシュから一括反映）
- delete（posts/categories/tags/media）成功時、および 404（既にサーバーに存在しない）時にローカルキャッシュを即時削除
- posts sync／media sync で、Top-N（同期上限）外・ローカル未編集かつサーバー 404 の項目を自動でローカル削除
- 同等の体験を categories/tags/media にも拡張（create の自動キャッシュ化、push <id> の追加、media upload の即時キャッシュ）
- 警告 CS8602 の解消（ローカルメタの非 null 確定代入）

---

## 変更の詳細

### 1) 投稿（posts）
- create 直後にローカルキャッシュを自動生成
  - 目的: 新規作成→すぐ編集・push 可能にするため
  - 影響: `posts/<ID>-*_content.md`, `posts/<ID>-*_editable.yaml` を即時作成
  - ファイル: Program.cs（posts create ハンドラで `cacheService.SavePostToCache` を呼び出し）

- 単一投稿の一括反映: posts push <id>
  - 目的: フィールド指定不要で、キャッシュ（MD+YAML）を“そのまま”サーバー反映
  - 仕様:
    - editMode と接続設定の MarkdownConversion を尊重
    - カテゴリ/タグは名称/スラッグ→ID 解決、解決不可ならエラー
    - 成功後はサーバー応答でキャッシュも更新
  - ファイル: Program.cs（posts push コマンド追加）、Services/SyncService.cs（`PushPostAsync` 新設）

- delete 時のローカルキャッシュ削除
  - 目的: コマンド削除とキャッシュの状態を一致させる
  - 仕様: サーバー削除成功時は即時削除、サーバー 404（既に削除済み）の場合もローカル削除
  - ファイル: Program.cs（posts delete の try/catch で 404 をハンドリング）

- 同期時のクリーンアップ
  - 目的: ローカル未編集でサーバーに無い（404）古い項目を自動削除
  - 仕様: Top-N 外の投稿に対し、未編集（ハッシュ一致）かつサーバー 404 → ローカル削除
  - ファイル: Services/SyncService.cs（`SynchronizePostsAsync` 内の分岐強化）

- フィールド単位の update を廃止
  - 目的: 操作性と整合性（競合判定の単純化）を優先し、push に一本化
  - ファイル: Program.cs（posts update を削除）

- CS8602（null 参照の可能性）警告の解消
  - 仕様: `localEditableMeta ?? throw` による非 null 変数 `meta` へ確定代入
  - ファイル: Services/SyncService.cs

### 2) カテゴリ（categories）/ タグ（tags）
- create 直後にローカルキャッシュを自動生成
  - 仕様: `SaveCategoryToCache` / `SaveTagToCache` を呼び出し
  - ファイル: Program.cs（各 create ハンドラ）

- 単一反映コマンドの追加: categories push <id> / tags push <id>
  - 仕様: ローカル YAML（name/slug/description）を一括でサーバーに適用し、ハッシュを更新
  - ファイル: Program.cs（push サブコマンド追加）、Services/SyncService.cs（`PushCategoryAsync` / `PushTagAsync` 新設）

- delete 時のローカルキャッシュ削除
  - 仕様: サーバー削除成功時は即時削除、404 時もローカル削除
  - ファイル: Program.cs（categories/tags delete ハンドラで 404 をハンドリング）

### 3) メディア（media）
- upload 直後にローカルキャッシュを自動生成
  - 仕様: アップロード結果の `SourceUrl` からバイナリをダウンロードし、YAML メタと合わせて保存
  - ファイル: Program.cs（media upload ハンドラで `DownloadMediaFileAsync` → `SaveMediaToCache`）

- 単一反映コマンドの追加: media push <id>
  - 仕様: ローカル YAML のメタ（title/alt_text/caption/description）を一括でサーバー更新（バイナリは対象外）
  - ファイル: Program.cs（push サブコマンド追加）、Services/SyncService.cs（`PushMediaAsync` 新設）、Services/CacheService.cs（`UpdateMediaMetadataOnly` 追加）

- delete 時のローカルキャッシュ削除
  - 仕様: サーバー削除成功時は即時削除、404 時もローカル削除
  - ファイル: Program.cs（media delete の 404 ハンドリング）

- 同期時のクリーンアップ
  - 仕様: Top-N 外・未編集かつサーバー 404 のメディアはローカル削除
  - ファイル: Services/SyncService.cs（`SynchronizeMediaAsync` の後段で検査）

### 4) README.md に関する補足
- 一度 UTF-8 化と記述整理を実施したが、元の詳細な説明が失われる懸念があったため、変更前の版にリバート済み
- 今後、上記新仕様（push 追加/自動キャッシュ/削除時のキャッシュクリーン/同期クリーンアップ）を元の文面にマージする予定

---

## 追加された/更新された主なメソッドとコマンド
- Services/SyncService.cs
  - `PushPostAsync(int id, ConnectionProfile, CancellationToken)`
  - `PushCategoryAsync(int id, CancellationToken)`
  - `PushTagAsync(int id, CancellationToken)`
  - `PushMediaAsync(int id, CancellationToken)`
- Services/CacheService.cs
  - `UpdateMediaMetadataOnly(WordPressMedia media)` を追加
  - 既存の `SavePostToCache/SaveCategoryToCache/SaveTagToCache/SaveMediaToCache` を create/upload の経路で活用
- Program.cs（コマンド）
  - posts: `create`（即時キャッシュ）、`push <id>`（新設）、`delete`（404 時もキャッシュ削除）
  - categories/tags: `create`（即時キャッシュ）、`push <id>`（新設）、`delete`（404 時もキャッシュ削除）
  - media: `upload`（即時キャッシュ）、`push <id>`（新設）、`delete`（404 時もキャッシュ削除）

---

## 既知の注意点/設計メモ
- Top-N 外の存在確認は個別 GET を行うため、対象が非常に多い環境では API 負荷が増える可能性があります
  - 改善案: include クエリの活用や 1 回あたりの検査上限の設定導入
- YAML で「未記載」の項目は更新対象外とし、空文字は「明示的に空にする」意思表示として扱います
- 単一 push に集約することで、競合判定の複雑化（フィールド単位差分管理）の回避を優先しています

---

## 関連コミット（抜粋）
- Fix CS8602: ensure non-null meta in SyncService when pushing local changes
- Delete local cache files on post delete when server deletion succeeds
- posts delete: also remove local cache on 404 NotFound
- posts sync: remove local cache if server post is 404 and local is unmodified
- Extend delete + sync behaviors: remove local cache for categories/tags/media on delete and on 404; media sync cleans unmodified local items not in top-N if server deleted
- posts create: immediately write cache for new post
- Add posts push <id>; remove posts update; add SyncService.PushPostAsync
- Rewrite README.md in UTF-8（のちにリバート）
- feat: Auto-cache on create for categories/tags/media; add push <id> for categories/tags/media; add CacheService.UpdateMediaMetadataOnly; immediate cache on media upload

---

このファイルはソリューション直下に配置されています（CHANGELOG.md）。
