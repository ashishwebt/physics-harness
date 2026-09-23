import { useEffect, useRef, useState } from 'react';
import { Pencil, Trash2, Check, X } from 'lucide-react';

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

function relativeTime(iso) {
  const date = toLocalDate(iso);
  if (!date) return 'just now';

  const diffMs = Date.now() - date.getTime();
  const mins = Math.round(diffMs / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days}d ago`;
  return date.toLocaleDateString();
}

export default function ConversationItem({
  conversation,
  active,
  onOpen,
  onRename,
  onRequestDelete,
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(conversation.title);
  const inputRef = useRef(null);

  useEffect(() => {
    if (editing) {
      inputRef.current?.focus();
      inputRef.current?.select();
    }
  }, [editing]);

  const commit = async () => {
    if (draft.trim() && draft.trim() !== conversation.title) {
      const ok = await onRename(conversation.id, draft.trim());
      if (!ok) setDraft(conversation.title);
    } else {
      setDraft(conversation.title);
    }
    setEditing(false);
  };

  const cancel = () => {
    setDraft(conversation.title);
    setEditing(false);
  };

  if (editing) {
    return (
      <div className="flex items-center gap-1 px-3 py-2 rounded-lg bg-paper border border-line">
        <input
          ref={inputRef}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') commit();
            if (e.key === 'Escape') cancel();
          }}
          className="flex-1 min-w-0 bg-transparent text-sm text-ink outline-none"
          aria-label="Conversation title"
        />
        <button onClick={commit} aria-label="Save name" className="p-1 text-slate hover:bg-line/50 rounded">
          <Check size={14} />
        </button>
        <button onClick={cancel} aria-label="Cancel rename" className="p-1 text-ink-soft hover:bg-line/50 rounded">
          <X size={14} />
        </button>
      </div>
    );
  }

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onOpen(conversation.id)}
      onKeyDown={(e) => e.key === 'Enter' && onOpen(conversation.id)}
      className={`group flex items-center gap-2 px-3 py-2 rounded-lg cursor-pointer transition-colors ${
        active ? 'bg-accent/15 border border-accent/40' : 'hover:bg-paper border border-transparent'
      }`}
    >
      <div className="min-w-0 flex-1">
        <p className={`text-sm truncate ${active ? 'text-ink font-medium' : 'text-ink'}`}>{conversation.title}</p>
        <p className="text-xs text-ink-soft mt-0.5">
          {relativeTime(conversation.updatedAt)}
          {typeof conversation.messageCount === 'number' ? ` · ${conversation.messageCount} msgs` : ''}
        </p>
      </div>
      <div className="hidden group-hover:flex items-center gap-0.5 shrink-0">
        <button
          onClick={(e) => {
            e.stopPropagation();
            setEditing(true);
          }}
          aria-label={`Rename ${conversation.title}`}
          className="p-1.5 text-ink-soft hover:text-ink hover:bg-line/50 rounded"
        >
          <Pencil size={13} />
        </button>
        <button
          onClick={(e) => {
            e.stopPropagation();
            onRequestDelete(conversation);
          }}
          aria-label={`Delete ${conversation.title}`}
          className="p-1.5 text-ink-soft hover:text-danger hover:bg-danger/10 rounded"
        >
          <Trash2 size={13} />
        </button>
      </div>
    </div>
  );
}
