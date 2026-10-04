import { useEffect, useMemo, useRef, useState } from 'react';
import { FileText, FileCode2, ChevronsLeft, ChevronsRight } from 'lucide-react';
import MessageBubble from './MessageBubble';
import MessageInput from './MessageInput';
import { getSelectedFile, getVisibleFiles } from './threadFiles';

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

export default function ChatWindow({ messages, files, loading, sending, onSend }) {
  const scrollRef = useRef(null);
  const visibleFiles = useMemo(() => getVisibleFiles(files), [files]);
  const [selectedPath, setSelectedPath] = useState(null);
  const [filePanelOpen, setFilePanelOpen] = useState(true);

  const selectedFile = useMemo(
    () => getSelectedFile(visibleFiles, selectedPath),
    [visibleFiles, selectedPath],
  );

  useEffect(() => {
    if (!visibleFiles.length) {
      setSelectedPath(null);
      setFilePanelOpen(false);
      return;
    }

    if (!selectedPath || !visibleFiles.some((file) => file.path === selectedPath)) {
      setSelectedPath(visibleFiles[0].path);
    }
  }, [visibleFiles, selectedPath]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages, sending]);

  const showFilePanel = visibleFiles.length > 0 && filePanelOpen;

  return (
    <div className="flex-1 flex min-w-0 bg-paper">
      <div className="flex-1 flex flex-col min-w-0">
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

      {visibleFiles.length > 0 && (
        showFilePanel ? (
          <aside className="w-full max-w-[420px] shrink-0 border-l border-line bg-surface/60">
            <div className="flex items-center justify-between gap-3 border-b border-line px-3 py-3">
              <div className="min-w-0">
                <p className="text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-soft">Thread files</p>
                <p className="mt-1 truncate text-sm font-medium text-ink">{visibleFiles.length} item{visibleFiles.length === 1 ? '' : 's'}</p>
              </div>

              <button
                type="button"
                onClick={() => setFilePanelOpen(false)}
                className="flex items-center gap-1 rounded-lg border border-line bg-paper px-2 py-1 text-[11px] text-ink-soft hover:text-ink"
                aria-label="Collapse file panel"
              >
                <ChevronsLeft size={14} />
                Hide
              </button>
            </div>

            <div className="border-b border-line px-3 py-3 space-y-2">
              {visibleFiles.map((file) => {
                const isSelected = selectedFile?.path === file.path;
                return (
                  <button
                    key={file.path}
                    type="button"
                    onClick={() => setSelectedPath(file.path)}
                    className={`w-full flex items-center gap-2 rounded-xl border px-2.5 py-2 text-left transition-colors ${
                      isSelected
                        ? 'border-slate bg-slate/8 text-ink'
                        : 'border-line bg-paper text-ink-soft hover:border-slate/60 hover:text-ink'
                    }`}
                  >
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md border border-line bg-surface text-ink-soft">
                      {file.path.toLowerCase().endsWith('.md') || file.path.toLowerCase().endsWith('.txt') ? (
                        <FileText size={13} />
                      ) : (
                        <FileCode2 size={13} />
                      )}
                    </span>
                    <span className="truncate text-xs font-medium">{file.path}</span>
                  </button>
                );
              })}
            </div>

            {selectedFile && (
              <div className="flex flex-col min-h-0">
                <div className="flex items-center justify-between gap-3 border-b border-line px-3 py-2.5">
                  <div className="min-w-0">
                    <p className="text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-soft">Open file</p>
                    <p className="mt-1 truncate text-sm font-medium text-ink">{selectedFile.path}</p>
                  </div>
                  <span className="rounded-full border border-line bg-paper px-2 py-0.5 text-[10px] font-medium text-ink-soft uppercase tracking-[0.12em]">
                    Read-only
                  </span>
                </div>

                <div className="flex-1 overflow-auto p-3">
                  <pre className="min-h-full whitespace-pre-wrap rounded-xl border border-line bg-paper p-3 text-[11px] leading-6 text-ink-soft font-mono">
                    {selectedFile.content || 'This file is empty.'}
                  </pre>
                </div>
              </div>
            )}
          </aside>
        ) : (
          <div className="flex shrink-0 items-center justify-center border-r border-line bg-surface/60 px-2">
            <button
              type="button"
              onClick={() => setFilePanelOpen(true)}
              className="flex h-9 w-9 items-center justify-center rounded-lg border border-line bg-paper text-ink-soft hover:text-ink"
              aria-label="Show file panel"
            >
              <ChevronsRight size={16} />
            </button>
          </div>
        )
      )}
    </div>
  );
}
