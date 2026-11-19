// Le REGISTRE des composants montables a la racine d'une fenetre (racine.tsx) :
// le C# nomme le composant (RacineWeb.Composant, Pont/Racines.cs), la page le
// trouve ici.
//
// Le registre est automatique : chaque fichier src/ecrans/<Nom>.tsx et
// src/composants/<Nom>.tsx exporte un composant nomme comme lui, et devient
// montable sous ce nom. Aucune liste a tenir a la main : deux lots qui portent
// deux ecrans en meme temps ne se marchent pas dessus dans ce fichier.
// (Jusqu'au 19/11/2026, ce module montait aussi les « iles » React dans les
// coquilles Blazor -- d'ou son nom ; Blazor est retire.)

import type { ComponentType } from 'react'

export type Proprietes = Record<string, unknown>

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

/** Le composant `nom` ; un nom inconnu echoue en listant les noms montables. */
export function composantMontable(nom: string): ComponentType<Proprietes> {
  const composant = composants.get(nom)
  if (!composant) {
    throw new Error(`Composant inconnu : « ${nom} ». Montables : ${composantsMontables().join(', ')}.`)
  }
  return composant
}
