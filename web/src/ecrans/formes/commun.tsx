// LE SOCLE DES FORMES D'ECRAN GENERE (plan « RIG v3 100 % fonctionnel », lot 1, 25/11/2026).
//
// Chaque procedure decrite dans Pont/EcransGeneres/ porte sa FORME (FormeEcran) ; web/src/ecrans/EcranGenere.tsx
// l'aiguille vers le composant de cette forme (Forme<Nom>.tsx, a cote de ce fichier). Ce qu'elles partagent est ici :
// la lecture des lignes au pont, la provenance (base du greffe ou exemple), le champ type, et le refus honnete des
// gestes. Les listes reprennent le socle des listes modernes (composants/ListeModerne.tsx, wwwroot/rig-listes.css).
//
// LES GESTES (regle du plan) : tant qu'aucun ecrivain n'existe (le lot 2 les branche), un geste ne change RIEN et le
// dit : « Demonstration : rien n'est enregistre ». Jamais un faux succes : une ligne ne change pas d'etat, une saisie
// reste a l'ecran, marquee non enregistree.

import { useEffect, useState, type ReactNode } from 'react'
import { appeler } from '../../pont'
import {
  CelluleChevron, CelluleIdentite, MenuLigne, Pastille, PointEtat, normaliser, type ActionLigne, type Teinte,
} from '../../composants/ListeModerne'
import type { ChampEcranVue, DescripteurEcranVue, EtatEcranVue, LigneEcranVue, MarcheEcranVue } from '../../plan/types'

/** Les lignes que le pont rend (Pont/EcranGenere.cs, VueLignesEcran). */
export interface VueLignesEcran {
  /** base : tout vient de la base ; mixte : certains groupes restent en exemple ; exemple ; erreur (exemples + motif). */
  mode: 'base' | 'mixte' | 'exemple' | 'erreur'
  message: string | null
  greffe: string | null
  lignes: LigneEcranVue[]
  /** Les groupes dont les lignes sont des exemples du descripteur. */
  exemples: string[]
  /** Les groupes branches sur la base mais sans aucune ligne : ils restent affiches, avec « Aucune ligne ». */
  vides?: string[]
}

/** Les trois etats qui portent les gestes : le descripteur les nomme ainsi (EcransGeneresCheck le verifie). */
export const AFAIRE = 'AFaire'
export const ENCOURS = 'EnCours'
export const VALIDEE = 'Validee'

/** Le refus d'un geste tant qu'aucun ecrivain n'existe : la phrase exacte que tout bouton dit. */
export const DEMONSTRATION = 'Démonstration : rien n’est enregistré'

/** Une ligne de l'ecran, avec son rang dans la liste (cle stable des gestes). */
export interface LigneTenue extends LigneEcranVue { i: number }

/** Une ligne est figee hors « a faire » et « en cours » : aucun geste, une fiche en lecture seule. */
export const figee = (etat: string) => etat !== AFAIRE && etat !== ENCOURS

/**
 * Les lignes de l'ecran : celles de la base, ou les exemples. Un appel qui echoue (pont absent, erreur du C#) laisse les
 * exemples, marques comme tels : l'ecran n'est jamais vide sans le dire. Lues UNE fois par procedure ([d.code]) : chaque
 * redessin de la racine renvoie un descripteur neuf (meme contenu), qui ne doit ni relire la base ni refermer la ligne
 * ouverte. null tant que le pont n'a pas repondu : rien n'est affiche avant, jamais un nom fictif sous un ecran branche.
 */
export function useLignes(d: DescripteurEcranVue): { vue: VueLignesEcran | null; lignes: LigneTenue[] } {
  const [vue, poserVue] = useState<VueLignesEcran | null>(null)
  useEffect(() => {
    let vivant = true
    const recevoir = (v: VueLignesEcran) => { if (vivant) poserVue(v) }
    appeler<VueLignesEcran>('EcranGenere.Lignes', d.code).then(recevoir, () => recevoir({
      mode: 'erreur', message: 'Les lignes de la base n’ont pas pu être lues.', greffe: null, lignes: d.lignes, exemples: d.groupes,
    }))
    return () => { vivant = false }
  }, [d.code])
  return { vue, lignes: (vue?.lignes ?? []).map((l, i) => ({ ...l, i })) }
}

/** La procedure d'une ligne : celle de son groupe dans un ecran reuni, sinon l'ecran lui-meme. */
export function parcoursDe(d: DescripteurEcranVue, groupe: string): DescripteurEcranVue {
  return d.membres.find((m) => m.titre === groupe) ?? d
}

/** Les parties d'une procedure : ses etapes (Saisie), sinon ses sections. */
export const partiesDe = (d: DescripteurEcranVue): MarcheEcranVue[] => (d.marches.length > 0 ? d.marches : d.sections)

/** Les gestes d'une procedure : ceux que la fiche nomme, sinon son bouton final. */
export const gestesDe = (d: DescripteurEcranVue): string[] => (d.gestes.length > 0 ? d.gestes : [d.valider])

/** L'etat d'une ligne, au nom de SA procedure (« A relancer ») ; a defaut, celui de l'ecran. */
export function etatDe(d: DescripteurEcranVue, l: { groupe: string; etat: string }): EtatEcranVue | undefined {
  return parcoursDe(d, l.groupe).etats.find((e) => e.code === l.etat) ?? d.etats.find((e) => e.code === l.etat)
}

/** La teinte de liste de chaque icone d'etat de l'accueil (a-traiter, saisie-manuelle...). */
const TeinteIcone: Record<string, Teinte> = {
  'a-traiter': 'bleu', 'saisie-manuelle': 'orange', validee: 'vert', 'en-attente': 'ardoise', rejetee: 'rouge',
}
export const teinteEtat = (e: EtatEcranVue | undefined): Teinte => (e ? TeinteIcone[e.icone] ?? 'gris' : 'gris')

/** Une teinte stable par groupe : son rang dans les groupes de l'ecran. */
const TeintesGroupe: Teinte[] = ['bleu', 'violet', 'orange', 'vert', 'ardoise', 'rouge']
export const teinteGroupe = (d: DescripteurEcranVue, groupe: string): Teinte =>
  TeintesGroupe[Math.max(0, d.groupes.indexOf(groupe)) % TeintesGroupe.length] ?? 'gris'

/** La mention « exemple » : des lignes du descripteur, aux noms fictifs. */
export function Exemple() {
  return <span className="rig-tcw__pastille rig-tcw__pastille--accent rig-tcw__exemple">exemple</span>
}

/** D'ou viennent les lignes : la base du greffe, ou des exemples (avec la raison et le plafond s'il y en a). */
export function Provenance({ vue }: { vue: VueLignesEcran }) {
  const suite = vue.message ? ` ${vue.message}` : ''
  if (vue.mode === 'base' || vue.mode === 'mixte') {
    return (
      <p className="rig-eg-note" role="note">
        Lignes de la base du greffe {vue.greffe}
        {vue.mode === 'mixte' && <>, sauf les groupes marqués <Exemple /></>}.{suite}
      </p>
    )
  }
  return (
    <p className="rig-eg-note" role="note">
      <Exemple /> Lignes d’exemple : noms fictifs, aucun dossier réel.{suite}
    </p>
  )
}

/** Une ligne en exemple dans un ecran branche (mode mixte : son groupe n'a pas encore de lecteur). */
export const enExemple = (vue: VueLignesEcran | null, l: { groupe: string }) => vue?.mode === 'mixte' && vue.exemples.includes(l.groupe)

/** Le compte rendu d'un geste : ce que l'ecran a fait, ou pourquoi il n'a rien fait. Annonce aux lecteurs d'ecran. */
export function useAnnonce(): [string | null, (texte: string | null) => void] {
  return useState<string | null>(null)
}
export function Annonce({ texte }: { texte: string | null }) {
  if (texte === null) return null
  return <p className="rig-eg-annonce" role="status">{texte}</p>
}

/** Le refus d'un geste : la phrase de demonstration, le geste et sa portee. */
export const refus = (geste: string, portee?: string) => `${DEMONSTRATION} — « ${geste} »${portee ? ` ${portee}` : ''}.`

/** Les saisies d'un formulaire : une valeur par champ, cle « section › champ ». */
export type Saisies = Readonly<Record<string, string>>
export const cleChamp = (section: string, champ: string) => `${section} › ${champ}`

/** Un champ se compte rempli : une saisie non blanche, une case cochee. Une sous-liste ne se saisit pas ici. */
const rempli = (c: ChampEcranVue, valeur: string | undefined) => c.type !== 'Tableau' && (valeur ?? '').trim() !== ''

/** Une section a au moins un champ qu'on saisit ici (pas une sous-liste seule). */
export const saisissable = (s: MarcheEcranVue) => s.champs.some((c) => c.type !== 'Tableau')

/** Une section est faite quand l'un de ses champs saisissables est rempli. */
export const sectionFaite = (s: MarcheEcranVue, saisies: Saisies) => s.champs.some((c) => rempli(c, saisies[cleChamp(s.titre, c.libelle)]))

/**
 * Les controles de la fiche RIG v2 a l'enregistrement : chaque section OBLIGATOIRE de la fiche (« Obligatoire pour
 * valider ») doit avoir au moins un champ rempli. Rend les titres des sections qui manquent. Pour ce qu'on CREE (Saisie)
 * ou qu'on ENGAGE (Operation) ; pas pour une Fiche, ou un champ vide garde la valeur de la base (FormeFiche).
 */
export function manquantes(sections: MarcheEcranVue[], saisies: Saisies): string[] {
  return sections.filter((s) => s.obligatoire && saisissable(s) && !sectionFaite(s, saisies)).map((s) => s.titre)
}

/** Un champ saisi : son controle selon son type. Une sous-liste (Tableau) se lit avec l'objet, elle ne se saisit pas. */
export function ChampSaisie({ champ, valeur, changer, lecture = false, compact = false }: {
  champ: ChampEcranVue; valeur: string; changer: (valeur: string) => void; lecture?: boolean; compact?: boolean
}) {
  const commun = { 'aria-label': champ.libelle, disabled: lecture, className: 'rig-saisie-champ__saisie' }
  let controle: ReactNode
  switch (champ.type) {
    case 'Zone':
      controle = <textarea {...commun} rows={compact ? 1 : 3} value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Date':
      controle = <input {...commun} type="date" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Periode': {
      const [du = '', au = ''] = valeur.split('|')
      controle = (
        <span className="rig-eg-periode">
          <input {...commun} aria-label={`${champ.libelle} : du`} type="date" value={du} onChange={(e) => changer(`${e.target.value}|${au}`)} />
          <span aria-hidden="true">au</span>
          <input {...commun} aria-label={`${champ.libelle} : au`} type="date" value={au} onChange={(e) => changer(`${du}|${e.target.value}`)} />
        </span>
      )
      break
    }
    case 'Nombre':
      controle = <input {...commun} type="number" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Montant':
      controle = <input {...commun} inputMode="decimal" placeholder="0,00 €" value={valeur} onChange={(e) => changer(e.target.value)} />
      break
    case 'Case':
      return (
        <label className="rig-eg-case">
          <input type="checkbox" aria-label={champ.libelle} disabled={lecture} checked={valeur === 'oui'}
                 onChange={(e) => changer(e.target.checked ? 'oui' : '')} />
          {!compact && <span>{champ.libelle}</span>}
        </label>
      )
    case 'Liste':
      controle = (
        <select {...commun} value={valeur} onChange={(e) => changer(e.target.value)}>
          <option value="">—</option>
          {champ.options.map((o) => <option key={o} value={o}>{o}</option>)}
        </select>
      )
      break
    case 'Tableau':
      controle = <div className="rig-eg-tableau" role="note">Liste lue avec l’objet dans la base du greffe.</div>
      break
    default:
      controle = <input {...commun} type="text" value={valeur} onChange={(e) => changer(e.target.value)} />
  }
  if (compact) return <>{controle}</>
  return (
    <label className={`rig-saisie-champ${champ.type === 'Zone' || champ.type === 'Tableau' ? ' rig-saisie-champ--pleine' : ''}`}>
      <span className="rig-saisie-champ__libelle">{champ.libelle}</span>
      {controle}
    </label>
  )
}

/** Les sections d'une fiche : en formulaire (saisies), ou en lecture (lecture = vrai, valeurs « — »). */
export function Sections({ sections, saisies, changer, lecture = false }: {
  sections: MarcheEcranVue[]; saisies: Saisies; changer: (cle: string, valeur: string) => void; lecture?: boolean
}) {
  return (
    <div className="rig-eg-sections">
      {sections.map((s) => (
        <section key={s.titre} className="rig-eg-section" aria-label={s.titre}>
          <header className="rig-eg-section__tete">
            <span className="rig-eg-section__titre">{s.titre}</span>
            {s.obligatoire && !lecture && saisissable(s) && <span className="rig-saisie-champ__requis">obligatoire</span>}
          </header>
          <p className="rig-eg-section__consigne">{s.consigne}</p>
          {s.champs.length > 0 && (lecture ? (
            <dl className="rig-eg-lecture">
              {s.champs.map((c) => (
                <div key={c.libelle} className="rig-eg-lecture__champ">
                  <dt>{c.libelle}</dt>
                  <dd>{c.type === 'Tableau' ? 'Liste lue avec l’objet' : '—'}</dd>
                </div>
              ))}
            </dl>
          ) : (
            <div className="rig-champs rig-eg-champs">
              {s.champs.map((c) => (
                <ChampSaisie key={c.libelle} champ={c} valeur={saisies[cleChamp(s.titre, c.libelle)] ?? ''}
                             changer={(v) => changer(cleChamp(s.titre, c.libelle), v)} />
              ))}
            </div>
          ))}
        </section>
      ))}
    </div>
  )
}

/** Le texte d'une ligne pour la recherche : nom, precisions, groupe et cellules. */
export const texteLigne = (l: LigneEcranVue) => [l.nom, l.precisions, l.groupe, ...(l.valeurs ?? [])].join(' ')

/** Un montant ou un nombre lu dans une cellule (« 180.00 », « 1 234,50 ») ; NaN s'il n'y en a pas. */
export function nombreDe(texte: string | undefined): number {
  if (texte === undefined || texte.trim() === '') return Number.NaN
  return Number(texte.replace(/[\s  €]/g, '').replace(',', '.'))
}

/** Une ligne passe-t-elle les criteres remplis ? Chaque critere rempli doit se lire dans la ligne (sans casse ni accents). */
export function passeCriteres(l: LigneEcranVue, criteres: Saisies): boolean {
  const texte = normaliser(texteLigne(l))
  return Object.values(criteres).every((v) => v.trim() === '' || texte.includes(normaliser(v.trim())))
}

/** Le panneau des criteres de la fiche RIG v2 (aside.rig-criteres, replie par defaut par la liste moderne). */
export function PanneauCriteres({ d, criteres, changer, effacer, children }: {
  d: DescripteurEcranVue; criteres: Saisies; changer: (critere: string, valeur: string) => void; effacer: () => void
  children?: ReactNode
}) {
  return (
    <aside className="rig-criteres" aria-label={`Critères — ${d.titre}`}>
      <div className="rig-kicker">Critères</div>
      {d.criteres.map((c) => (
        <label key={c} className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">{c}</span>
          <input className="rig-saisie-champ__saisie" aria-label={c} value={criteres[c] ?? ''} onChange={(e) => changer(c, e.target.value)} />
        </label>
      ))}
      {children}
      <div className="rig-criteres__actions">
        <button type="button" className="rig-bouton rig-bouton--plat" onClick={effacer}>Effacer</button>
      </div>
    </aside>
  )
}

/** Ce qu'une table de lignes sait faire en plus de lister : cocher, deplier, ouvrir. */
export interface ProprietesTableLignes {
  d: DescripteurEcranVue
  vue: VueLignesEcran | null
  /** Les lignes a montrer (deja filtrees). */
  lignes: LigneTenue[]
  /** Toutes les lignes de l'ecran : pour dire « aucune ligne » ou « rien ne correspond ». */
  total: number
  /** Une recherche, un etat ou un critere filtre la liste : les groupes vides se cachent alors. */
  filtre?: boolean | undefined
  /** Les cases a cocher (File, Audience, Operation) ; absent, pas de case. */
  cases?: { cochees: ReadonlySet<number>; basculer: (i: number) => void; toutes: (cocher: boolean) => void } | undefined
  /** Le detail d'une ligne depliee (chevron) ; absent, pas de chevron. */
  detail?: ((l: LigneTenue) => ReactNode) | undefined
  /** Clic sur la ligne : l'ouvrir (Fiche, Consultation, Document). */
  ouvrir?: ((l: LigneTenue) => void) | undefined
  /** La ligne ouverte, soulignee. */
  ouverte?: number | null | undefined
  /** Les actions du menu « … » de la ligne. */
  actions?: ((l: LigneTenue) => ActionLigne[]) | undefined
}

/** La table des lignes d'un ecran genere : identite (nom, precisions), procedure ou groupe, etat. */
export function TableLignes({ d, vue, lignes, total, filtre = false, cases, detail, ouvrir, ouverte, actions }: ProprietesTableLignes) {
  const [depliees, poserDepliees] = useState<ReadonlySet<number>>(() => new Set())
  const avecGroupe = d.groupes.length > 1
  const nbColonnes = (cases ? 1 : 0) + (detail ? 1 : 0) + 2 + (avecGroupe ? 1 : 0) + (actions ? 1 : 0)
  const cochables = cases ? lignes.filter((l) => !figee(l.etat)) : []
  const toutesCochees = cases !== undefined && cochables.length > 0 && cochables.every((l) => cases.cochees.has(l.i))
  const basculerDepliee = (i: number) => poserDepliees((avant) => {
    const apres = new Set(avant)
    if (!apres.delete(i)) apres.add(i)
    return apres
  })
  const aucuneLigne = `Aucune ligne dans la base du greffe${vue?.greffe ? ` ${vue.greffe}` : ''}.`
  const videTable = (
    <tr>
      <td colSpan={nbColonnes} className="rig-table__vide">
        {vue === null ? 'Lecture des lignes…' : total === 0 ? aucuneLigne : 'Rien ne correspond à la recherche.'}
      </td>
    </tr>
  )
  const rangee = (l: LigneTenue) => {
    const etat = etatDe(d, l)
    const ouverteIci = depliees.has(l.i)
    return [
      <tr key={l.i} className={[ouvrir ? 'rig-eg-ligne--ouvrable' : '', ouverte === l.i ? 'rig-eg-ligne--ouverte' : ''].join(' ').trim() || undefined}
          aria-current={ouverte === l.i ? 'true' : undefined} onClick={ouvrir ? () => ouvrir(l) : undefined}>
        {cases && (
          <td className="rig-eg-case-cellule" onClick={(e) => e.stopPropagation()}>
            <input type="checkbox" aria-label={`Cocher ${l.nom}`} checked={cases.cochees.has(l.i)} disabled={figee(l.etat)}
                   title={figee(l.etat) ? `Ligne figée (${etat?.libelle ?? l.etat}) : aucun geste` : undefined}
                   onChange={() => cases.basculer(l.i)} />
          </td>
        )}
        {detail && <CelluleChevron ouverte={ouverteIci} basculer={() => basculerDepliee(l.i)} />}
        <CelluleIdentite nom={l.nom} objet={l.precisions} titre={`${l.nom} — ${l.precisions}`} />
        {avecGroupe && (
          <td title={l.groupe}>
            <Pastille teinte={teinteGroupe(d, l.groupe)}>{l.groupe}</Pastille>
            {enExemple(vue, l) && <> <Exemple /></>}
          </td>
        )}
        <td><PointEtat teinte={teinteEtat(etat)}>{etat?.libelle ?? l.etat}</PointEtat></td>
        {actions && <td className="rig-lm-cellule-menu"><MenuLigne actions={actions(l)} libelle={`Actions — ${l.nom}`} /></td>}
      </tr>,
      detail && ouverteIci ? <tr key={`${l.i}-detail`} className="rig-eg-detail"><td colSpan={nbColonnes}>{detail(l)}</td></tr> : null,
    ]
  }
  // Un ecran REUNI (plusieurs procedures) range ses lignes par procedure, un bloc chacune, titre et nombre en tete. Un
  // groupe branche sur la base mais sans ligne (vue.vides) reste a l'ecran avec son titre et « Aucune ligne » : un
  // message unique ne les remplace pas (cas « Envois et retours », 25/11/2026, plan-f7ba4de2 lot 2). Une recherche ou
  // un etat qui filtre cache les groupes vides.
  const vides = filtre ? [] : vue?.vides ?? []
  const groupes = d.membres.length === 0 ? null : [...new Set([...d.groupes, ...lignes.map((l) => l.groupe)])]
    .map((titre) => ({ titre, lignes: lignes.filter((l) => l.groupe === titre) }))
    .filter((g) => g.lignes.length > 0 || vides.includes(g.titre))
  return (
    <div className="rig-table-cadre">
      <table className="rig-table rig-lm-table rig-eg-table">
        <colgroup>
          {cases && <col style={{ width: '34px' }} />}
          {detail && <col style={{ width: '30px' }} />}
          <col />
          {avecGroupe && <col style={{ width: '24%' }} />}
          <col style={{ width: '18%' }} />
          {actions && <col style={{ width: '40px' }} />}
        </colgroup>
        <thead>
          <tr>
            {cases && (
              <th className="rig-eg-case-cellule">
                <input type="checkbox" aria-label="Tout cocher" checked={toutesCochees} disabled={cochables.length === 0}
                       onChange={(e) => cases.toutes(e.target.checked)} />
              </th>
            )}
            {detail && <th aria-label="Déplier" />}
            <th>{d.sujet.charAt(0).toUpperCase() + d.sujet.slice(1)}</th>
            {avecGroupe && <th>{d.membres.length > 0 ? 'Procédure' : 'Groupe'}</th>}
            <th>État</th>
            {actions && <th aria-label="Actions" />}
          </tr>
        </thead>
        {groupes === null ? (
          <tbody>
            {lignes.flatMap(rangee)}
            {lignes.length === 0 && videTable}
          </tbody>
        ) : groupes.length === 0 ? <tbody>{videTable}</tbody> : groupes.map((g) => (
          <tbody key={g.titre} className="rig-tcw__groupe rig-eg-groupe">
            <tr className="rig-eg-groupe__tete">
              <th colSpan={nbColonnes} scope="colgroup">
                <span className="rig-tcw__groupe-titre">{g.titre}</span>
                <span className="rig-tcw__groupe-nombre">{g.lignes.length}</span>
              </th>
            </tr>
            {g.lignes.length > 0 ? g.lignes.flatMap(rangee) : (
              <tr><td colSpan={nbColonnes} className="rig-tcw__vide-groupe">{aucuneLigne}</td></tr>
            )}
          </tbody>
        ))}
      </table>
    </div>
  )
}

/** Les groupes branches mais vides (ecran reuni) : dits sous la table, pour que l'agent ne croie pas la fonction absente. */
export function GroupesVides({ vue }: { vue: VueLignesEcran | null }) {
  const vides = vue?.vides ?? []
  if (vides.length === 0) return null
  return <p className="rig-eg-note">Aucune ligne dans la base du greffe{vue?.greffe ? ` ${vue.greffe}` : ''} pour : {vides.join(' · ')}.</p>
}

/** La synthese d'une liste : les lignes reelles, et a part celles en exemple. */
export function synthese(d: DescripteurEcranVue, vue: VueLignesEcran | null, lignes: LigneTenue[]): ReactNode {
  const exemples = lignes.filter((l) => enExemple(vue, l)).length
  return <><b>{lignes.length - exemples}</b> {d.sujet}{exemples > 0 && <> · {exemples} en exemple</>}</>
}
