import { useEffect, useRef } from 'react';
import MessageBubble from './MessageBubble';
import MessageInput from './MessageInput';

function ThinkingIndicator() {
  return (
    <div className="flex justify-start msg-enter">
      <div className="bg-surface border border-line rounded-2xl rounded-bl-sm px-4 py-3 flex items-center gap-1">
        {[0, 1, 2].map((i) => (
          <span
            key={i}
            className="think-dot w-1.5 h-1.5 rounded-full bg-ink-soft"
            style={{ animationDelay: `${i * 0.15}s` }}
          />
        ))}
      </div>
    </div>
  );
}

function EmptyState() {
  return (
    <div className="h-full flex flex-col items-center justify-center text-center px-6">
      <p className="font-wordmark text-2xl text-ink mb-1">Start a new thread</p>
      <p className="text-sm text-ink-soft max-w-xs">
        Ask a question, paste something to summarize, or just say hello — your first message names the conversation.
      </p>
    </div>
  );
}

export default function ChatWindow({ messages, loading, sending, onSend }) {
  const scrollRef = useRef(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages, sending]);

  return (
    <div className="flex-1 flex flex-col min-w-0 bg-paper">
      <div ref={scrollRef} className="flex-1 overflow-y-auto">
        {loading ? (
          <div className="p-4 md:p-6 space-y-4 max-w-3xl mx-auto w-full" aria-hidden="true">
            {[...Array(3)].map((_, i) => (
              <div key={i} className={`flex ${i % 2 ? 'justify-end' : 'justify-start'}`}>
                <div className="h-10 w-2/3 rounded-2xl bg-surface border border-line animate-pulse" />
              </div>
            ))}
          </div>
        ) : messages.length === 0 ? (
          <EmptyState />
        ) : (
          <div className="p-4 md:p-6 space-y-4 max-w-3xl mx-auto w-full">
            {messages.map((m, i) => (
              <MessageBubble key={i} role={m.role} content={m.content} createdAt={m.createdAt} />
            ))}
            {sending && <ThinkingIndicator />}
          </div>
        )}
      </div>

      <MessageInput onSend={onSend} disabled={sending} />
    </div>
  );
}
