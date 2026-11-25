// FORME « ETAT » : un journal, un livre, une statistique, un export. Les PARAMETRES de la fiche RIG v2 (la periode...)
// dans le panneau de gauche, le TABLEAU de resultats aux colonnes de la fiche, et ses TOTAUX au pied : chaque colonne
// montant ou nombre est totalisee, par groupe (le mode de paiement d'un journal de caisse) puis en tout. Les gestes de la
// fiche (Editer, Generer, Exporter...) ne produisent rien tant qu'aucun ecrivain n'existe : ils le disent.
// La periode filtre le tableau sur sa premiere colonne date, quand il en a une.

import { useState } from 'react'
import { EnteteListe, ListeModerne, montant, normaliser, PiedListe, VuesListe } from '../../composants/ListeModerne'
import { dateCourte } from '../../criteres'
import type { DescripteurEcranVue } from '../../plan/types'
import { cellules, lisible } from './FormeReferentiel'
import {
  Annonce, ChampSaisie, GroupesVides, Provenance, enExemple, Exemple, gestesDe, nombreDe, refus, synthese, texteLigne,
  useAnnonce, useLignes, type Saisies,
} from './commun'

const Tous = '\u0000tous'

export function FormeEtat({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const [parametres, poserParametres] = useState<Saisies>({})
  const [texte, poserTexte] = useState('')
  const [groupe, poserGroupe] = useState(Tous)
  const [annonce, annoncer] = useAnnonce()

  // La periode (premier parametre de type Periode) borne la premiere colonne date du tableau.
  const periode = d.parametres.find((p) => p.type === 'Periode')
  const [du = '', au = ''] = (periode ? parametres[periode.libelle] ?? '' : '').split('|')
  const colonneDate = d.colonnes.findIndex((c) => c.type === 'Date')
  const cherche = normaliser(texte.trim())
  const visibles = lignes.filter((l) => {
    if (groupe !== Tous && l.groupe !== groupe) return false
    if (cherche !== '' && !normaliser(texteLigne(l)).includes(cherche)) return false
    const jour = colonneDate >= 0 ? cellules(d, l)[colonneDate] ?? '' : ''
    return jour === '' || ((du === '' || jour >= du) && (au === '' || jour <= au))
  })
  // Les totaux : chaque colonne montant ou nombre, en tout et par groupe.
  const totalisees = d.colonnes.map((c, k) => ({ c, k })).filter(({ c }) => c.type === 'Montant' || c.type === 'Nombre')
  // fix-ok: un total sans aucune valeur lue (lignes d'exemple sans cellules) s'ecrivait « 0,00 € » : un faux chiffre. Il dit « — ».
  const somme = (k: number, groupeVise?: string): number | null => {
    const lus = visibles.filter((l) => groupeVise === undefined || l.groupe === groupeVise)
      .map((l) => nombreDe(cellules(d, l)[k])).filter((n) => !Number.isNaN(n))
    return lus.length === 0 ? null : lus.reduce((s, n) => s + n, 0)
  }
  const ecrire = (type: string, n: number | null) => (n === null ? '—' : type === 'Montant' ? montant(n) : n.toLocaleString('fr-FR'))
  const groupesVus = d.groupes.filter((g) => visibles.some((l) => l.groupe === g))

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Etat">
      <aside className="rig-criteres" aria-label={`Paramètres — ${d.titre}`}>
        <div className="rig-kicker">Paramètres</div>
        {d.parametres.map((p) => (
          <ChampSaisie key={p.libelle} champ={p} valeur={parametres[p.libelle] ?? ''}
                       changer={(v) => poserParametres((avant) => ({ ...avant, [p.libelle]: v }))} />
        ))}
        <div className="rig-criteres__actions">
          <button type="button" className="rig-bouton rig-bouton--plat" onClick={() => poserParametres({})}>Effacer</button>
        </div>
      </aside>
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={<>{synthese(d, vue, lignes)}{du || au ? <> · du {du ? dateCourte(du) : '…'} au {au ? dateCourte(au) : '…'}</> : null}</>}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <VuesListe vues={[{ cle: Tous, libelle: 'Tout', compte: lignes.length },
                          ...d.groupes.map((g) => ({ cle: g, libelle: g, compte: lignes.filter((l) => l.groupe === g).length }))]}
                   active={groupe} choisir={poserGroupe}>
          {gestesDe(d).map((g, k) => (
            <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`}
                    onClick={() => annoncer(refus(g, `: aucun état n’est produit (${visibles.length} ligne${visibles.length > 1 ? 's' : ''} à l’écran)`))}>{g}</button>
          ))}
        </VuesListe>
        <Annonce texte={annonce} />
        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table rig-eg-table">
            <thead>
              <tr>{d.colonnes.map((c) => <th key={c.libelle} title={c.libelle} className={c.type === 'Montant' || c.type === 'Nombre' ? 'rig-lm-droite' : undefined}>{c.libelle}</th>)}</tr>
            </thead>
            <tbody>
              {visibles.map((l) => (
                <tr key={l.i}>
                  {cellules(d, l).map((v, k) => {
                    const c = d.colonnes[k]!
                    return (
                      <td key={c.libelle} title={k === 0 ? `${l.nom} — ${l.precisions}` : v}
                          className={c.type === 'Montant' || c.type === 'Nombre' ? 'rig-lm-droite' : undefined}>
                        {lisible(c, v)}{k === 0 && enExemple(vue, l) && <> <Exemple /></>}
                      </td>
                    )
                  })}
                </tr>
              ))}
              {visibles.length === 0 && (
                <tr><td colSpan={d.colonnes.length} className="rig-table__vide">
                  {vue === null ? 'Lecture des lignes…' : lignes.length === 0 ? 'Aucune ligne dans la base du greffe.' : 'Rien pour ces paramètres.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
        <GroupesVides vue={vue} />
        <PiedListe>
          <span><b>{visibles.length}</b> ligne{visibles.length > 1 ? 's' : ''}</span>
          {totalisees.map(({ c, k }) => (
            <span key={c.libelle} className="rig-eg-totaux">
              {groupesVus.length > 1 && groupesVus.map((g) => <span key={g}>{g} <b>{ecrire(c.type, somme(k, g))}</b></span>)}
              <span className="rig-eg-total">Total {c.libelle.toLowerCase()} <b>{ecrire(c.type, somme(k))}</b></span>
            </span>
          ))}
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
