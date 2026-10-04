import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// The API (and the local /cdn package store in development) are proxied so the SPA,
// the refresh cookie and package downloads share one origin locally.
const apiTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5080';
const proxy = {
  '/api': { target: apiTarget, changeOrigin: false },
  '/cdn': { target: apiTarget, changeOrigin: false },
};

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  // One root .env for backend + frontend; only PUBLIC_* / VITE_* values reach the browser bundle.
  envDir: '..',
  envPrefix: ['VITE_', 'PUBLIC_'],
  server: { port: 5173, strictPort: true, proxy },
  preview: { port: Number(process.env.PREVIEW_PORT ?? 4173), strictPort: true, proxy },
  build: {
    target: 'es2022',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('node_modules')) {
            if (/react-router|@tanstack|react-dom|scheduler|[\\/]react[\\/]/.test(id)) return 'vendor';
            if (/zod|react-hook-form|@hookform/.test(id)) return 'forms';
          }
          return undefined;
        },
      },
    },
  },
});
