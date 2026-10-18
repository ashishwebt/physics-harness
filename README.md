# Physics Harness

[日本語](./README.ja.md)

Physics Harness is a full-stack AI study app built around a local physics textbook and a research-focused assistant. The backend serves conversation APIs and the frontend provides a chat interface for asking questions grounded in the project’s physics content.

## What this project does

This repo combines:

- a .NET API in `Chat.App.API`
- a React + Vite frontend in `chat-app-ui`
- a book ingestion tool in `book-injector`
- local physics content under `Chat.App.API/Physics`

The app is designed to help users explore a structured physics curriculum, ask questions about specific sections, and use an AI assistant that can reference the local textbook material while answering.

## Core features

- Create and manage chat conversations
- Ask questions grounded in the local physics book
- Read and cite textbook sections from the included markdown content
- Stream AI responses in the UI
- Keep conversation history in SQLite
- Work with artifact files for collaborative drafting or notes
- Regenerate the textbook corpus from OpenStax source material

## Included content

The project includes a converted OpenStax physics text, organized by chapter and section under `Chat.App.API/Physics`.

- `Chat.App.API/Physics/index.md` is the table of contents
- Each chapter folder contains section markdown files
- `Chat.App.API/ATTRIBUTION.md` documents the source and license

This content is sourced from OpenStax and remains under CC BY 4.0; it is not included under the surrounding project’s MIT license.

## Prerequisites

- .NET SDK 10
- Node.js 20+ LTS
- An AI provider configuration for the backend, such as a valid API key and model in the application settings

## Quick start

### 1) Configure the backend

Set your AI settings in `Chat.App.API/appsettings.Development.json` or `Chat.App.API/appsettings.json`:

```json
"Agent": {
  "ApiKey": "your-key",
  "Model": "your-model",
  "Name": "Physics-agent"
}
```

### 2) Start the backend

```bash
dotnet restore
cd Chat.App.API
dotnet run
```

The API runs with ASP.NET Core and serves local endpoints on the default development ports.

### 3) Start the frontend

```bash
cd chat-app-ui
npm install
npm run dev
```

The frontend is available at:

- http://localhost:5173

The backend is typically available at:

- http://localhost:5081/swagger
- http://localhost:5081/api/health

### 4) Open the app

Visit the frontend in the browser and start a new conversation.

## Regenerating the physics book

If you want to refresh the markdown content from the upstream OpenStax source data:

```bash
dotnet run --project book-injector/book-injector.csproj
```

This downloads the source archive, converts relevant modules into markdown, writes chapter files under `Chat.App.API/Physics`, and creates the index and attribution files.

## Project structure

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

## Configuration notes

The backend reads configuration from:

- `Chat.App.API/appsettings.json`
- `Chat.App.API/appsettings.Development.json`

Key settings include:

- `ConnectionStrings:Default`
- `ConnectionStrings:ConnectionStringMessages`
- `Agent:ApiKey`
- `Agent:Model`
- `Agent:Name`
- `Agent:SystemPrompt`
- `Cors:AllowedOrigins`

## API endpoints

The API exposes the main endpoints used by the app:

- `GET /api/health`
- `GET /api/conversations`
- `PATCH /api/conversations/{id}`
- `DELETE /api/conversations/{id}`
- `POST /api/chat`
- `GET /api/chat/{conversationId}`

## Notes

- The frontend expects `/api/...` routes and is configured to work with the backend locally through Vite.
- The app persists conversation and message history in SQLite.
- The physics corpus is intentionally local and source-grounded so the assistant can answer in the context of the included textbook material.
