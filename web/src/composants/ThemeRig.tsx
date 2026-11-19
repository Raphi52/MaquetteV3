// LE THEME D'AFFICHAGE sur la page entiere : data-rig-theme pose sur <html>.
//
// Le theme ne peut pas rester porte par le seul accueil : sans lui sur <html>,
// le fond de la page hote reste sombre et cerne le mode clair d'un cadre noir.
// Cette ile ne dessine rien ; Home.razor la monte dans TOUTES ses fenetres
// (une fenetre d'onglet sorti n'a ni barre ni accueil, mais garde le theme).
// Le fond de la fenetre WPF, lui, reste pose cote C# (FenetreInterop.AppliquerFond).
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis
// appliquerTheme de wwwroot/rig-accueil.js.

import { useEffect } from 'react'

export function ThemeRig({ theme }: { theme: string }) {
  useEffect(() => {
    document.documentElement.setAttribute('data-rig-theme', theme)
  }, [theme])
  return null
}
