# Chat.App

[English](./README.md)

.NET 10 バックエンドと React + Vite フロントエンドで構成されたフルスタック AI チャットアプリケーションです。会話の作成・管理、AI エージェントへのメッセージ送信、ストリーミング応答の表示ができます。

## 概要

- バックエンド: `Chat.App.API`
  - ASP.NET Core Web API
  - SQLite による会話と履歴の保存
  - AI エージェント連携によるストリーミング応答
  - ヘルスチェックと会話 API

- フロントエンド: `chat-app-ui`
  - React + Vite
  - Tailwind CSS UI
  - 会話サイドバーとチャットウィンドウ
  - ライト/ダークテーマ切り替え
  - Markdown 表示とトースト通知

## 主な機能

- チャット会話の作成・管理
- 会話の名前変更と削除
- AI エージェントへのメッセージ送信
- リアルタイムでの応答ストリーミング
- 会話ごとの履歴取得
- SQLite による永続化
- デスクトップとモバイルに対応した UI

## 必要条件

- .NET SDK 10
- Node.js 20 以上の LTS
- バックエンド側の AI プロバイダー設定（API キーやモデル設定）

## 起動方法

### 1) バックエンド起動

```bash
dotnet restore
cd Chat.App.API
dotnet run
```

### 2) フロントエンド起動

```bash
cd chat-app-ui
npm install
npm run dev
```

### 3) アプリを開く

- フロントエンド: http://localhost:5173
- API ドキュメント: http://localhost:5081/swagger
- ヘルスチェック: http://localhost:5081/api/health

## VS Code デバッグ

ルートの `.vscode/launch.json` を使って、次の設定でデバッグできます。

- `Chat.App.API` – ASP.NET Core バックエンドを起動
- `React` – Vite フロントエンドを起動
- `fullstack` – API + フロントエンドを同時起動

## 主要 API

- `GET /api/health`
- `GET /api/conversations`
- `PATCH /api/conversations/{id}`
- `DELETE /api/conversations/{id}`
- `POST /api/chat`
- `GET /api/chat/{conversationId}`

---

[元の README に戻る](README.md)
