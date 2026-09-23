# Chat.App Frontend

[日本語](./README.ja.md)

A React + Vite + Tailwind chat interface for the Chat.App project. It provides a conversation sidebar, streaming message view, markdown rendering, theme switching, and conversation CRUD actions backed by the .NET API.

## Features

- Conversation list with create, rename, and delete flows
- Streaming chat responses from the backend
- Message history retrieval for each conversation
- Markdown rendering and copy-to-clipboard for assistant responses
- Light/dark theme persistence
- Health status indicator for the API
- Mobile-friendly sidebar and composer layout

## Prerequisites

- Node.js 20+ or a compatible LTS version
- The backend API running locally

## Setup

From the project root:

```bash
cd chat-app-ui
npm install
npm run dev
```

The dev server runs at http://localhost:5173 and proxies requests under `/api` to the backend at http://localhost:5081 as configured in `vite.config.js`.

## Scripts

```bash
npm run dev     # start the Vite development server
npm run build   # create a production build in dist/
npm run preview # preview the production build locally
```

## API integration

The UI calls the backend endpoints below:

- `GET /api/health` – backend health check
- `GET /api/conversations` – list conversations
- `PATCH /api/conversations/{id}` – rename a conversation
- `DELETE /api/conversations/{id}` – delete a conversation
- `POST /api/chat` – send a new chat message
- `GET /api/chat/{id}` – fetch conversation details/history

## Project structure

```text
src/
  api/
    client.js             API client wrapper and error handling
  components/
    App.jsx               top-level app shell
    ChatWindow.jsx        message list and streaming UI
    ConversationItem.jsx  row-based list item with rename/delete actions
    ConfirmModal.jsx      confirmation dialog for delete actions
    MessageBubble.jsx     message styling, markdown, copy button
    MessageInput.jsx      composer input and send handling
    Sidebar.jsx           conversation navigation and actions
    TopBar.jsx            title, health state, theme toggle, menu button
  context/
    ToastContext.jsx      toast notifications
  hooks/
    useConversations.js   conversation state and API mutations
    useHealth.js           health polling logic
    useTheme.js            persisted theme behavior
  index.css               Tailwind/theme styling
  main.jsx                app entry point
```

## Notes

- The frontend relies on relative `/api/...` calls, which works well when the UI and API are served from the same origin or through the Vite proxy during development.
- The app is designed to behave well for streaming chat responses and optimistic UI updates while preserving a responsive user experience.
- Theme variables and Tailwind settings live in `src/index.css` and `tailwind.config.js`.
