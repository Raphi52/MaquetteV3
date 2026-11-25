// FORME « FICHE » : corriger un objet existant (une affaire, un dossier, une creance). On le RETROUVE (criteres et
// resultats), puis on le corrige dans un formulaire par sections aux vrais champs (texte, date, liste, case), et on
// Enregistre ou on Annule. Un champ laisse vide garde la valeur de la base : une section obligatoire ne manque donc
// jamais ici. La regle de la creation (Saisie, « au moins un champ rempli ») n'y vaut pas : appliquee a une fiche, elle
// redemandait l'objet deja ouvert (« Corriger une affaire » : « Instance est obligatoire », controle final du lot 1,
// 25/11/2026). Les controles propres a chaque fiche RIG v2 viendront avec son ecrivain. Une ligne figee (verrouillee
// par un autre utilisateur...) s'ouvre en lecture seule.
// Ecran reuni : chaque ligne s'ouvre sur les sections de SA procedure.

import { useState } from 'react'
import { EnteteListe, ListeModerne, PiedListe, PointEtat } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import { Resultats } from './Resultats'
import {
  Annonce, Sections, etatDe, figee, parcoursDe, refus, sectionFaite, teinteEtat, useAnnonce, useLignes, type Saisies,
} from './commun'

export function FormeFiche({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const [ouverte, poserOuverte] = useState<number | null>(null)
  const [saisies, poserSaisies] = useState<Saisies>({})
  const [annonce, annoncer] = useAnnonce()
  const sel = ouverte === null ? undefined : lignes.find((l) => l.i === ouverte)

  if (!sel) {
    return <Resultats d={d} vue={vue} lignes={lignes} forme="Fiche" invite="Choisissez une ligne pour ouvrir sa fiche."
                      ouvrir={(l) => { poserOuverte(l.i); poserSaisies({}); annoncer(null) }} />
  }

  const p = parcoursDe(d, sel.groupe)
  const etat = etatDe(d, sel)
  const lecture = figee(sel.etat)
  const fermer = () => { poserOuverte(null); poserSaisies({}); annoncer(null) }
  const enregistrer = () => {
    if (!p.sections.some((s) => sectionFaite(s, saisies))) {
      annoncer(refus('Enregistrer', `: aucun champ n’est modifié, « ${sel.nom} » garde les valeurs de la base`))
      return
    }
    annoncer(refus('Enregistrer', `: « ${sel.nom} » n’est pas modifiée dans la base, la fiche reste ouverte`))
  }
  return (
    <div className="rig-proc__corps rig-eg" data-forme="Fiche">
      <ListeModerne etiquette={`${p.titre} — ${sel.nom}`}>
        <EnteteListe titre={sel.nom} synthese={<>{d.membres.length > 0 ? `${p.titre} · ` : ''}{sel.precisions}</>} />
        <div className="rig-lm-outils">
          <button type="button" className="rig-lm-outil" onClick={fermer}>← Résultats</button>
          <PointEtat teinte={teinteEtat(etat)}>{etat?.libelle ?? sel.etat}</PointEtat>
        </div>
        {lecture && <p className="rig-eg-note" role="note">Fiche figée ({etat?.libelle ?? sel.etat}) : elle s’ouvre en lecture seule.</p>}
        <Sections sections={p.sections} saisies={saisies} lecture={lecture}
                  changer={(cle, v) => poserSaisies((avant) => ({ ...avant, [cle]: v }))} />
        <Annonce texte={annonce} />
        <PiedListe aide={lecture ? undefined : 'Les champs vides gardent la valeur de la base.'}>
          <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={lecture} onClick={enregistrer}>Enregistrer</button>
          <button type="button" className="rig-lm-outil" onClick={fermer}>Annuler</button>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
