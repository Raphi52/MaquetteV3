// LA RECHERCHE COMMUNE des formes qui retrouvent un objet avant d'agir dessus (Consultation, Fiche) : les criteres de
// la fiche RIG v2 dans le panneau de gauche, la liste des resultats a droite (liste moderne), une ligne cliquee
// s'ouvre. Les criteres filtrent les lignes a l'ecran : chaque critere rempli doit se lire dans la ligne.
// DANS LA BASE (plan « RIG v3 100 % fonctionnel », lot 2) : quand un lecteur de l'ecran sait chercher (vue.recherche),
// le texte et les criteres partent aussi au serveur, un instant apres la frappe (useLignes().chercher) ; les lignes ainsi
// cherchees (vue.cherchees, pour CETTE recherche) ne sont pas refiltrees a l'ecran : la base a deja choisi.

import { useEffect, useState } from 'react'
import { EnteteListe, ListeModerne, normaliser, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  PanneauCriteres, Provenance, TableLignes, memeRecherche, passeCriteres, synthese, texteLigne, useLignes,
  type LigneTenue, type Saisies,
} from './commun'

/** Le delai entre la derniere frappe et la recherche dans la base. */
export const DELAI_RECHERCHE = 350

export function Resultats({ d, lignes: lues, ouvrir, forme, invite }: {
  d: DescripteurEcranVue; lignes: ReturnType<typeof useLignes>; ouvrir: (l: LigneTenue) => void; forme: string; invite: string
}) {
  const { vue, lignes, chercher, cherchee } = lues
  const [texte, poserTexte] = useState('')
  const [criteres, poserCriteres] = useState<Saisies>({})
  const enBase = vue?.recherche === true
  useEffect(() => {
    if (!enBase) return
    const minuterie = setTimeout(() => chercher({ texte, criteres }), DELAI_RECHERCHE)
    return () => clearTimeout(minuterie)
  }, [enBase, texte, criteres, chercher])
  const cherche = normaliser(texte.trim())
  const dejaCherchees = memeRecherche(cherchee, { texte, criteres }) ? vue?.cherchees ?? [] : []
  const visibles = lignes.filter((l) => dejaCherchees.includes(l.groupe)
    || (passeCriteres(l, criteres) && (cherche === '' || normaliser(texteLigne(l)).includes(cherche))))
  return (
    <div className="rig-proc__corps rig-eg" data-forme={forme}>
      <PanneauCriteres d={d} criteres={criteres} changer={(c, v) => poserCriteres((avant) => ({ ...avant, [c]: v }))}
                       effacer={() => poserCriteres({})} />
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche,
                               valider: () => { const premiere = visibles[0]; if (premiere) ouvrir(premiere) } }} />
        {vue && <Provenance vue={vue} />}
        {enBase && <p className="rig-eg-note" role="note">La recherche et les critères cherchent aussi dans la base du greffe.</p>}
        <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} ouvrir={ouvrir}
                     filtre={cherche !== '' || Object.values(criteres).some((v) => v.trim() !== '')} />
        <PiedListe aide={invite}>
          <span><b>{visibles.length}</b> résultat{visibles.length > 1 ? 's' : ''}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
