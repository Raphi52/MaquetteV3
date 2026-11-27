// Les annonces BODACC que cherchent la palette Ctrl K (composants/Palette.tsx) et la page Recherche (EcranRecherche.tsx).
//
// Plan « RIG v3 100 % fonctionnel », lot 20 (27/11/2026) : elles cherchaient les annonces d'EXEMPLE de l'ecran BODACC
// (EcranBodacc.tsx), meme branchees au serveur. Elles demandent maintenant au pont (Pont/EcranBodacc.cs, Chercher) :
//   « serveur »  les annonces de la base du greffe dont chaque mot tape se retrouve dans l'annonce
//                (Serveur/Socle/EcransMain/LectureBodacc.cs, ChercherAsync : la requete de l'ecran BODACC, sans sa periode) ;
//   « demo »     sans serveur : les annonces d'exemple de l'ecran, que la liste marque « exemple » ;
//   autre        greffe absent, acces refuse, base muette : aucune annonce -- jamais l'exemple a la place de la base.
// Le filtre de la palette (annonceRepond) s'applique ensuite comme avant : la base rend des candidates, la palette garde
// celles ou chaque mot se retrouve, accents et casse ignores.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { AnnoncesBodacc, type AnnonceBodacc } from './EcranBodacc'
import type { ModeLecture } from '../provenance'

/** Ce que rend le pont (Pont/EcranBodacc.cs, VueRechercheBodacc). */
export interface VueRechercheBodacc {
  mode: ModeLecture
  message: string | null
  annonces: AnnonceBodacc[]
  total: number
}

/** Les annonces a filtrer pour une saisie : celles de la base, ou l'exemple (sans serveur). */
export interface AnnoncesCherchees {
  annonces: AnnonceBodacc[]
  /** Vrai sans serveur : ce sont les annonces d'exemple de l'ecran BODACC. */
  exemple: boolean
}

const Aucune: AnnoncesCherchees = { annonces: [], exemple: false }

/** Le delai apres la derniere frappe avant de demander a la base (ms) : la palette cherche a chaque touche. */
export const DelaiRecherche = 150

/** La saisie vaut-elle une recherche ? Trois caracteres hors espaces, comme annonceRepond. */
const chercheQuelqueChose = (texte: string) => texte.replace(/\s/g, '').length >= 3

/** Les annonces ou chercher <paramref name="texte"/> : demandees au pont apres une courte pause de frappe. */
export function useAnnoncesCherchees(texte: string): AnnoncesCherchees {
  const [trouvees, poser] = useState<AnnoncesCherchees>(Aucune)
  useEffect(() => {
    if (!chercheQuelqueChose(texte)) { poser(Aucune); return }
    let perime = false
    const minuteur = window.setTimeout(() => {
      appeler<VueRechercheBodacc>('EcranBodacc.Chercher', texte).then(
        (v) => {
          if (perime) return
          poser(v.mode === 'serveur' ? { annonces: v.annonces, exemple: false }
            : v.mode === 'demo' ? { annonces: AnnoncesBodacc, exemple: true } : Aucune)
        },
        // Pont absent ou en panne : aucune annonce, plutot que l'exemple a la place de la base.
        () => { if (!perime) poser(Aucune) },
      )
    }, DelaiRecherche)
    return () => { perime = true; window.clearTimeout(minuteur) }
  }, [texte])
  return trouvees
}
