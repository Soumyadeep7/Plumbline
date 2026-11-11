import { defineConfig } from "vite";

// The dev server forwards /api calls to the .NET backend on port 5080.
// esbuild compiles the React Bits .jsx components with the automatic JSX runtime.
export default defineConfig({
  esbuild: { jsx: "automatic" },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5080",
    },
  },
});
