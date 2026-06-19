import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import fs from 'node:fs'
import type { IncomingMessage, ServerResponse } from 'node:http'
import path from 'node:path'
import type { Plugin, ViteDevServer } from 'vite'

const cacheDir = path.resolve(__dirname, 'data')
const cardCacheFile = path.join(cacheDir, 'riftbound-cards.json')
const deckCacheFile = path.join(cacheDir, 'riftbound-decks.json')
const apiProxyTarget =
  process.env.VITE_API_PROXY_TARGET ?? process.env.services__server__http__0 ?? process.env.services__server__https__0

function localJsonCache(route: string, cacheFile: string): Plugin {
  return {
    name: `local-json-cache-${route.replace(/\W+/g, '-')}`,
    configureServer(server: ViteDevServer) {
      server.middlewares.use(route, (req: IncomingMessage, res: ServerResponse) => {
        res.setHeader('Content-Type', 'application/json')

        if (req.method === 'GET') {
          if (!fs.existsSync(cacheFile)) {
            res.end(JSON.stringify({ data: [] }))
            return
          }
          res.end(fs.readFileSync(cacheFile, 'utf8'))
          return
        }

        if (req.method === 'POST') {
          let body = ''
          req.on('data', (chunk: Buffer) => {
            body += chunk
          })
          req.on('end', () => {
            fs.mkdirSync(cacheDir, { recursive: true })
            fs.writeFileSync(cacheFile, body, 'utf8')
            res.end(JSON.stringify({ ok: true, file: cacheFile }))
          })
          return
        }

        res.statusCode = 405
        res.end(JSON.stringify({ error: 'Method not allowed' }))
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), localJsonCache('/api/local-cards', cardCacheFile), localJsonCache('/api/local-decks', deckCacheFile)],
  server: {
    proxy: apiProxyTarget
      ? {
          '/api/v1': {
            target: apiProxyTarget,
            changeOrigin: true,
            secure: false,
          },
          '/hubs': {
            target: apiProxyTarget,
            changeOrigin: true,
            secure: false,
            ws: true,
          },
        }
      : undefined,
  },
})
