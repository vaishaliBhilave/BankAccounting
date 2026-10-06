import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// In development the API runs on :8080 (docker compose up api); the proxy keeps the browser same-origin,
// exactly like production, where the API serves this build from wwwroot.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': 'http://localhost:8080', '/health': 'http://localhost:8080' } },
  build: { outDir: 'dist' },
  test: { environment: 'jsdom', globals: true, setupFiles: ['./src/test/setup.ts'], css: false }
});
