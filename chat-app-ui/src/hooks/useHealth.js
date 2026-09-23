import { useEffect, useState } from 'react';
import { api } from '../api/client.js';

export function calculateHealthState({
  success,
  failureStartedAt,
  now,
  retryWindowMs = 60000,
}) {
  if (success) {
    return {
      status: 'ok',
      failureStartedAt: null,
    };
  }

  const startedAt = failureStartedAt ?? now;
  return {
    status: now - startedAt >= retryWindowMs ? 'down' : 'checking',
    failureStartedAt: startedAt,
  };
}

export function useHealth(pollMs = 60000) {
  const [status, setStatus] = useState('checking'); // 'checking' | 'ok' | 'down'

  useEffect(() => {
    let cancelled = false;
    let failureStartedAt = null;

    async function check() {
      const now = Date.now();

      try {
        await api.health();
        if (!cancelled) {
          const nextState = calculateHealthState({
            success: true,
            failureStartedAt,
            now,
            retryWindowMs: 60000,
          });
          failureStartedAt = nextState.failureStartedAt;
          setStatus(nextState.status);
        }
      } catch {
        if (!cancelled) {
          const nextState = calculateHealthState({
            success: false,
            failureStartedAt,
            now,
            retryWindowMs: 60000,
          });
          failureStartedAt = nextState.failureStartedAt;
          setStatus(nextState.status);
        }
      }
    }

    check();
    const id = setInterval(check, pollMs);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [pollMs]);

  return status;
}
