// COMPARER deux captures d'un ecran porte : avant (Blazor) / apres (React).
//
//   npm run comparer -- <avant.png> <apres.png> [--haut <px>] [--sortie <diff.png>]
//
// Rend le nombre et la part de pixels qui different, et ecrit une image ou ils
// sont marques en rouge (par defaut <apres>-diff.png). --haut ignore une bande
// en haut de l'image (ex. la barre d'onglets, qui change d'un lancement a
// l'autre sans rapport avec l'ecran compare).
//
// Pourquoi un outil : la preuve d'un portage (web/README.md) est « meme rendu
// qu'avant ». A l'oeil, un pixel de decalage ou une police qui change passe ;
// ici il se compte. Code de sortie : 0 si aucune difference, 1 sinon,
// 2 sur une erreur d'usage (tailles differentes, fichier absent).

import { readFileSync, writeFileSync } from 'node:fs'
import { PNG } from 'pngjs'
import pixelmatch from 'pixelmatch'

const args = process.argv.slice(2)
const options = { haut: 0, sortie: '' }
const fichiers = []
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--haut') options.haut = Number(args[++i])
  else if (args[i] === '--sortie') options.sortie = args[++i] ?? ''
  else fichiers.push(args[i])
}

if (fichiers.length !== 2 || !Number.isInteger(options.haut) || options.haut < 0) {
  console.error('Usage : npm run comparer -- <avant.png> <apres.png> [--haut <px>] [--sortie <diff.png>]')
  process.exit(2)
}

const [cheminAvant, cheminApres] = fichiers
const avant = PNG.sync.read(readFileSync(cheminAvant))
const apres = PNG.sync.read(readFileSync(cheminApres))
if (avant.width !== apres.width || avant.height !== apres.height) {
  console.error(`Tailles differentes : ${avant.width}x${avant.height} contre ${apres.width}x${apres.height}.`)
  process.exit(2)
}

const { width, height } = avant
const haut = Math.min(options.haut, height)
const hauteur = height - haut

/** La bande comparee, sans les `haut` premieres lignes. */
function bande(png) {
  return png.data.subarray(haut * width * 4)
}

const diff = new PNG({ width, height: hauteur })
// threshold 0 : on veut TOUTE difference, un portage n'a pas de tolerance.
const differents = pixelmatch(bande(avant), bande(apres), diff.data, width, hauteur, { threshold: 0 })
const sortie = options.sortie || cheminApres.replace(/\.png$/i, '') + '-diff.png'
writeFileSync(sortie, PNG.sync.write(diff))

const total = width * hauteur
const part = ((differents / total) * 100).toFixed(3)

// OU sont les differences : le rectangle qui les contient, en pixels de la
// capture ENTIERE (bande du haut comprise). Sans lui, il fallait relire l'image
// rouge a l'oeil pour savoir quelle ligne de l'ecran avait bouge (lot 3).
let boite = null
for (let y = 0; y < hauteur; y++) {
  for (let x = 0; x < width; x++) {
    const i = (y * width + x) * 4
    let ecart = 0
    for (let k = 0; k < 3; k++) {
      ecart = Math.max(ecart, Math.abs(avant.data[(haut * width * 4) + i + k] - apres.data[(haut * width * 4) + i + k]))
    }
    if (ecart === 0) continue
    if (!boite) boite = { x0: x, y0: y + haut, x1: x, y1: y + haut, ecartMax: ecart }
    boite.x0 = Math.min(boite.x0, x); boite.x1 = Math.max(boite.x1, x)
    boite.y0 = Math.min(boite.y0, y + haut); boite.y1 = Math.max(boite.y1, y + haut)
    boite.ecartMax = Math.max(boite.ecartMax, ecart)
  }
}

console.log(JSON.stringify({ avant: cheminAvant, apres: cheminApres, zone: `${width}x${hauteur} (bande du haut ignoree : ${haut} px)`, pixelsDifferents: differents, part: `${part} %`, boite, image: sortie }))
process.exit(differents === 0 ? 0 : 1)
