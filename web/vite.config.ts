import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Les ecrans React sont servis par la fenetre comme n'importe quel fichier de
// wwwroot : un SEUL module, a nom FIXE (wwwroot/app/rig-iles.js), que
// Components/IleReact.razor importe. Pas d'empreinte dans le nom : le
// manifeste des fichiers statiques de .NET liste les fichiers a la
// compilation, un nom qui change a chaque build y manquerait.
//
// UNE construction = UNE version de React, celle de production, decidee ICI
// et pas par l'environnement du poste. Mesure du 18/11/2026 : ce poste a
// NODE_ENV=development en variable globale ; Vite compilait alors le JSX pour
// le React de developpement (jsxDEV) pendant que define ci-dessous embarquait
// celui de production -- ecran vide, « o.jsxDEV is not a function ». Les deux
// reglages suivent donc la meme valeur, fixee par la commande.
// Les tests (vitest, commande « serve ») gardent le React de developpement :
// act() n'existe pas en production.
export default defineConfig(({ command }) => {
  const production = command === 'build'
  return {
    plugins: [react()],
    // Le mode bibliotheque ne remplace pas process.env.NODE_ENV : sans cette
    // ligne, la WebView casse sur « process is not defined ».
    define: production ? { 'process.env.NODE_ENV': JSON.stringify('production') } : {},
    oxc: { jsx: { runtime: 'automatic', development: !production } },
    build: {
      outDir: '../wwwroot/app',
      emptyOutDir: true,
      sourcemap: true,
      lib: {
        entry: 'src/iles.ts',
        formats: ['es'],
        fileName: () => 'rig-iles.js',
      },
    },
    test: {
      environment: 'jsdom',
      include: ['src/**/*.test.{ts,tsx}'],
    },
  }
})
