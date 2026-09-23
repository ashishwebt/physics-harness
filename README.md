# Chat.App

[日本語](./README.ja.md)

A full-stack AI chat application built with a .NET 10 backend and a React + Vite frontend. The project lets users create and manage conversations, send messages to an AI agent, and view streaming responses with a polished chat interface.

## Project overview

- Backend: `Chat.App.API`
  - ASP.NET Core Web API
  - SQLite-backed conversation and history storage
  - AI agent integration for streaming chat responses
  - health check and conversation APIs

- Frontend: `chat-app-ui`
  - React + Vite
  - Tailwind CSS UI
  - conversation sidebar and chat window
  - light/dark theme support
  - markdown rendering and toasts

## Features

- Create and manage chat conversations
- Rename and delete conversations
- Send messages to the AI agent
- Stream responses in real time
- Retrieve message history for each conversation
- Persistent storage with SQLite
- Responsive UI for desktop and mobile

## Prerequisites

- .NET SDK 10
- Node.js 20+ LTS
- An AI provider configuration for the backend (for example, a configured API key and model in the app settings)

## Quick start

### 1) Start the backend

```bash
dotnet restore
cd Chat.App.API
dotnet run
```

The API runs locally with the default ASP.NET Core development configuration. The application reads configuration from `Chat.App.API/appsettings.json` and `appsettings.Development.json`.

### 2) Start the frontend

```bash
cd chat-app-ui
npm install
npm run dev
```

The frontend dev server will start at http://localhost:5173 and proxy API requests to the backend at http://localhost:5081.

### 3) Open the app

Visit:

- Frontend: http://localhost:5173
- API docs (development): http://localhost:5081/swagger
- Health check: http://localhost:5081/api/health

## VS Code debugging

The workspace already includes a root-level VS Code debug setup in `.vscode/launch.json`.

Use the following debug options from the VS Code Run and Debug panel:

- `Chat.App.API` – launches the ASP.NET Core backend in debug mode
- `React` – starts the Vite frontend in the integrated terminal
- `fullstack` – launches both services together in one compound configuration

Set breakpoints in files like `Chat.App.API/Program.cs`, `Chat.App.API/Controllers/*.cs`, or `chat-app-ui/src/**/*.jsx` and then choose the profile you want.

This is the easiest way to step through the API request flow and the UI state updates during development.

## Configuration

The backend uses the app settings file at `Chat.App.API/appsettings.json`.

Key settings include:

- `ConnectionStrings:Default`
- `ConnectionStrings:ConnectionStringMessages`
- `Agent:ApiKey`
- `Agent:Model`
- `Agent:Name`
- `Agent:SystemPrompt`
- `Cors:AllowedOrigins`

Update these values to match your environment and AI provider.

## API endpoints

The backend exposes these main endpoints:

- `GET /api/health`
- `GET /api/conversations`
- `PATCH /api/conversations/{id}`
- `DELETE /api/conversations/{id}`
- `POST /api/chat`
- `GET /api/chat/{conversationId}`

## Repository structure

```text
Chat.App/
├─ Chat.App.API/              .NET API project
│  ├─ Controllers/
│  ├─ Data/
│  ├─ Models/
│  ├─ AgentServices/
│  ├─ Program.cs
│  ├─ appsettings.json
│  └─ appsettings.Development.json
├─ chat-app-ui/              React frontend
│  ├─ src/
│  ├─ package.json
│  ├─ vite.config.js
│  └─ README.md
├─ Chat.App.sln
├─ LICENSE
├─ README.md
└─ ...
```

## Notes

- The frontend is designed to use relative `/api/...` routes. In local development this is handled by Vite proxying; in production, serving the frontend and API from the same origin or behind a reverse proxy is the most reliable approach.
- The backend uses SQLite for conversation records and chat history. Ensure the app has permission to write to the configured data directory.
