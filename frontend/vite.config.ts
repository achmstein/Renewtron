import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Aspire's WithReference("renewtron-server") injects per-endpoint env vars in
// the form services__<name>__<scheme>__<index>. We always prefer the plain-HTTP
// endpoint because Vite's bundled proxy (legacy http-proxy) doesn't speak h2,
// and the .NET server's https endpoint negotiates HTTP/2. Falling back to
// SERVER_HTTP / SERVER_HTTPS for older AppHost configs.
const proxyTarget =
  process.env['services__renewtron-server__http__0'] ||
  process.env.SERVER_HTTP ||
  process.env['services__renewtron-server__https__0'] ||
  process.env.SERVER_HTTPS ||
  'http://localhost:5000'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  build: {
    rollupOptions: {
      output: {
        // Recharts is by far the heaviest dependency — keep it in its own
        // long-cached chunk instead of bloating the admin bundle.
        manualChunks: { charts: ['recharts'] },
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: proxyTarget,
        changeOrigin: true,
        secure: false,
      },
      '/health': {
        target: proxyTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
