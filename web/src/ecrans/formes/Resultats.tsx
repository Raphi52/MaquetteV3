// LA RECHERCHE COMMUNE des formes qui retrouvent un objet avant d'agir dessus (Consultation, Fiche) : les criteres de
// la fiche RIG v2 dans le panneau de gauche, la liste des resultats a droite (liste moderne), une ligne cliquee
// s'ouvre. Les criteres filtrent les lignes a l'ecran : chaque critere rempli doit se lire dans la ligne.

import { useState } from 'react'
import { EnteteListe, ListeModerne, normaliser, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  GroupesVides, PanneauCriteres, Provenance, TableLignes, passeCriteres, synthese, texteLigne,
  type LigneTenue, type Saisies, type VueLignesEcran,
} from './commun'

export function Resultats({ d, vue, lignes, ouvrir, forme, invite }: {
  d: DescripteurEcranVue; vue: VueLignesEcran | null; lignes: LigneTenue[]; ouvrir: (l: LigneTenue) => void
  forme: string; invite: string
}) {
  const [texte, poserTexte] = useState('')
  const [criteres, poserCriteres] = useState<Saisies>({})
  const cherche = normaliser(texte.trim())
  const visibles = lignes.filter((l) => passeCriteres(l, criteres) && (cherche === '' || normaliser(texteLigne(l)).includes(cherche)))
  return (
    <div className="rig-proc__corps rig-eg" data-forme={forme}>
      <PanneauCriteres d={d} criteres={criteres} changer={(c, v) => poserCriteres((avant) => ({ ...avant, [c]: v }))}
                       effacer={() => poserCriteres({})} />
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche,
                               valider: () => { const premiere = visibles[0]; if (premiere) ouvrir(premiere) } }} />
        {vue && <Provenance vue={vue} />}
        <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} ouvrir={ouvrir} />
        <GroupesVides vue={vue} />
        <PiedListe aide={invite}>
          <span><b>{visibles.length}</b> résultat{visibles.length > 1 ? 's' : ''}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
