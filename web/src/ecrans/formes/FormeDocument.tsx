// FORME « DOCUMENT » : une visionneuse (un PDF, une image, des pages a pivoter, une page web, l'aide du greffe). A
// gauche les documents (les lignes), a droite la page ouverte et les outils de la fiche RIG v2. Pivoter tourne la page
// A L'ECRAN (une vue, rien n'est ecrit) ; tout autre geste (Enregistrer le PDF, Imprimer...) le dit : rien n'est
// enregistre tant qu'aucun ecrivain n'existe. En demonstration, aucun fichier : la page dit ce qu'elle montrerait.

import { useState } from 'react'
import { EnteteListe, ListeModerne, normaliser } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, GroupesVides, Provenance, Sections, TableLignes, gestesDe, parcoursDe, refus, synthese, texteLigne,
  useAnnonce, useLignes,
} from './commun'

/** Un geste de la fiche qui tourne la page : il change la vue, pas le fichier. */
const tourne = (geste: string) => /pivot|rotation|tourner/i.test(geste)

export function FormeDocument({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const [texte, poserTexte] = useState('')
  const [ouverte, poserOuverte] = useState<number | null>(null)
  const [angle, poserAngle] = useState(0)
  const [annonce, annoncer] = useAnnonce()
  const cherche = normaliser(texte.trim())
  const visibles = lignes.filter((l) => cherche === '' || normaliser(texteLigne(l)).includes(cherche))
  const sel = lignes.find((l) => l.i === ouverte) ?? visibles[0]
  const p = sel ? parcoursDe(d, sel.groupe) : d
  const jouer = (geste: string) => {
    if (tourne(geste)) { poserAngle((a) => (a + 90) % 360); annoncer(`Page tournée à l’écran (${(angle + 90) % 360}°) : le fichier n’est pas modifié.`); return }
    annoncer(refus(geste, sel ? `: « ${sel.nom} » n’est pas modifié` : undefined))
  }

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Document">
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <div className="rig-eg-visionneuse">
          <div className="rig-eg-visionneuse__liste">
            <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} ouverte={sel?.i ?? null}
                         ouvrir={(l) => { poserOuverte(l.i); poserAngle(0); annoncer(null) }} />
            <GroupesVides vue={vue} />
          </div>
          <section className="rig-eg-visionneuse__vue" aria-label={sel ? `Document — ${sel.nom}` : 'Document'}>
            <div className="rig-lm-outils" role="toolbar" aria-label="Outils de la visionneuse">
              {gestesDe(p).map((g, k) => (
                <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`}
                        disabled={!sel} onClick={() => jouer(g)}>{g}</button>
              ))}
            </div>
            <Annonce texte={annonce} />
            <div className="rig-eg-page" style={{ transform: `rotate(${angle}deg)` }} data-angle={angle}>
              {sel ? (
                <>
                  <b className="rig-eg-page__titre">{sel.nom}</b>
                  <span className="rig-eg-page__detail">{sel.precisions}</span>
                  <span className="rig-eg-page__vide">Aperçu indisponible : aucun fichier n’est ouvert en démonstration.</span>
                </>
              ) : <span className="rig-eg-page__vide">Aucun document à montrer.</span>}
            </div>
            <Sections sections={p.sections} saisies={{}} changer={() => {}} lecture />
          </section>
        </div>
      </ListeModerne>
    </div>
  )
}
