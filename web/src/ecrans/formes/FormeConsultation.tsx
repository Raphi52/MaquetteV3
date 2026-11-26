// FORME « CONSULTATION » : les ecrans « Trouver ». Criteres, resultats, puis la fiche de la ligne en LECTURE SEULE,
// par sections (les anciennes marches de l'escalier). Les gestes que la fiche RIG v2 nomme (Exporter, Signaler...)
// se posent sur la fiche ouverte. Une ligne de la base ouvre sa fiche LUE (useFiche, plan « RIG v3 100 % fonctionnel »,
// lot 2) ; ses gestes vont a l'ecrivain de la procedure (type Agir), sinon ils disent « Demonstration ».

import { useState } from 'react'
import { EnteteListe, ListeModerne, PointEtat } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import { Resultats } from './Resultats'
import { Annonce, Sections, etatDe, gestesDe, parcoursDe, refus, teinteEtat, useAnnonce, useFiche, useGeste, useLignes } from './commun'

export function FormeConsultation({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes, relire } = lues
  const [ouverte, poserOuverte] = useState<number | null>(null)
  const [annonce, annoncer] = useAnnonce()
  const jouer = useGeste(d, vue, annoncer)
  const sel = ouverte === null ? undefined : lignes.find((l) => l.i === ouverte)
  const { fiche } = useFiche(d, sel, vue)

  if (!sel) {
    return <Resultats d={d} lignes={lues} forme="Consultation" invite="Choisissez une ligne pour lire sa fiche."
                      ouvrir={(l) => { poserOuverte(l.i); annoncer(null) }} />
  }
  const p = parcoursDe(d, sel.groupe)
  const etat = etatDe(d, sel)
  return (
    <div className="rig-proc__corps rig-eg" data-forme="Consultation">
      <ListeModerne etiquette={`${p.titre} — ${sel.nom}`}>
        <EnteteListe titre={sel.nom} synthese={<>{d.membres.length > 0 ? `${p.titre} · ` : ''}{sel.precisions}</>} />
        <div className="rig-lm-outils">
          <button type="button" className="rig-lm-outil" onClick={() => { poserOuverte(null); annoncer(null) }}>← Résultats</button>
          <PointEtat teinte={teinteEtat(etat)}>{etat?.libelle ?? sel.etat}</PointEtat>
          <span className="rig-eg-espace" />
          {/* Le bouton de la fiche quand c'est un geste (Exporter, Signaler, Archiver...) ; « Valider » ou « Rechercher »
              n'en sont pas sur une fiche en lecture. fix-ok: mesure (releve des 11 descripteurs Consultation) -- aucun
              ne declare de Gestes, leur geste V2 est leur bouton Valider (« Exporter », « Signaler »...) : p.gestes seul
              n'affichait rien. */}
          {gestesDe(p).filter((g) => g !== 'Valider' && g !== 'Rechercher').map((g) => (
            <button key={g} type="button" className="rig-lm-outil"
                    onClick={() => void jouer({ type: 'Agir', nom: g, groupe: sel.groupe, ids: [sel.id], valeurs: {} },
                                              refus(g, `: « ${sel.nom} »`), relire)}>{g}</button>
          ))}
        </div>
        <Annonce texte={annonce} />
        {fiche && fiche.mode !== 'base' && fiche.message && <p className="rig-eg-note" role="note">{fiche.message}</p>}
        <Sections sections={p.sections} saisies={{}} changer={() => {}} lecture fiche={fiche?.fiche ?? null} />
      </ListeModerne>
    </div>
  )
}
