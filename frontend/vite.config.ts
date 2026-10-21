import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// The API (and the local /cdn package store in development) are proxied so the SPA,
// the refresh cookie and package downloads share one origin locally.
const apiTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5080';
const proxy = {
  '/api': { target: apiTarget, changeOrigin: false },
  '/cdn': { target: apiTarget, changeOrigin: false },
};

const ADSENSE_START = '<!-- adsense:start -->';
const ADSENSE_END = '<!-- adsense:end -->';

/**
 * Google AdSense: the tag must sit literally in <head> of the served HTML (Google verifies the site from it), so it is
 * injected into dist/index.html at build time. dist/workspace.html is the same page WITHOUT it and is served for the
 * ad-free house workspace and admin console (public/_redirects). PUBLIC_ADSENSE_CLIENT="" disables it (E2E builds).
 */
function adsense(client: string): Plugin {
  let outDir = 'dist';
  return {
    name: 'rongnho-adsense',
    apply: 'build',
    configResolved(config) {
      outDir = config.build.outDir;
    },
    transformIndexHtml(html) {
      if (!client) return html;
      const tag = `<script async src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${client}" crossorigin="anonymous"></script>`;
      return html.replace('</head>', `    ${ADSENSE_START}\n    ${tag}\n    ${ADSENSE_END}\n  </head>`);
    },
    closeBundle() {
      const index = readFileSync(`${outDir}/index.html`, 'utf8');
      const start = index.indexOf(ADSENSE_START);
      const end = index.indexOf(ADSENSE_END);
      const workspace = start >= 0 && end > start ? index.slice(0, start) + index.slice(end + ADSENSE_END.length) : index;
      writeFileSync(`${outDir}/workspace.html`, workspace.replace('<meta name="robots" content="index, follow, max-image-preview:large" />', '<meta name="robots" content="noindex" />'));
    },
  };
}

const adsenseClient = (mode: string) => loadEnv(mode, '..', 'PUBLIC_').PUBLIC_ADSENSE_CLIENT ?? process.env.PUBLIC_ADSENSE_CLIENT ?? 'ca-pub-9474823258875185';

export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss(), adsense(adsenseClient(mode))],
  // Lets the SPA load AdSense when arriving on a public page from the ad-free shell (empty in dev: no ads on localhost).
  define: { __ADSENSE_CLIENT__: JSON.stringify(mode === 'production' ? adsenseClient(mode) : '') },
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
}));
