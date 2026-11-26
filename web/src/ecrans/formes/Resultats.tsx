// LA RECHERCHE COMMUNE des formes qui retrouvent un objet avant d'agir dessus (Consultation, Fiche) : les criteres de
// la fiche RIG v2 dans le panneau de gauche, la liste des resultats a droite (liste moderne), une ligne cliquee
// s'ouvre. Les criteres filtrent les lignes a l'ecran : chaque critere rempli doit se lire dans la ligne.
// DANS LA BASE (plan « RIG v3 100 % fonctionnel », lot 2) : quand un lecteur de l'ecran sait chercher (vue.recherche),
// le texte et les criteres partent aussi au serveur, un instant apres la frappe ; les lignes ainsi cherchees ne sont pas
// refiltrees a l'ecran : la base a deja choisi. La regle est celle de toutes les formes (useRecherche, commun.tsx).

import { useState } from 'react'
import { EnteteListe, ListeModerne, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  NoteRecherche, PanneauCriteres, Provenance, TableLignes, synthese, useRecherche, type LignesTenues, type LigneTenue,
  type Saisies,
} from './commun'

export function Resultats({ d, lignes: lues, ouvrir, forme, invite }: {
  d: DescripteurEcranVue; lignes: LignesTenues; ouvrir: (l: LigneTenue) => void; forme: string; invite: string
}) {
  const { vue, lignes } = lues
  const [texte, poserTexte] = useState('')
  const [criteres, poserCriteres] = useState<Saisies>({})
  const { visibles, enBase, filtre } = useRecherche(lues, texte, criteres)
  return (
    <div className="rig-proc__corps rig-eg" data-forme={forme}>
      <PanneauCriteres d={d} criteres={criteres} changer={(c, v) => poserCriteres((avant) => ({ ...avant, [c]: v }))}
                       effacer={() => poserCriteres({})} />
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche,
                               valider: () => { const premiere = visibles[0]; if (premiere) ouvrir(premiere) } }} />
        {vue && <Provenance vue={vue} />}
        <NoteRecherche enBase={enBase} />
        <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} ouvrir={ouvrir} filtre={filtre} />
        <PiedListe aide={invite}>
          <span><b>{visibles.length}</b> résultat{visibles.length > 1 ? 's' : ''}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
