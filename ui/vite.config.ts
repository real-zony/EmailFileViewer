import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Tauri expects a fixed dev server port (see devUrl in src-tauri/tauri.conf.json).
  server: {
    port: 1420,
    strictPort: true,
  },
  // Keep Rust compiler errors visible in the terminal.
  clearScreen: false,
})
