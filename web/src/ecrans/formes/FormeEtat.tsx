// FORME « ETAT » : un journal, un livre, une statistique, un export. Les PARAMETRES de la fiche RIG v2 (la periode...)
// dans le panneau de gauche, le TABLEAU de resultats aux colonnes de la fiche, et ses TOTAUX au pied : chaque colonne
// montant ou nombre est totalisee, par groupe (le mode de paiement d'un journal de caisse) puis en tout. Les gestes de la
// fiche (Editer, Generer, Exporter...) partent a l'ecrivain de la procedure (type Agir, son nom, les lignes AFFICHEES de la base,
// et les PARAMETRES saisis : la periode du titre, le tri) quand les lignes viennent de la base ; sans ecrivain, ou sur des exemples,
// ils ne produisent rien et le disent (plan « RIG v3 100 % fonctionnel », lot 6 : l'impression des registres judiciaires).
// UN envoi par procedure, pas un par groupe (lot 16 du plan-f4fb097f, 28/11/2026) : RIG v2 edite le resultat en un clic -- un
// journal de caisse trie par mode de paiement (OPE_METHODETRI.Editer, OPE_METHODETRI.cs:83-109), une demande par registre dans la
// MEME validation (OPE_REGISTRE_ABSTRACT.cs:113-126, que l'ecrivain fait lui-meme). Seul un ecran reuni envoie un geste par
// procedure membre (useGesteEnLot).
// Une liste COUPEE au plafond du serveur (vue.plafonnees) n'est pas le registre : RIG v2 imprime le registre entier avec le
// total de sa requete (OPE_RESULTATS.cs:35-39, :65-73 ; OPE_REGISTRE_GENERAL.cs:221) -- le geste est refuse, rien ne part.
// LA BASE (lot 6) : quand le lecteur sait chercher (vue.recherche), le texte et les PARAMETRES (au libelle du descripteur)
// cherchent dans la base (useRecherche, comme une Audience : un parametre ne se lit pas dans une ligne) -- les criteres de
// RIG v2, qui relance sa requete a chaque saisie (proc_regjud.md:156-160) ; resserres, ils levent la coupure. Sinon, la periode
// et le filtre ne portent que sur les lignes chargees : ils ne levent pas la coupure, le message ne les propose donc pas, et
// la periode filtre le tableau sur sa premiere colonne date, quand il en a une.

import { useState } from 'react'
import { EnteteListe, ListeModerne, montant, PiedListe, VuesListe } from '../../composants/ListeModerne'
import { dateCourte } from '../../criteres'
import type { DescripteurEcranVue } from '../../plan/types'
import { CasesSansValeurs, cellules, lisible, sansValeurs } from './FormeReferentiel'
import {
  Annonce, ChampSaisie, GroupesVides, NoteRecherche, Provenance, deLaBase, enExemple, Exemple, gestesDe, nombreDe, refus, synthese,
  useAnnonce, useGeste, useGesteEnLot, useLignes, useRecherche, type Saisies,
} from './commun'

const Tous = '\u0000tous'

export function FormeEtat({ d }: { d: DescripteurEcranVue }) {
  const lues = useLignes(d)
  const { vue, lignes } = lues
  const [parametres, poserParametres] = useState<Saisies>({})
  const [texte, poserTexte] = useState('')
  const [groupe, poserGroupe] = useState(Tous)
  const [annonce, annoncer] = useAnnonce()
  const jouer = useGeste(d, vue, annoncer)
  const enLot = useGesteEnLot(d, vue, annoncer)

  // La periode (premier parametre de type Periode) borne la premiere colonne date du tableau.
  const periode = d.parametres.find((p) => p.type === 'Periode')
  const [du = '', au = ''] = (periode ? parametres[periode.libelle] ?? '' : '').split('|')
  const colonneDate = d.colonnes.findIndex((c) => c.type === 'Date')
  // Le texte filtre ici les lignes que la base n'a pas cherchees ; les parametres partent a la base si elle cherche.
  const { visibles: trouvees, enBase } = useRecherche(lues, texte, parametres, false)
  const visibles = trouvees.filter((l) => {
    if (groupe !== Tous && l.groupe !== groupe) return false
    if (enBase) return true
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
  // Le geste g sur les lignes affichees (en tete) ; ecrit, l'Etat se relit (lot 13 : « Lettrer » change les sommes du lettrage).
  // fix-ok: cause mesuree (vitest EcranGenere.test.tsx « journal de caisse en Tout », 28/11/2026 : rouge « Chèque : Journal… Espèces :
  // Journal… ») -- l'Etat passait par useGesteEnLot, qui fait un envoi par GROUPE, et n'envoyait aucun parametre ({}) : deux editions
  // au meme titre, sur les dates des lignes et non sur la periode saisie.
  const agir = (g: string) => {
    const demonstration = (n: number) => refus(g, `: aucun état n’est produit (${n} ligne${n > 1 ? 's' : ''} à l’écran)`)
    if (d.membres.length > 0) return void enLot('Agir', g, visibles, parametres, demonstration, lues.relire)
    // Les lignes d'exemple (un groupe sans lecteur) ne partent pas ; sans ligne de la base, la demonstration (useGeste).
    const lignesBase = visibles.filter((l) => deLaBase(vue, l))
    const visees = lignesBase.length > 0 ? lignesBase : visibles
    void jouer({ type: 'Agir', nom: g, groupe: visees[0]?.groupe ?? d.titre, ids: visees.map((l) => l.id), valeurs: parametres },
      demonstration(visibles.length), lues.relire)
  }

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
        <NoteRecherche enBase={enBase} />
        <VuesListe vues={[{ cle: Tous, libelle: 'Tout', compte: lignes.length },
                          ...d.groupes.map((g) => ({ cle: g, libelle: g, compte: lignes.filter((l) => l.groupe === g).length }))]}
                   active={groupe} choisir={poserGroupe}>
          {gestesDe(d).map((g, k) => (
            <button key={g} type="button" className={`rig-lm-outil${k === 0 ? ' rig-lm-outil--fort' : ''}`}
                    onClick={() => (visibles.some((l) => vue?.plafonnees?.includes(l.groupe))
                      ? annoncer({ refus: `« ${g} » : la liste est coupée (${vue?.message ?? ''}). RIG v2 imprime toutes les lignes ; `
                                        + (enBase
                                          ? 'resserrez les paramètres, qui cherchent dans la base, pour qu’elles soient toutes à l’écran : rien n’est enregistré.'
                                          : 'RIG v3 ne lit que celles-ci, la période et le filtre ne portent que sur elles : rien n’est enregistré.') })
                      : agir(g))}>{g}</button>
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
                  {sansValeurs(l) ? <CasesSansValeurs d={d} vue={vue} l={l} /> : cellules(d, l).map((v, k) => {
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
