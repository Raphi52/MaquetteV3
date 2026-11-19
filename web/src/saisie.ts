// Saisies NUMERIQUES des ecrans portes : le meme comportement que le @bind
// Blazor d'un int ou d'un decimal.
//
// Blazor relisait le texte du champ ; s'il etait illisible (vide, « 12a »), la
// valeur gardait sa derniere forme lisible -- jamais remise a zero. On garde donc
// DEUX choses : le texte affiche, tel que tape, et la derniere valeur lisible,
// la seule que l'ecran utilise (lot 1, Factures : « Montant encaisse »).
//
// La lecture d'un entier est celle de criteres.ts (entierLisible, lot 5) ; ce
// fichier n'ajoute que la lecture d'un decimal et l'etat « texte + valeur ».

import { useState } from 'react'
import { entierLisible } from './criteres'

/**
 * La valeur que le `@bind` d'un `decimal` retenait d'un champ type="number" :
 * le nombre ecrit (point decimal, culture invariante comme le convertisseur
 * de Blazor pour ces champs), ou null si le texte n'en est pas un.
 */
export function decimalLisible(texte: string): number | null {
  const t = texte.trim()
  return /^[-+]?(\d+\.?\d*|\.\d+)$/.test(t) ? Number(t) : null
}

export interface NombreSaisi {
  /** Le texte du champ, tel que tape. */
  texte: string
  /** La derniere valeur lisible. */
  valeur: number
  /** A brancher sur onChange : le texte suit la frappe, la valeur seulement si elle est lisible. */
  saisir: (texte: string) => void
  /** Remet le champ a une valeur (bouton « Effacer », « Vider »). */
  poser: (valeur: number) => void
}

export function useNombreSaisi(initiale: number, lire: (texte: string) => number | null = entierLisible): NombreSaisi {
  const [texte, setTexte] = useState(String(initiale))
  const [valeur, setValeur] = useState(initiale)
  return {
    texte,
    valeur,
    saisir: (t) => {
      setTexte(t)
      const v = lire(t)
      if (v !== null) setValeur(v)
    },
    poser: (v) => {
      setTexte(String(v))
      setValeur(v)
    },
  }
}

/** La valeur d'un champ type="date" pour le pont : null quand il est vide. */
export function dateSaisie(texte: string): string | null {
  return texte === '' ? null : texte
}
