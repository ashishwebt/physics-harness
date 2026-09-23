# Chat.App Frontend

[English](./README.md)

Chat.App プロジェクト向けの React + Vite + Tailwind ベースのチャット UI です。会話サイドバー、ストリーミング応答表示、Markdown 表示、テーマ切り替え、会話の CRUD 操作を備えています。

## 機能

- 会話一覧の作成・名前変更・削除
- バックエンドからの応答ストリーミング表示
- 各会話の履歴取得
- Markdown 表示とコピー機能
- ライト/ダークテーマの保存
- API のヘルスステータス表示
- モバイル向けのサイドバーと入力レイアウト

## 前提条件

- Node.js 20 以上の LTS
- ローカルでバックエンド API が起動していること

## セットアップ

プロジェクトのルートから実行:

```bash
cd chat-app-ui
npm install
npm run dev
```

開発サーバーは http://localhost:5173 で起動し、`/api` 配下のリクエストをバックエンドの http://localhost:5081 にプロキシします。

## スクリプト

```bash
npm run dev     # Vite 開発サーバーを起動
npm run build   # dist/ に本番ビルドを生成
npm run preview # 本番ビルドをローカルでプレビュー
```

## API 連携

UI は以下の API を呼び出します。

- `GET /api/health` – 監視チェック
- `GET /api/conversations` – 会話一覧取得
- `PATCH /api/conversations/{id}` – 会話名変更
- `DELETE /api/conversations/{id}` – 会話削除
- `POST /api/chat` – 新しいメッセージ送信
- `GET /api/chat/{id}` – 会話詳細と履歴取得

## プロジェクト構成

```text
src/
  api/
    client.js             API クライアントとエラー処理
  components/
    App.jsx               top-level app shell
    ChatWindow.jsx        メッセージ一覧とストリーミング UI
    ConversationItem.jsx  会話リスト行の表示と操作
    ConfirmModal.jsx      削除確認ダイアログ
    MessageBubble.jsx     メッセージ表示、Markdown、コピー機能
    MessageInput.jsx      入力フォームと送信処理
    Sidebar.jsx           会話一覧とナビゲーション
    TopBar.jsx            タイトル、ヘルス状態、テーマ切替
  context/
    ToastContext.jsx      トースト通知
  hooks/
    useConversations.js   会話状態と API 操作
    useHealth.js           ヘルスチェックのポーリング
    useTheme.js            テーマ状態の管理
  index.css               Tailwind とテーマスタイル
  main.jsx                エントリーポイント
```

---

[元の README に戻る](README.md)
