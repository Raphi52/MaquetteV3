// LE CLAVIER (ex-activerClavier de wwwroot/rig-accueil.js, porte au lot 2 du
// plan « finir le passage a React », 19/11/2026). RIG v2 se conduit au
// clavier. Premier pas ici : Echap ferme ce qui est ouvert. L'ecoute est posee
// sur le DOCUMENT (et non sur un element focusable) parce que le focus peut
// etre n'importe ou -- dans une grille, un champ, ou nulle part apres un clic
// sur le fond. Ce qui se ferme (l'affaire d'abord, l'ecran ensuite) est decide
// par le C# (SessionPlanTravail.ToucheEchap).

/** Branche Echap ; rend la fonction qui retire l'ecoute (arreterClavier). */
export function ecouterEchap(echap: () => void, doc: Document = document): () => void {
  const surTouche = (e: KeyboardEvent) => {
    if (e.key !== 'Escape') return
    echap()
  }
  doc.addEventListener('keydown', surTouche)
  return () => doc.removeEventListener('keydown', surTouche)
}
