import { useState } from 'react';
import { Plus, X } from 'lucide-react';
import ConversationItem from './ConversationItem';
import ConfirmModal from './ConfirmModal';

export default function Sidebar({
  open,
  onClose,
  conversations,
  conversationsLoading,
  activeId,
  onOpen,
  onNew,
  onRename,
  onDelete,
}) {
  const [pendingDelete, setPendingDelete] = useState(null);

  return (
    <>
      {/* Mobile scrim */}
      {open && (
        <div className="fixed inset-0 bg-ink/30 z-30 md:hidden" onClick={onClose} aria-hidden="true" />
      )}

      <aside
        className={`fixed md:static inset-y-0 left-0 z-40 w-72 shrink-0 border-r border-line bg-surface flex flex-col
          transform transition-transform duration-200 ease-out
          ${open ? 'translate-x-0' : '-translate-x-full md:translate-x-0'}`}
        aria-label="Conversations"
      >
        <div className="flex items-center justify-between px-4 h-14 border-b border-line shrink-0">
          <h2 className="text-sm font-semibold text-ink-soft uppercase tracking-wide">Conversations</h2>
          <button
            onClick={onClose}
            className="md:hidden p-1 text-ink-soft hover:text-ink"
            aria-label="Close sidebar"
          >
            <X size={18} />
          </button>
        </div>

        <div className="p-3 border-b border-line shrink-0">
          <button
            onClick={onNew}
            className="w-full flex items-center justify-center gap-2 rounded-lg bg-slate text-slate-ink text-sm font-medium py-2 hover:opacity-90 transition-opacity"
          >
            <Plus size={16} />
            New conversation
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-2 space-y-1">
          {conversationsLoading && (
            <div className="space-y-2 p-1" aria-hidden="true">
              {[...Array(4)].map((_, i) => (
                <div key={i} className="h-12 rounded-lg bg-paper animate-pulse" />
              ))}
            </div>
          )}

          {!conversationsLoading && conversations.length === 0 && (
            <p className="text-sm text-ink-soft px-3 py-6 text-center">
              No conversations yet. Start one to see it here.
            </p>
          )}

          {conversations.map((c) => (
            <ConversationItem
              key={c.id}
              conversation={c}
              active={c.id === activeId}
              onOpen={(id) => {
                onOpen(id);
                onClose();
              }}
              onRename={onRename}
              onRequestDelete={setPendingDelete}
            />
          ))}
        </div>
      </aside>

      <ConfirmModal
        open={!!pendingDelete}
        title="Delete this conversation?"
        body={pendingDelete ? `"${pendingDelete.title}" will be permanently removed. This can't be undone.` : ''}
        confirmLabel="Delete"
        onCancel={() => setPendingDelete(null)}
        onConfirm={async () => {
          await onDelete(pendingDelete.id);
          setPendingDelete(null);
        }}
      />
    </>
  );
}
