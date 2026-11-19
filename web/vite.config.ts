import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { defineConfig, type Plugin } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Les ecrans React sont servis par la fenetre comme n'importe quel fichier de
// wwwroot : un SEUL module, a nom FIXE (wwwroot/app/rig-iles.js), que
// wwwroot/index.html charge. Pas d'empreinte dans le nom : index.html le nomme
// en dur, et la cible ConstruireWeb du .csproj le prend pour temoin.
//
// UNE construction = UNE version de React, celle de production, decidee ICI
// et pas par l'environnement du poste. Mesure du 18/11/2026 : ce poste a
// NODE_ENV=development en variable globale ; Vite compilait alors le JSX pour
// le React de developpement (jsxDEV) pendant que define ci-dessous embarquait
// celui de production -- ecran vide, « o.jsxDEV is not a function ». Les deux
// reglages suivent donc la meme valeur, fixee par la commande.
// Les tests (vitest) et le serveur de developpement (commande « serve »)
// gardent le React de developpement : act() n'existe pas en production, et la
// reprise a chaud des composants (React Refresh) non plus.

const wwwroot = fileURLToPath(new URL('../wwwroot/', import.meta.url))

/** Le module construit, tel que wwwroot/index.html le nomme. */
const MODULE_CONSTRUIT = 'app/rig-iles.js'

/**
 * SERVEUR DE DEVELOPPEMENT (npm run dev, lance par dev.ps1) : la fenetre RIG
 * charge la page depuis Vite (variable RIGV3_VITE, Pont/PageWeb.cs), qui
 * reprend un .tsx modifie A CHAUD, sans recharger la page ni relancer l'appli.
 * La page est LE MEME wwwroot/index.html qu'en production : seul le module
 * construit y est remplace par la source (src/main.ts), et Vite y ajoute son
 * client de reprise a chaud. Les feuilles de style et documents de wwwroot
 * sont servis tels quels (publicDir).
 */
function pageDeDeveloppement(): Plugin {
  return {
    name: 'rig-page-de-developpement',
    apply: 'serve',
    configureServer(serveur) {
      serveur.middlewares.use(async (requete, reponse, suivant) => {
        const chemin = (requete.url ?? '/').split('?')[0]
        if (chemin !== '/' && chemin !== '/index.html') { suivant(); return }
        try {
          const brut = await readFile(wwwroot + 'index.html', 'utf8')
          if (!brut.includes(MODULE_CONSTRUIT)) throw new Error(`wwwroot/index.html ne charge plus ${MODULE_CONSTRUIT}`)
          const html = await serveur.transformIndexHtml('/index.html', brut.replace(MODULE_CONSTRUIT, '/src/main.ts'))
          reponse.setHeader('Content-Type', 'text/html; charset=utf-8')
          reponse.setHeader('Cache-Control', 'no-store')
          reponse.end(html)
        } catch (erreur) {
          suivant(erreur)
        }
      })
    },
  }
}

export default defineConfig(({ command }) => {
  const production = command === 'build'
  return {
    plugins: [react(), pageDeDeveloppement()],
    // Le mode bibliotheque ne remplace pas process.env.NODE_ENV : sans cette
    // ligne, la WebView casse sur « process is not defined ».
    define: production ? { 'process.env.NODE_ENV': JSON.stringify('production') } : {},
    oxc: { jsx: { runtime: 'automatic', development: !production } },
    // En construction, rien a recopier : la sortie EST dans wwwroot.
    publicDir: production ? false : wwwroot,
    server: {
      // 127.0.0.1 et pas « localhost » : Node peut n'ecouter que sur ::1, et la
      // fenetre charge http://127.0.0.1:<port>/ (dev.ps1 choisit le port).
      host: '127.0.0.1',
      strictPort: true,
    },
    build: {
      outDir: '../wwwroot/app',
      emptyOutDir: true,
      sourcemap: true,
      lib: {
        entry: 'src/main.ts',
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
