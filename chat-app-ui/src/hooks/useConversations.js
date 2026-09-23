import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import { useToast } from '../context/ToastContext';

export function useConversations() {
  const toast = useToast();

  const [conversations, setConversations] = useState([]);
  const [conversationsLoading, setConversationsLoading] = useState(true);

  const [activeId, setActiveId] = useState(null);
  const [messages, setMessages] = useState([]);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [sending, setSending] = useState(false);

  const requestSeq = useRef(0);

  const refreshList = useCallback(async () => {
    setConversationsLoading(true);
    try {
      const list = await api.listConversations();
      setConversations(list);
    } catch (err) {
      toast.error(err.message);
    } finally {
      setConversationsLoading(false);
    }
  }, [toast]);

  useEffect(() => {
    refreshList();
  }, [refreshList]);

  const openConversation = useCallback(
    async (id) => {
      setActiveId(id);
      setConversationLoading(true);
      const seq = ++requestSeq.current;
      try {
        const detail = await api.getConversation(id);
        if (seq === requestSeq.current) setMessages(detail.messages || []);
      } catch (err) {
        if (seq === requestSeq.current) toast.error(err.message);
      } finally {
        if (seq === requestSeq.current) setConversationLoading(false);
      }
    },
    [toast]
  );

  const startNewConversation = useCallback(() => {
    setActiveId(null);
    setMessages([]);
  }, []);

  const sendMessage = useCallback(
    async (text) => {
      const trimmed = text.trim();
      if (!trimmed || sending) return;

      const optimisticUser = {
        role: 'user',
        content: trimmed,
        createdAt: new Date().toISOString(),
      };
      setMessages((m) => [...m, optimisticUser]);
      setSending(true);

      try {
        let finalId = activeId;
        // Maintain a streaming handler so the UI can show partial assistant
        // content as it's produced and pick up the conversation id immediately.
        await api.sendMessage(activeId, trimmed, {
          onEvent: (eventName, payload) => {
            if (!payload || typeof payload !== 'object') return;

            const nextId = payload.conversationId;
            if (nextId) {
              finalId = nextId;
              setActiveId(nextId);
            }

            if (eventName === 'message' || eventName === 'done') {
              const chunk = payload?.assistantMessage?.content ?? payload?.content ?? '';
              if (!chunk) return;

              setMessages((prev) => {
                const last = prev[prev.length - 1];
                if (!last || last.role !== 'assistant') {
                  return [...prev, { role: 'assistant', content: chunk, createdAt: new Date().toISOString() }];
                }
                const updated = prev.slice();
                updated[updated.length - 1] = { ...last, content: (last.content || '') + chunk };
                return updated;
              });
            }
          },
        });

        // Refresh final state from server (conversation id may have changed)
        const result = await api.getConversation(finalId);
        const nextId = result?.id ?? finalId;
        const isNewConversation = !activeId;

        setMessages(result.messages || []);
        setActiveId(nextId);

        if (result.title || result.createdAt || result.updatedAt) {
          setConversations((list) => {
            const others = list.filter((c) => c.id !== nextId);
            const updated = {
              id: nextId,
              title: result.title,
              createdAt: result.createdAt,
              updatedAt: result.updatedAt,
            };
            return [updated, ...others];
          });
        }

        if (isNewConversation) {
          // Title is server-generated on first turn; a quiet list refresh
          // keeps counts/timestamps consistent without a jarring reload.
          refreshList();
        }
      } catch (err) {
        toast.error(err.message);
        // Roll back the optimistic bubble so the input can be retried.
        setMessages((m) => m.filter((msg) => msg !== optimisticUser));
      } finally {
        setSending(false);
      }
    },
    [activeId, sending, toast, refreshList]
  );

  const renameConversation = useCallback(
    async (id, title) => {
      const trimmed = title.trim();
      if (!trimmed) return false;
      try {
        const updated = await api.renameConversation(id, trimmed);
        setConversations((list) =>
          list.map((c) => (c.id === id ? { ...c, title: updated.title, updatedAt: updated.updatedAt } : c))
        );
        toast.success('Conversation renamed.');
        return true;
      } catch (err) {
        toast.error(err.message);
        return false;
      }
    },
    [toast]
  );

  const deleteConversation = useCallback(
    async (id) => {
      try {
        await api.deleteConversation(id);
        setConversations((list) => list.filter((c) => c.id !== id));
        if (activeId === id) {
          setActiveId(null);
          setMessages([]);
        }
        toast.success('Conversation deleted.');
        return true;
      } catch (err) {
        toast.error(err.message);
        return false;
      }
    },
    [activeId, toast]
  );

  return {
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
  };
}
