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

/** Publisher ownership is verified with meta tags, without loading ad scripts during build.
 * Auto ads are opt-in and runtime gated by public site configuration. The workspace remains ad-free. */
function adsense(client: string): Plugin {
  let outDir = 'dist';
  return {
    name: 'rongnho-adsense',
    apply: 'build',
    configResolved(config) {
      outDir = config.build.outDir;
    },
    transformIndexHtml(html) {
      // Ownership tags only; never load third-party advertising scripts here.
      const tags = [
        ...(client ? [`<meta name="google-adsense-account" content="${client}">`] : []),
        '<meta name="clckd" content="3b2aefb1d9c09ae742409df61ad0bed7" />',
      ];
      return html.replace('</head>', `    ${ADSENSE_START}\n    ${tags.join("\n    ")}\n    ${ADSENSE_END}\n  </head>`);
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
  // Auto ads are disabled by default. Ownership verification does not require loading ad code.
  define: { __ADSENSE_CLIENT__: JSON.stringify(mode === 'production' && (loadEnv(mode, '..', 'PUBLIC_').PUBLIC_ADSENSE_AUTO_ADS ?? process.env.PUBLIC_ADSENSE_AUTO_ADS) === 'true' ? adsenseClient(mode) : '') },
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
