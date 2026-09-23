import { useRef, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ArrowUp, Eye, EyeOff } from 'lucide-react';

export default function MessageInput({ onSend, disabled }) {
  const [value, setValue] = useState('');
  const [preview, setPreview] = useState(false);
  const textareaRef = useRef(null);

  const submit = () => {
    if (!value.trim() || disabled) return;
    onSend(value);
    setValue('');
    setPreview(false);
    requestAnimationFrame(() => {
      if (textareaRef.current) textareaRef.current.style.height = 'auto';
    });
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  const autoGrow = (e) => {
    setValue(e.target.value);
    e.target.style.height = 'auto';
    e.target.style.height = `${Math.min(e.target.scrollHeight, 200)}px`;
  };

  return (
    <div className="shrink-0 border-t border-line bg-surface p-3 md:p-4">
      <div className="max-w-3xl mx-auto">
        <div className="rounded-2xl border border-line bg-paper focus-within:border-slate transition-colors">
          <div className="flex items-center justify-between px-3 pt-2">
            <span className="text-[11px] text-ink-soft">Markdown supported · Enter to send · Shift+Enter for a new line</span>
            <button
              type="button"
              onClick={() => setPreview((p) => !p)}
              disabled={!value.trim()}
              className="flex items-center gap-1 text-[11px] text-ink-soft hover:text-ink disabled:opacity-40 transition-colors"
            >
              {preview ? <EyeOff size={12} /> : <Eye size={12} />}
              {preview ? 'Edit' : 'Preview'}
            </button>
          </div>

          <div className="px-3 pb-2 pt-1">
            {preview ? (
                <div className="prose prose-sm max-w-none min-h-[2.5rem] text-sm text-ink [&_p]:my-1.5">
                <ReactMarkdown remarkPlugins={[remarkGfm]}>{value || '*Nothing to preview yet.*'}</ReactMarkdown>
              </div>
            ) : (
              <textarea
                ref={textareaRef}
                value={value}
                onChange={autoGrow}
                onKeyDown={handleKeyDown}
                placeholder="Message…"
                rows={1}
                aria-label="Message"
                className="w-full resize-none bg-transparent text-sm text-ink placeholder:text-ink-soft outline-none max-h-[200px]"
              />
            )}
          </div>
        </div>

        <div className="flex justify-end mt-2">
          <button
            onClick={submit}
            disabled={!value.trim() || disabled}
            aria-label="Send message"
            className="flex items-center gap-1.5 rounded-full bg-slate text-slate-ink text-sm font-medium px-4 py-2 disabled:opacity-40 disabled:cursor-not-allowed hover:opacity-90 transition-opacity"
          >
            Send
            <ArrowUp size={14} />
          </button>
        </div>
      </div>
    </div>
  );
}
