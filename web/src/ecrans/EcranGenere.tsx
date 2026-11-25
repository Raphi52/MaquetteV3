// L'ECRAN GENERIQUE : l'AIGUILLEUR des procedures du menu decrites en donnees (Pont/EcransGeneres/, un fichier par
// verbe). Il ne connait aucune procedure : une de plus = un descripteur de plus, sans une ligne de React.
//
// Jusqu'au 25/11/2026, cet ecran mettait TOUTES les procedures en escalier (tuiles, etapes « Terminer l'etape »,
// Valider n/m) -- une recherche, une table de reglage ou un journal aussi ; Raphael, sur « Corriger une affaire » :
// absurde pour corriger une fiche. Chaque descripteur porte desormais sa FORME (Pont/EcransGeneres/Descripteur.cs,
// FormeEcran), choisie d'apres le role de sa fiche RIG v2, et cet ecran monte le composant de cette forme
// (web/src/ecrans/formes/Forme<Nom>.tsx) : File, Consultation, Fiche, Saisie, Referentiel, Etat, Audience, Document,
// Operation. Les listes y reprennent le socle des listes modernes (composants/ListeModerne.tsx).
//
// Un ecran REUNI (descripteur.membres, 24/11/2026) met plusieurs procedures dans une seule liste quand elles ont la
// meme forme et une liste commune (File, Consultation, Fiche, Operation, Document) ; sinon (descripteur.enOnglets) il
// pose un onglet par procedure, chacune dans SA forme (EcranOnglets, le meme que pour les ecrans faits a la main).
//
// LES LIGNES : demandees au pont a l'ouverture (EcranGenere.Lignes, Pont/EcranGenere.cs ; formes/commun.tsx). Branche
// sur le serveur, l'ecran montre les lignes de la base du greffe ; sans serveur, sans lecteur ou en panne, les lignes
// d'exemple du descripteur, marquees « exemple ». LES GESTES ne font rien tant qu'aucun ecrivain n'existe, et le disent :
// « Demonstration : rien n'est enregistre ».

import { useState } from 'react'
import type { DescripteurEcranVue } from '../plan/types'
import { EcranOnglets } from './EcranOnglets'
import { FormeAudience } from './formes/FormeAudience'
import { FormeConsultation } from './formes/FormeConsultation'
import { FormeDocument } from './formes/FormeDocument'
import { FormeEtat } from './formes/FormeEtat'
import { FormeFiche } from './formes/FormeFiche'
import { FormeFile } from './formes/FormeFile'
import { FormeOperation } from './formes/FormeOperation'
import { FormeReferentiel } from './formes/FormeReferentiel'
import { FormeSaisie } from './formes/FormeSaisie'

export type { VueLignesEcran } from './formes/commun'

export interface ProprietesEcranGenere {
  descripteur: DescripteurEcranVue
}

export function EcranGenere({ descripteur: d }: ProprietesEcranGenere) {
  if (d.enOnglets) return <EcranGenereEnOnglets d={d} />
  return <Forme d={d} />
}

/** Le composant de la forme du descripteur. key : une autre procedure (onglet, ecran reuni) est un ecran neuf. */
function Forme({ d }: { d: DescripteurEcranVue }) {
  switch (d.forme) {
    case 'File': return <FormeFile key={d.code} d={d} />
    case 'Consultation': return <FormeConsultation key={d.code} d={d} />
    case 'Fiche': return <FormeFiche key={d.code} d={d} />
    case 'Saisie': return <FormeSaisie key={d.code} d={d} />
    case 'Referentiel': return <FormeReferentiel key={d.code} d={d} />
    case 'Etat': return <FormeEtat key={d.code} d={d} />
    case 'Audience': return <FormeAudience key={d.code} d={d} />
    case 'Document': return <FormeDocument key={d.code} d={d} />
    case 'Operation': return <FormeOperation key={d.code} d={d} />
  }
}

/** Un ecran reuni dont les procedures n'ont pas de liste commune : un onglet par procedure, dans sa forme. L'onglet
 * montre se tient ici (aucun geste au C# : le descripteur porte deja les procedures). */
function EcranGenereEnOnglets({ d }: { d: DescripteurEcranVue }) {
  const [actif, poserActif] = useState(d.membres[0]?.code ?? '')
  return (
    <EcranOnglets titre={d.titre} onglets={d.membres.map((m) => ({ code: m.code, libelle: m.titre }))}
                  actif={actif} choisir={poserActif}
                  ecran={(code) => { const m = d.membres.find((x) => x.code === code); return m ? <Forme d={m} /> : null }} />
  )
}
