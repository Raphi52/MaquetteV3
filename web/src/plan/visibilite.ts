// UN ECRAN D'ONGLET PEUT ETRE CACHE SANS ETRE DETRUIT (19/11/2026, conv-2017).
//
// Les ecrans des onglets ouverts restent montes quand on change d'onglet
// (Application.tsx) : ils gardent leur etat, comme un onglet de navigateur.
// Un ecran cache ne se voit pas, mais ses ecoutes globales (clavier sur
// window...) tournent encore : il lit ce contexte pour les suspendre, sinon il
// volerait une touche a l'ecran montre. Hors d'un onglet (tests, fenetre d'un
// onglet sorti), un ecran est visible.

import { createContext } from 'react'

export const EcranVisible = createContext(true)
