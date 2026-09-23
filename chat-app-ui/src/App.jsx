import { useEffect, useState } from 'react';
import { ToastProvider } from './context/ToastContext';
import { useTheme } from './hooks/useTheme';
import { useConversations } from './hooks/useConversations';
import Sidebar from './components/Sidebar';
import TopBar from './components/TopBar';
import ChatWindow from './components/ChatWindow';

function Shell() {
  const { theme, toggle } = useTheme();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  const {
    conversations,
    conversationsLoading,
    activeId,
    messages,
    conversationLoading,
    sending,
    openConversation,
    startNewConversation,
    sendMessage,
    renameConversation,
    deleteConversation,
  } = useConversations();

  // On initial load, open a conversation if the URL contains one.
  useEffect(() => {
    try {
      const m = window.location.pathname.match(/^\/c\/conversation\/([^/]+)\/?$/);
      if (m && m[1]) {
        openConversation(m[1]);
      }
    } catch {
      // ignore in non-browser / test environments
    }
    // run only on mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    try {
      const path = activeId ? `/c/conversation/${activeId}` : '/';
      if (window && window.history && window.location) {
        if (window.location.pathname !== path) {
          // Use replaceState so streaming partial updates don't fill history.
          window.history.replaceState({}, '', path);
        }
      }
    } catch {
      // Ignore in environments without a window object (SSR/tests).
    }
  }, [activeId]);

  return (
    <div className="h-screen flex flex-col bg-paper text-ink">
      <TopBar onMenuClick={() => setSidebarOpen(true)} theme={theme} onToggleTheme={toggle} />
      <div className="flex-1 flex min-h-0">
        <Sidebar
          open={sidebarOpen}
          onClose={() => setSidebarOpen(false)}
          conversations={conversations}
          conversationsLoading={conversationsLoading}
          activeId={activeId}
          onOpen={openConversation}
          onNew={() => {
            startNewConversation();
            setSidebarOpen(false);
          }}
          onRename={renameConversation}
          onDelete={deleteConversation}
        />
        <ChatWindow
          messages={messages}
          loading={conversationLoading}
          sending={sending}
          onSend={sendMessage}
        />
      </div>
    </div>
  );
}

export default function App() {
  return (
    <ToastProvider>
      <Shell />
    </ToastProvider>
  );
}
