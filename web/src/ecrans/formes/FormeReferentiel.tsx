// FORME « REFERENTIEL » : une table de reglage (modes de paiement, mandataires, cabinets, articles tarifaires,
// destinataires, imprimantes...). Une GRILLE editable aux colonnes de la fiche RIG v2 : rechercher, ajouter une ligne,
// modifier en ligne (les cellules deviennent des champs), supprimer. Tant qu'aucun ecrivain n'existe, Enregistrer et
// Supprimer le disent et ne changent rien : la ligne en cours de modification reste ouverte, marquee non enregistree.

import { useState } from 'react'
import { EnteteListe, ListeModerne, MenuLigne, montant, normaliser, PiedListe, VuesListe } from '../../composants/ListeModerne'
import type { ChampEcranVue, DescripteurEcranVue, LigneEcranVue } from '../../plan/types'
import {
  Annonce, ChampSaisie, GroupesVides, Provenance, enExemple, Exemple, nombreDe, refus, synthese, texteLigne,
  useAnnonce, useLignes, type LigneTenue, type VueLignesEcran,
} from './commun'

const Tous = '\u0000tous'

/** Les cellules d'une ligne dans l'ordre des colonnes ; sans valeurs, la premiere colonne porte le nom. */
export const cellules = (d: DescripteurEcranVue, l: LigneTenue): string[] =>
  d.colonnes.map((_, k) => l.valeurs?.[k] ?? (k === 0 ? l.nom : ''))

/**
 * Une ligne SANS cellules : celles que lisent les lecteurs de la base (Serveur/Socle/EcransGeneres/Lecteur*.cs), qui
 * ne rendent que nom, precisions, groupe et etat (Contrats/EcransGeneres/LignesEcran.cs, LigneLue ; le pont envoie
 * des valeurs vides), et les exemples qui n'en ont pas (Mandataires). Ce qui la distingue (abrege, SIREN, CNBF...)
 * est dans ses precisions : etalees sous les colonnes, jamais remplacees par des « — » (controle final du lot 1,
 * 25/11/2026 : « Mandataires » sur la base montrait le nom puis des « — » partout).
 */
export const sansValeurs = (l: LigneEcranVue) => !l.valeurs?.length

/** Les cases d'une ligne sans cellules : le nom dans la premiere colonne, les precisions sur toutes les autres. */
export function CasesSansValeurs({ d, vue, l }: { d: DescripteurEcranVue; vue: VueLignesEcran | null; l: LigneTenue }) {
  const precisions = l.precisions === '' ? '—' : l.precisions
  return (
    <>
      <td title={`${l.nom} — ${l.precisions}`}>
        <b>{l.nom}</b>
        {d.colonnes.length === 1 && <> <span className="rig-eg-precisions">{precisions}</span></>}
        {enExemple(vue, l) && <> <Exemple /></>}
      </td>
      {d.colonnes.length > 1 && <td colSpan={d.colonnes.length - 1} className="rig-eg-precisions">{precisions}</td>}
    </>
  )
}

/** Une cellule lue : une case en Oui / Non, un montant a la francaise, le reste tel quel (vide : « — »). */
export function lisible(c: ChampEcranVue, valeur: string): string {
  if (c.type === 'Case') return valeur === 'oui' ? 'Oui' : valeur === '' ? '—' : 'Non'
  if (c.type === 'Montant') { const n = nombreDe(valeur); return Number.isNaN(n) ? (valeur || '—') : montant(n) }
  if (c.type === 'Date' && /^\d{4}-\d{2}-\d{2}$/.test(valeur)) return valeur.split('-').reverse().join('/')
  return valeur === '' ? '—' : valeur
}

export function FormeReferentiel({ d }: { d: DescripteurEcranVue }) {
  const { vue, lignes } = useLignes(d)
  const [texte, poserTexte] = useState('')
  const [groupe, poserGroupe] = useState(Tous)
  // La ligne en cours de modification (son rang, ou « nouvelle ») et ses valeurs saisies.
  const [edition, poserEdition] = useState<{ cle: number | 'nouvelle'; valeurs: string[] } | null>(null)
  const [annonce, annoncer] = useAnnonce()

  const cherche = normaliser(texte.trim())
  const visibles = lignes.filter((l) => (groupe === Tous || l.groupe === groupe)
    && (cherche === '' || normaliser(texteLigne(l)).includes(cherche)))
  const nbColonnes = d.colonnes.length + 1
  const modifier = (l: LigneTenue) => { poserEdition({ cle: l.i, valeurs: cellules(d, l) }); annoncer(null) }
  const changer = (k: number, v: string) => poserEdition((e) => e && { ...e, valeurs: e.valeurs.map((x, j) => (j === k ? v : x)) })
  const enregistrer = () => annoncer(refus('Enregistrer', edition?.cle === 'nouvelle'
    ? ': la ligne n’est pas ajoutée à la table' : ': la ligne n’est pas modifiée dans la table'))
  const annuler = () => { poserEdition(null); annoncer(null) }

  const ligneEnEdition = (cle: number | 'nouvelle') => edition && edition.cle === cle && (
    <tr key={`edition-${cle}`} className="rig-eg-ligne--edition">
      {d.colonnes.map((c, k) => (
        <td key={c.libelle}>
          <ChampSaisie champ={c} valeur={edition.valeurs[k] ?? ''} changer={(v) => changer(k, v)} compact />
        </td>
      ))}
      <td className="rig-eg-edition-actions">
        <button type="button" className="rig-lm-outil rig-lm-outil--fort" onClick={enregistrer}>Enregistrer</button>
        <button type="button" className="rig-lm-outil" onClick={annuler}>Annuler</button>
      </td>
    </tr>
  )

  return (
    <div className="rig-proc__corps rig-eg" data-forme="Referentiel">
      <ListeModerne etiquette={d.titre}>
        <EnteteListe titre={d.titre} synthese={synthese(d, vue, lignes)}
                     filtre={{ valeur: texte, changer: poserTexte, indication: d.recherche }} />
        {vue && <Provenance vue={vue} />}
        <VuesListe vues={[{ cle: Tous, libelle: 'Toutes', compte: lignes.length },
                          ...d.groupes.map((g) => ({ cle: g, libelle: g, compte: lignes.filter((l) => l.groupe === g).length }))]}
                   active={groupe} choisir={poserGroupe}>
          <button type="button" className="rig-lm-outil rig-lm-outil--fort" disabled={edition?.cle === 'nouvelle'}
                  onClick={() => { poserEdition({ cle: 'nouvelle', valeurs: d.colonnes.map(() => '') }); annoncer(null) }}>Ajouter</button>
        </VuesListe>
        <Annonce texte={annonce} />
        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table rig-eg-table rig-eg-grille">
            <thead>
              <tr>
                {d.colonnes.map((c) => <th key={c.libelle} title={c.libelle} className={c.type === 'Montant' ? 'rig-lm-droite' : undefined}>{c.libelle}</th>)}
                <th aria-label="Actions" style={{ width: edition ? '190px' : '40px' }} />
              </tr>
            </thead>
            <tbody>
              {ligneEnEdition('nouvelle')}
              {visibles.map((l) => ligneEnEdition(l.i) || (
                <tr key={l.i} onDoubleClick={() => modifier(l)}>
                  {sansValeurs(l) ? <CasesSansValeurs d={d} vue={vue} l={l} /> : cellules(d, l).map((v, k) => {
                    const c = d.colonnes[k]!
                    return (
                      <td key={c.libelle} title={k === 0 ? `${l.nom} — ${l.precisions}` : v}
                          className={c.type === 'Montant' ? 'rig-lm-droite' : undefined}>
                        {k === 0 ? <b>{lisible(c, v)}</b> : lisible(c, v)}
                        {k === 0 && enExemple(vue, l) && <> <Exemple /></>}
                      </td>
                    )
                  })}
                  <td className="rig-lm-cellule-menu">
                    <MenuLigne libelle={`Actions — ${l.nom}`} actions={[
                      { libelle: 'Modifier', agir: () => modifier(l) },
                      { libelle: 'Supprimer', agir: () => annoncer(refus('Supprimer', `: « ${l.nom} » reste dans la table`)) },
                    ]} />
                  </td>
                </tr>
              ))}
              {visibles.length === 0 && edition?.cle !== 'nouvelle' && (
                <tr><td colSpan={nbColonnes} className="rig-table__vide">
                  {vue === null ? 'Lecture des lignes…' : lignes.length === 0 ? 'Aucune ligne dans la table du greffe.' : 'Rien ne correspond à la recherche.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
        <GroupesVides vue={vue} />
        <PiedListe aide="Double-cliquez une ligne pour la modifier.">
          <span><b>{visibles.length}</b> ligne{visibles.length > 1 ? 's' : ''}</span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
