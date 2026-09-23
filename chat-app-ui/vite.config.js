import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The UI calls relative paths like /api/chat and /api/conversations.
// In dev, proxy them to your backend so there's no CORS setup to manage;
// in prod, serve this build from the same origin as the API (or put a
// reverse proxy in front of both) so those relative paths keep working.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5081',
        changeOrigin: true,
      },
    },
  },
});
