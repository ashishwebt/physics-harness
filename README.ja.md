# Physics Harness

[English](./README.md)

Physics Harness は、ローカルの物理学教科書と研究型アシスタントを組み合わせたフルスタック AI 学習アプリです。バックエンドが会話 API を提供し、フロントエンドがその内容を対話的に表示します。

## このプロジェクトの目的

このリポジトリには次の要素が含まれています。

- `Chat.App.API` の .NET API
- `chat-app-ui` の React + Vite フロントエンド
- `book-injector` の教材取り込みツール
- `Chat.App.API/Physics` 配下のローカル物理テキスト

アプリは、構造化された物理学の学習内容を扱い、特定の章や節に関する質問に対して、ローカルの教材を参照しながら回答することを想定しています。

## 主な機能

- 会話の作成と管理
- ローカル物理教材に基づく質問応答
- 章・節ごとに整理された教科書本文の参照
- UI 上でのストリーミング応答
- SQLite による会話履歴保存
- 共同編集のためのアーティファクトファイル
- OpenStax 由来の教材を再生成する機能

## 含まれる教材

プロジェクトには OpenStax の物理学テキストを Markdown に変換した内容が含まれており、`Chat.App.API/Physics` に配置されています。

- `Chat.App.API/Physics/index.md` は目次
- 各章のフォルダにセクションごとの Markdown が格納
- `Chat.App.API/ATTRIBUTION.md` に出典とライセンス情報が記載

この教材は OpenStax のコンテンツをベースにしており、CC BY 4.0 で提供されています。周辺プロジェクトの MIT ライセンスとは別物です。

## 必要条件

- .NET SDK 10
- Node.js 20 以上の LTS
- バックエンド側の AI 設定（API キーやモデル名など）

## 起動手順

### 1) バックエンド設定

`Chat.App.API/appsettings.Development.json` または `Chat.App.API/appsettings.json` に AI 設定を追加します。

```json
"Agent": {
  "ApiKey": "your-key",
  "Model": "your-model",
  "Name": "Physics-agent"
}
```

### 2) バックエンド起動

```bash
dotnet restore
cd Chat.App.API
dotnet run
```

ASP.NET Core を使って API が起動します。

### 3) フロントエンド起動

```bash
cd chat-app-ui
npm install
npm run dev
```

以下の URL で利用できます。

- http://localhost:5173

バックエンドは次の URL で確認できます。

- http://localhost:5081/swagger
- http://localhost:5081/api/health

### 4) アプリを開く

ブラウザでフロントエンドを開いて新しい会話を開始します。

## 教材の再生成

OpenStax の元データから Markdown 一式を更新したい場合は次を実行します。

```bash
dotnet run --project book-injector/book-injector.csproj
```

この処理で教材のダウンロード、変換、章別ファイル生成、目次と出典ファイルの更新が行われます。

## ディレクトリ構成

```text
physics-harness/
├─ Chat.App.API/
│  ├─ AgentServices/
│  ├─ Controllers/
│  ├─ Database/
│  ├─ Models/
│  ├─ Physics/
│  ├─ appsettings.json
│  ├─ appsettings.Development.json
│  ├─ ATTRIBUTION.md
│  ├─ Program.cs
│  └─ ...
├─ chat-app-ui/
│  ├─ src/
│  ├─ package.json
│  ├─ vite.config.js
│  └─ ...
├─ book-injector/
│  └─ Program.cs
├─ Chat.App.sln
├─ LICENSE
├─ README.md
├─ README.ja.md
└─ ...
```

## 設定メモ

バックエンドは次の設定ファイルを読み込みます。

- `Chat.App.API/appsettings.json`
- `Chat.App.API/appsettings.Development.json`

主な項目:

- `ConnectionStrings:Default`
- `ConnectionStrings:ConnectionStringMessages`
- `Agent:ApiKey`
- `Agent:Model`
- `Agent:Name`
- `Agent:SystemPrompt`
- `Cors:AllowedOrigins`

## 主要 API

- `GET /api/health`
- `GET /api/conversations`
- `PATCH /api/conversations/{id}`
- `DELETE /api/conversations/{id}`
- `POST /api/chat`
- `GET /api/chat/{conversationId}`

## 補足

- フロントエンドは `/api/...` を前提にしており、ローカル開発では Vite のプロキシで連携します。
- 会話履歴とメッセージは SQLite に保存されます。
- 物理教材はローカルに保持され、AI がその内容を参照して回答する設計になっています。

---

[英語版 README に戻る](README.md)
