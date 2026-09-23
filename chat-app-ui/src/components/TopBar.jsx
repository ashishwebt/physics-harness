import { Menu, Moon, Sun } from 'lucide-react';
import { useHealth } from '../hooks/useHealth';

const HEALTH_COPY = {
  checking: { label: 'Checking…', dot: 'bg-ink-soft' },
  ok: { label: 'All systems go', dot: 'bg-emerald-500' },
  down: { label: 'Server unreachable', dot: 'bg-danger' },
};

export default function TopBar({ onMenuClick, theme, onToggleTheme }) {
  const health = useHealth();
  const copy = HEALTH_COPY[health];

  return (
    <header className="h-14 shrink-0 border-b border-line bg-surface flex items-center justify-between px-3 md:px-5">
      <div className="flex items-center gap-3 min-w-0">
        <button
          onClick={onMenuClick}
          className="md:hidden p-1.5 -ml-1 text-ink-soft hover:text-ink"
          aria-label="Open conversations"
        >
          <Menu size={20} />
        </button>
        <span className="font-wordmark text-lg font-semibold text-ink tracking-tight">Fieldnote</span>
      </div>

      <div className="flex items-center gap-4">
        <div className="hidden sm:flex items-center gap-1.5 text-xs text-ink-soft" role="status">
          <span className={`w-1.5 h-1.5 rounded-full ${copy.dot}`} aria-hidden="true" />
          {copy.label}
        </div>
        <button
          onClick={onToggleTheme}
          className="p-1.5 rounded-lg text-ink-soft hover:text-ink hover:bg-paper transition-colors"
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
        >
          {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
        </button>
      </div>
    </header>
  );
}
