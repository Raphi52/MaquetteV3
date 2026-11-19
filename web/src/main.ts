// Point d'entree de la page (wwwroot/index.html charge wwwroot/app/rig-iles.js,
// construit depuis ce fichier ; sous dev.ps1, le serveur Vite sert ce fichier
// lui-meme). Il monte la racine de la fenetre dans #app : voir racine.tsx.

import { demarrer } from './racine'

const app = document.getElementById('app')
if (app) void demarrer(app)
