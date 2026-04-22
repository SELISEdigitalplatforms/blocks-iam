import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Production build output is served by ASP.NET from ../server/Api/wwwroot (build via ./run.sh or `npm run build`).
// Optional: `npm run dev` in client/ for local UI work without dotnet (no API unless you configure it yourself).
export default defineConfig({
  envPrefix: ['BLOCKS_'],
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../server/Api/wwwroot',
    emptyOutDir: true,
  },
});
