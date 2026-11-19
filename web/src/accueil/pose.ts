// LA POSE DES TUILES : la geometrie du glisser-deposer de la grille.
//
// Repris de wwwroot/rig-accueil.js (activerPose, lot 1 du plan « finir le
// passage a React ») : seul le navigateur connait la geometrie reelle de la
// grille, donc la case visee se calcule ici ; la position finale (colonne,
// ligne, largeur, hauteur) est renvoyee au C# qui, lui, la garde
// (SessionAccueil.PoserTuile / DimensionnerTuile, puis accueil.json).
//
// Les bornes sont LES MEMES que celles du C# : une tuile lachee arrive donc
// exactement ou le C# la reposera, sans saut au retour de la vue.

export const COLONNES = 12
export const ECART = 12 // doit valoir le gap de .rig-grille
export const HAUTEUR_LIGNE = 116 // doit valoir grid-auto-rows de .rig-grille
export const LARGEUR_MIN = 3 // en colonnes
export const HAUTEUR_MIN = 1 // en lignes

/** Une pose de tuile sur la grille (cases de 1 a 12, lignes a partir de 1). */
export interface PoseCase {
  colonne: number
  ligne: number
  largeur: number
  hauteur: number
}

/** Le pas horizontal d'une colonne (largeur + ecart), pour une grille de cette largeur. */
export function pasColonne(largeurGrille: number): number {
  return (largeurGrille - ECART * (COLONNES - 1)) / COLONNES + ECART
}

/** Deplacement : la case de depart decalee du mouvement du pointeur, bornee a la grille. */
export function deplacer(depart: PoseCase, dx: number, dy: number, pas: number): PoseCase {
  const dCol = Math.round(dx / pas)
  const dLigne = Math.round(dy / (HAUTEUR_LIGNE + ECART))
  const maxCol = COLONNES - depart.largeur + 1
  return {
    ...depart,
    colonne: Math.min(Math.max(depart.colonne + dCol, 1), Math.max(maxCol, 1)),
    ligne: Math.max(depart.ligne + dLigne, 1),
  }
}

/** Redimensionnement par le coin bas-droit : 3 colonnes au moins, jamais au-dela du bord droit. */
export function dimensionner(depart: PoseCase, dx: number, dy: number, pas: number): PoseCase {
  const dCol = Math.round(dx / pas)
  const dLigne = Math.round(dy / (HAUTEUR_LIGNE + ECART))
  const maxLarg = COLONNES - depart.colonne + 1
  return {
    ...depart,
    largeur: Math.min(Math.max(depart.largeur + dCol, LARGEUR_MIN), Math.max(maxLarg, LARGEUR_MIN)),
    hauteur: Math.max(depart.hauteur + dLigne, HAUTEUR_MIN),
  }
}

export function memePose(a: PoseCase, b: PoseCase): boolean {
  return a.colonne === b.colonne && a.ligne === b.ligne && a.largeur === b.largeur && a.hauteur === b.hauteur
}
