// Thin wrapper around the backend contract. Every function throws an
// ApiError with a human-readable message on failure, so callers can
// show it directly in a toast without re-deriving copy.

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export async function withRetry(
  operation,
  {
    maxRetries = 3,
    baseDelayMs = 3000,
    maxDelayMs = 30000,
    sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
    onDelay,
  } = {},
) {
  for (let retryAttempt = 0; ; retryAttempt += 1) {
    try {
      return await operation();
    } catch (error) {
      const hasRetriesLeft = retryAttempt < maxRetries;
      if (!hasRetriesLeft) {
        throw error;
      }

      const waitMs = Math.min(baseDelayMs * 3 ** retryAttempt, maxDelayMs);
      if (onDelay) onDelay(waitMs);
      await sleep(waitMs);
    }
  }
}

async function request(path, options = {}) {
  return withRetry(async () => {
    let res;
    try {
      res = await fetch(path, {
        headers: { 'Content-Type': 'application/json', ...options.headers },
        ...options,
      });
    } catch {
      throw new ApiError('Could not reach the server. Check your connection and try again.');
    }

    if (!res.ok) {
      let detail = '';
      try {
        const body = await res.json();
        detail = body?.message || body?.detail || '';
      } catch {
        /* response wasn't JSON, ignore */
      }
      throw new ApiError(detail || `Request failed (${res.status}).`, res.status);
    }

    if (res.status === 204) return null;
    return res.json();
  });
}

async function readEventStream(response, onEvent) {
  if (!response.body) {
    throw new ApiError('The server did not return a streaming response.', response.status);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  const flushBuffer = () => {
    const blocks = buffer.split(/\r?\n\r?\n/);
    buffer = blocks.pop() ?? '';

    for (const block of blocks) {
      if (!block.trim()) continue;

      let eventName = 'message';
      let payload = '';

      for (const line of block.split(/\r?\n/)) {
        if (!line) continue;
        if (line.startsWith(':')) continue;
        if (line.startsWith('event:')) {
          eventName = line.slice(6).trim();
          continue;
        }
        if (line.startsWith('data:')) {
          payload += `${payload ? '\n' : ''}${line.slice(5).trim()}`;
        }
      }

      if (!payload) continue;

      let value;
      try {
        value = JSON.parse(payload);
      } catch {
        value = payload;
      }

      onEvent(eventName, value);
    }
  };

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        flushBuffer();
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      flushBuffer();
    }
  } finally {
    reader.releaseLock();
  }
}

export const api = {
  health: () => request('/api/health'),

  listConversations: (skip = 0, limit = 20) =>
    request(`/api/conversations?skip=${skip}&limit=${limit}`),

  getConversation: (id) => request(`/api/chat/${id}`),

  sendMessage: async (conversationId, message, { onEvent } = {}) => {
    const response = await withRetry(async () => {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ conversationId, message }),
      });

      if (!res.ok) {
        let detail = '';
        try {
          const body = await res.json();
          detail = body?.message || body?.detail || '';
        } catch {
          /* response wasn't JSON, ignore */
        }
        throw new ApiError(detail || `Request failed (${res.status}).`, res.status);
      }

      return res;
    });

    const resolvedConversationId = response.headers.get('x-conversation-id') ?? conversationId;
    let finalConversationId = resolvedConversationId;

    // Forward stream events to the caller when provided so the UI can render
    // partial assistant content and pick up the conversation id as soon as it's
    // emitted by the server.
    await readEventStream(response, (eventName, payload) => {
      if (!payload || typeof payload !== 'object') return;

      const nextId = payload.conversationId ?? finalConversationId;
      if (nextId) finalConversationId = nextId;

      if (typeof onEvent === 'function') {
        try {
          onEvent(eventName, payload);
        } catch {
          // ignore handler errors
        }
      }
    });

    const detail = await request(`/api/chat/${finalConversationId}`);
    return {
      id: finalConversationId,
      ...detail,
      messages: detail?.messages || [],
    };
  },

  renameConversation: (id, title) =>
    request(`/api/conversations/${id}`, {
      method: 'PATCH',
      body: JSON.stringify({ title }),
    }),

  deleteConversation: (id) =>
    request(`/api/conversations/${id}`, { method: 'DELETE' }),
};
