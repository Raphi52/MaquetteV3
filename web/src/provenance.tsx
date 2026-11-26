// D'ou viennent les lignes d'un ECRAN FAIT MAIN (plan « RIG v3 100 % fonctionnel », lot 17, 26/11/2026) : Demandes,
// Arrivee du courrier, Teletransmissions, BODACC, Ouvrir un dossier. Leur pont (Pont/LectureEcranMain.cs) dit le mode :
//   « serveur »  les lignes viennent de la base du greffe : rien a dire, l'ecran est inchange ;
//   « demo »     sans serveur, la demonstration s'affiche : la mention « exemple » des ecrans generes (formes/commun.tsx) ;
//   autre        greffe absent, acces refuse, base ou serveur muet : le motif, en alerte, et aucune ligne.
// Une seule ligne au-dessus de la liste : la mise en page de l'ecran ne change pas.

import { Exemple } from './ecrans/formes/commun'
import { PointEtat } from './composants/ListeModerne'

/** Le mode de lecture d'un ecran fait main (Pont/LectureEcranMain.cs). */
export type ModeLecture = 'serveur' | 'demo' | 'sans-greffe' | 'refuse' | 'erreur'

/** La provenance des lignes : rien en base, « exemple » en demonstration, le motif d'une panne sinon. */
export function ProvenanceEcran({ mode, message, quoi }: {
  /** Absent : un pont d'avant ce lot, l'ecran ne dit rien. */
  mode?: ModeLecture | undefined
  message?: string | null | undefined
  /** Ce que montre l'ecran, au pluriel : « Demandes », « Annonces »… */
  quoi: string
}) {
  if (mode === undefined || mode === 'serveur') return null
  if (mode === 'demo') {
    return (
      <p className="rig-eg-note" role="note">
        <Exemple /> {quoi} de démonstration : sans serveur, aucune ne vient de la base du greffe.
      </p>
    )
  }
  const etat = mode === 'refuse' ? 'Accès refusé' : mode === 'sans-greffe' ? 'Aucun greffe ouvert' : 'Base injoignable'
  return (
    <p className="rig-eg-note" role="alert">
      <PointEtat teinte="rouge">{etat}</PointEtat>{' '}{message}
    </p>
  )
}
