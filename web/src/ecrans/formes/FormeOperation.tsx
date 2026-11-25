// FORME « OPERATION » : un geste ponctuel sur un ou quelques objets (supprimer ou liberer une instance, joindre,
// rapprocher ou remplacer un acteur, changer le juge-commissaire). Trois temps, et pas un de plus : CHOISIR les objets,
// voir les CONSEQUENCES (les regles de la fiche RIG v2 et ses champs), CONFIRMER par le bouton de la fiche. Ecran
// reuni : on agit dans UNE procedure a la fois (cocher une ligne d'une autre procedure repart d'elle). Une operation sur
// UN objet (descripteur.objetUnique : la fiche le recoit en parametre, PROC_IA et sa liasse) n'en garde qu'un : en
// cocher un autre le remplace, et il n'y a pas de « tout cocher ».
// La confirmation ne change rien tant qu'aucun ecrivain n'existe : elle le dit.

import { useState } from 'react'
import { EnteteListe, ListeModerne, normaliser, PiedListe } from '../../composants/ListeModerne'
import type { DescripteurEcranVue } from '../../plan/types'
import {
  Annonce, DEMONSTRATION, PanneauCriteres, Provenance, Sections, TableLignes, cleChamp, figee, manquantes,
  parcoursDe, passeCriteres, refus, texteLigne, useAnnonce, useLignes, type Saisies,
} from './commun'

const Temps = ['Choisir', 'Conséquences', 'Confirmer'] as const

export function FormeOperation({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const [temps, poserTemps] = useState(0)
  const [texte, poserTexte] = useState('')
  const [criteres, poserCriteres] = useState<Saisies>({})
  const [cochees, poserCochees] = useState<ReadonlySet<number>>(() => new Set())
  const [saisies, poserSaisies] = useState<Saisies>({})
  const [annonce, annoncer] = useAnnonce()

  const cherche = normaliser(texte.trim())
  const visibles = lignes.filter((l) => passeCriteres(l, criteres) && (cherche === '' || normaliser(texteLigne(l)).includes(cherche)))
  const choisies = lignes.filter((l) => cochees.has(l.i))
  const p = choisies[0] ? parcoursDe(d, choisies[0].groupe) : d.membres[0] ?? d
  const basculer = (i: number) => poserCochees((avant) => {
    const ligne = lignes.find((l) => l.i === i)
    const autre = ligne && choisies[0] && ligne.groupe !== choisies[0].groupe
    const unique = ligne !== undefined && parcoursDe(d, ligne.groupe).objetUnique === true
    const apres = new Set(autre || (unique && !avant.has(i)) ? [] : avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })
  const confirmer = () => {
    const manque = manquantes(p.sections, saisies)
    if (manque.length > 0) {
      poserTemps(1)
      annoncer(`Contrôle de la fiche RIG v2 : « ${manque.join(' », « ')} » ${manque.length > 1 ? 'sont obligatoires' : 'est obligatoire'}. ${DEMONSTRATION}.`)
      return
    }
    annoncer(refus(p.valider, `sur ${choisies.length} objet${choisies.length > 1 ? 's' : ''} : rien n’est modifié dans la base`))
  }
  const remplis = p.sections.flatMap((s) => s.champs.map((c) => ({ c, v: saisies[cleChamp(s.titre, c.libelle)] ?? '' })))
    .filter(({ v }) => v.trim() !== '')

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Operation">
      {temps === 0 && (
        <PanneauCriteres d={d} criteres={criteres} changer={(c, v) => poserCriteres((avant) => ({ ...avant, [c]: v }))}
                         effacer={() => poserCriteres({})} />
      )}
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={<>{p !== d ? `${p.titre} · ` : ''}<b>{choisies.length}</b> choisi{choisies.length > 1 ? 's' : ''}</>}
                     filtre={temps === 0 ? { valeur: texte, changer: poserTexte, indication: d.recherche } : undefined} />
        <ol className="rig-frise rig-frise--h rig-eg-temps" aria-label="Temps de l’opération">
          {Temps.map((t, k) => (
            <li key={t} className={`rig-frise__pas${k < temps ? ' rig-frise__pas--complet' : ''}${k === temps ? ' rig-frise__pas--courant' : ''}`}>
              <button type="button" className="rig-frise__bouton" aria-current={k === temps ? 'step' : undefined}
                      disabled={k > 0 && choisies.length === 0} onClick={() => poserTemps(k)}>
                <span className="rig-frise__pastille">{k < temps ? '✓' : k + 1}</span>
                <span className="rig-frise__libelle">{t}</span>
              </button>
            </li>
          ))}
        </ol>
        {temps === 0 && (
          <>
            {vue && <Provenance vue={vue} />}
            <TableLignes d={d} vue={vue} lignes={visibles} total={lignes.length} filtre={cherche !== '' || Object.values(criteres).some((v) => v.trim() !== '')}
                         cases={{ cochees, basculer, toutes: (d.membres.length > 0 ? d.membres : [d]).some((m) => m.objetUnique) ? undefined
                           : (cocher) => poserCochees(cocher
                             ? new Set(visibles.filter((l) => !figee(l.etat) && (!choisies[0] || l.groupe === choisies[0].groupe)).map((l) => l.i))
                             : new Set()) }} />
          </>
        )}
        {temps === 1 && (
          <Sections sections={p.sections} saisies={saisies} changer={(cle, v) => poserSaisies((avant) => ({ ...avant, [cle]: v }))} />
        )}
        {temps === 2 && (
          <div className="rig-eg-recap">
            <p><b>{p.valider}</b> sur :</p>
            <ul>{choisies.map((l) => <li key={l.i}><b>{l.nom}</b> — {l.precisions}</li>)}</ul>
            {remplis.length > 0 && (
              <dl className="rig-eg-lecture">
                {remplis.map(({ c, v }) => <div key={c.libelle} className="rig-eg-lecture__champ"><dt>{c.libelle}</dt><dd>{v === 'oui' ? 'Oui' : v}</dd></div>)}
              </dl>
            )}
          </div>
        )}
        <Annonce texte={annonce} />
        <PiedListe>
          <button type="button" className="rig-lm-outil" disabled={temps === 0} onClick={() => poserTemps(temps - 1)}>Précédent</button>
          {temps < 2 ? (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={choisies.length === 0}
                    onClick={() => poserTemps(temps + 1)}>Suivant</button>
          ) : (
            <button type="button" className="rig-lm-outil rig-lm-outil--fort" onClick={confirmer}>{p.valider}</button>
          )}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
