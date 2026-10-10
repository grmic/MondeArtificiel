import { defineConfig } from "vite";

// Le client est servi par le serveur .NET : le build va dans son dossier wwwroot.
// En mode `npm run dev`, Vite relaie l'API et le hub vers le serveur .NET (port 5080).
export default defineConfig({
  build: {
    outDir: "../src/Monde.Serveur/wwwroot",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5080",
      "/hub": { target: "http://localhost:5080", ws: true },
    },
  },
});
