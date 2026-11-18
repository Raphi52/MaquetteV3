// Point d'entree du module wwwroot/app/rig-iles.js : ce que
// Components/IleReact.razor appelle pour monter, mettre a jour et demonter
// un ecran React dans le div qu'il lui donne.
//
// Le REGISTRE est automatique : chaque fichier src/ecrans/<Nom>.tsx et
// src/composants/<Nom>.tsx exporte un composant nomme comme lui, et devient
// montable sous ce nom. Aucune liste a tenir a la main : deux lots qui portent
// deux ecrans en meme temps ne se marchent pas dessus dans ce fichier.

import { createElement, type ComponentType } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { rappelBlazor, type ReferenceBlazor } from './pont'

type Proprietes = Record<string, unknown>

const modules = import.meta.glob<Record<string, unknown>>(
  ['./ecrans/*.tsx', './composants/*.tsx', '!./**/*.test.tsx'],
  { eager: true },
)

/** Nom du fichier sans dossier ni extension : « ./ecrans/EcranVerrous.tsx » -> « EcranVerrous ». */
function nomDeFichier(chemin: string): string {
  return chemin.slice(chemin.lastIndexOf('/') + 1).replace(/\.tsx$/, '')
}

const composants = new Map<string, ComponentType<Proprietes>>()
for (const [chemin, module] of Object.entries(modules)) {
  const nom = nomDeFichier(chemin)
  const composant = module[nom]
  if (typeof composant === 'function') composants.set(nom, composant as ComponentType<Proprietes>)
}

/** Les noms montables, dans l'ordre alphabetique (diagnostic et tests). */
export function composantsMontables(): string[] {
  return [...composants.keys()].sort()
}

interface Ile {
  racine: Root
  composant: ComponentType<Proprietes>
  rappels: Proprietes
}

const iles = new WeakMap<Element, Ile>()

function rendre(ile: Ile, proprietes: Proprietes): void {
  ile.racine.render(createElement(ile.composant, { ...proprietes, ...ile.rappels }))
}

/**
 * Monte le composant `nom` dans `hote`. `nomsRappels` liste les proprietes
 * qui sont des gestes remontes a Blazor : chacune devient une fonction.
 */
export function monter(
  hote: Element,
  nom: string,
  proprietes: Proprietes,
  nomsRappels: string[],
  reference: ReferenceBlazor,
): void {
  const composant = composants.get(nom)
  if (!composant) {
    throw new Error(`Ile React inconnue : « ${nom} ». Montables : ${composantsMontables().join(', ')}.`)
  }
  demonter(hote)
  const rappels: Proprietes = {}
  for (const r of nomsRappels) rappels[r] = rappelBlazor(reference, r)
  const ile: Ile = { racine: createRoot(hote), composant, rappels }
  iles.set(hote, ile)
  rendre(ile, proprietes)
}

/** Les parametres de la coquille ont change : on rend a nouveau, l'etat React est garde. */
export function mettreAJour(hote: Element, proprietes: Proprietes): void {
  const ile = iles.get(hote)
  if (ile) rendre(ile, proprietes)
}

/** La coquille quitte l'ecran : React rend le div vide, propre, avant que Blazor le retire. */
export function demonter(hote: Element): void {
  const ile = iles.get(hote)
  if (!ile) return
  iles.delete(hote)
  ile.racine.unmount()
}
