import { useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Check, Copy } from 'lucide-react';

function toLocalDate(iso) {
  if (!iso) return null;

  const value = String(iso).trim();
  if (!value) return null;

  const asDate = new Date(value);
  if (Number.isNaN(asDate.getTime())) return null;

  const hasTimezone = /[zZ]|[+-]\d{2}:?\d{2}$/.test(value);
  if (!hasTimezone) {
    const normalized = value.includes('T') || value.includes(' ') ? value.replace(' ', 'T') : `${value}T00:00:00`;
    const utcDate = new Date(`${normalized}Z`);
    if (!Number.isNaN(utcDate.getTime())) return utcDate;
  }

  return asDate;
}

function formatTime(iso) {
  try {
    const date = toLocalDate(iso);
    if (!date) return '';
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  } catch {
    return '';
  }
}

export default function MessageBubble({ role, content, createdAt }) {
  const [copied, setCopied] = useState(false);
  const isUser = role === 'user';

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(content);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      /* clipboard unavailable; fail quietly */
    }
  };

  return (
    <div className={`msg-enter flex ${isUser ? 'justify-end' : 'justify-start'}`}>
      <div className={`group max-w-[85%] sm:max-w-[70%] ${isUser ? 'items-end' : 'items-start'} flex flex-col`}>
        <div
          className={`rounded-2xl px-4 py-2.5 text-sm leading-relaxed break-words ${
            isUser
              ? 'bg-accent text-accent-ink rounded-br-sm'
              : 'bg-surface border border-line text-ink rounded-bl-sm'
          }`}
        >
          <div className="prose prose-sm max-w-none [&_p]:my-1.5 [&_p:first-child]:mt-0 [&_p:last-child]:mb-0 [&_pre]:bg-ink/5 [&_pre]:rounded-lg [&_pre]:p-3 [&_code]:text-[0.85em]">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{content}</ReactMarkdown>
          </div>
        </div>
        <div className="flex items-center gap-2 mt-1 px-1">
          <span className="text-[11px] text-ink-soft">{formatTime(createdAt)}</span>
          {!isUser && (
            <button
              onClick={copy}
              className="opacity-0 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity text-ink-soft hover:text-ink"
              aria-label="Copy message"
            >
              {copied ? <Check size={12} /> : <Copy size={12} />}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
