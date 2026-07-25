import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Dev proxy target: the ASP.NET Core API has no launchSettings.json in this
// repo, so `dotnet run` binds Kestrel's bare default, http://localhost:5000
// (confirmed by actually running `dotnet run --project src/EventFinder.Api`
// and reading its "Now listening on" log line). Override via
// EVENTFINDER_API_PROXY_TARGET if the API is ever run on a different port.
const apiProxyTarget = process.env.EVENTFINDER_API_PROXY_TARGET ?? 'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: apiProxyTarget,
        changeOrigin: true,
      },
    },
  },
})
