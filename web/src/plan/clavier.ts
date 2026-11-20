// LE CLAVIER (ex-activerClavier de wwwroot/rig-accueil.js, porte au lot 2 du
// plan « finir le passage a React », 19/11/2026). RIG v2 se conduit au
// clavier. Premier pas ici : Echap ferme ce qui est ouvert. L'ecoute est posee
// sur le DOCUMENT (et non sur un element focusable) parce que le focus peut
// etre n'importe ou -- dans une grille, un champ, ou nulle part apres un clic
// sur le fond. Ce qui se ferme (l'affaire d'abord, l'ecran ensuite) est decide
// par le C# (SessionPlanTravail.ToucheEchap).
//
// UNE TOUCHE DEJA TRAITEE NE L'EST PAS DEUX FOIS (plan « Vue du dossier = coeur
// des actions », lot 5, 20/11/2026). Un composant qui consomme une touche --
// un menu, la liste des points a corriger, l'aide des raccourcis -- appelle
// preventDefault ; l'ecoute du document la laisse alors passer. Avant, Echap
// dans la liste des points a corriger fermait TOUTE l'affaire.
//
// LES RACCOURCIS D'UNE LETTRE de la page du dossier (Metier/ActionsDossier.cs,
// web/src/plan/ActionsDossier.tsx) et « ? » pour leur aide (AideClavier.tsx).
// Les touches deja prises, relevees avant de choisir (20/11/2026) :
//   - Ctrl K : la palette (Application.tsx ; Commandes web la garde pour sa recherche) ;
//   - Echap : ce module, puis le C# ; la palette, un menu de la barre, l'aide ;
//   - la palette ouverte : fleches, Entree, Tab, Ctrl M, Ctrl C ;
//   - un menu de la barre ouvert : la lettre de ses actions (elle la joue la premiere) ;
//   - la saisie des etapes et la saisie guidee : Entree, Maj+Entree, Echap, et toute
//     frappe dans leurs champs ; la zone des etapes : les touches de defilement ;
//   - le filtre de l'arbre, le renommage d'un noeud, la note du fil : des champs ;
//   - les poignees de colonne : fleches gauche/droite.
// Une lettre seule n'est donc prise QUE dans un champ ou un menu ouvert : les
// raccourcis ignorent toute frappe dans un champ (une liste <select> saute a
// l'option de la lettre), toute touche deja traitee, et toute combinaison
// Ctrl, Alt ou Cmd (AltGr arrive en Ctrl+Alt : les caracteres speciaux passent).

/** Branche Echap ; rend la fonction qui retire l'ecoute (arreterClavier). */
export function ecouterEchap(echap: () => void, doc: Document = document): () => void {
  const surTouche = (e: KeyboardEvent) => {
    // Deja traitee par un composant (menu, liste, aide) : elle ne ferme que lui.
    if (e.key !== 'Escape' || e.defaultPrevented) return
    echap()
  }
  doc.addEventListener('keydown', surTouche)
  return () => doc.removeEventListener('keydown', surTouche)
}

/** Echap pour la couche du DESSUS (l'aide des raccourcis) : ecoute a la CAPTURE, avant tout
 *  autre ecouteur et ou que soit le focus, la touche est marquee traitee -- ecouterEchap la
 *  laisse alors passer, l'affaire dessous reste ouverte. Rend la fonction qui retire l'ecoute. */
export function ecouterEchapDabord(fermer: () => void, doc: Document = document): () => void {
  const surTouche = (e: KeyboardEvent) => {
    if (e.key !== 'Escape' || e.defaultPrevented) return
    e.preventDefault()
    fermer()
  }
  doc.addEventListener('keydown', surTouche, true)
  return () => doc.removeEventListener('keydown', surTouche, true)
}

/** La frappe ecrit-elle dans un champ ? Un <select> compte : sa lettre y choisit une option. */
export function dansUneSaisie(cible: EventTarget | null): boolean {
  if (!(cible instanceof Element)) return false
  if ((cible as HTMLElement).isContentEditable) return true
  return cible.closest('input, textarea, select, [contenteditable]:not([contenteditable="false"])') !== null
}

/** La touche d'un raccourci : une lettre en capitale (« F »), ou « ? » pour l'aide ;
 *  null pour toute autre frappe. Une touche tenue ne rejoue pas l'action. */
export function toucheDeRaccourci(e: KeyboardEvent): string | null {
  if (e.defaultPrevented || e.repeat || e.isComposing || e.ctrlKey || e.altKey || e.metaKey) return null
  if (dansUneSaisie(e.target)) return null
  if (e.key === '?') return '?'
  return /^[a-z]$/i.test(e.key) ? e.key.toUpperCase() : null
}

/** Branche les raccourcis : `agir` recoit la touche (toucheDeRaccourci) et rend vrai
 *  s'il l'a jouee -- elle est alors consommee. Rend la fonction qui retire l'ecoute. */
export function ecouterRaccourcis(agir: (touche: string) => boolean, doc: Document = document): () => void {
  const surTouche = (e: KeyboardEvent) => {
    const touche = toucheDeRaccourci(e)
    if (touche !== null && agir(touche)) e.preventDefault()
  }
  doc.addEventListener('keydown', surTouche)
  return () => doc.removeEventListener('keydown', surTouche)
}
