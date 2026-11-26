// Ecran de la procedure PROC_AUDIENCE (RIG v2 : « Creer une Audience »,
// etape ETP_CREATION_AUDIENCE). Les champs sont ceux d'OPE_CREATION_AUDIENCE :
// ULTDateAudience, UltHeureAudience, ULTCBTypeAudience, ULTCBChambre,
// ULTCBSection, ULTCheckBPublique, ULTCheckBChoixWeb, plus le bloc
// OPE_TYPE_CLASSEMENT_ADMIS et le temoin ult_LabelMailActive.
//
// Porte depuis Components/Ecrans/EcranAudiences.razor (lot 5) : meme balisage,
// memes classes CSS. La date proposee (JeuDemo.Aujourdhui + 7 jours) vient de
// Pont/EcranAudiences.cs ; le reste etait declare dans l'ecran et l'a suivi.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la liste des audiences
// programmees prend les briques de composants/ListeModerne.tsx. Tete (synthese, « Aller a une audience… »
// Ctrl K qui filtre la liste affichee), une ligne par audience (Date, Heure, Type en pastille, Chambre,
// Publique en point d'etat, Affaires) ; Section et « Publiee sur le web » sont dans la ligne depliee. Ni vues
// ni cartes : l'ecran n'a aucun critere sur cette liste. Ni menu ni action de ligne : la liste n'en avait pas.
// La creation d'une audience ne change pas.
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026, marche du lot 17) : les listes de la creation (types
// d'audience, chambres, sections, types de classement) et les audiences a venir viennent du pont (Pont/EcranAudiencesBase.cs →
// Serveur/Socle/EcransMain/LectureAudiences.cs). Sans serveur, la demonstration du pont, marquee « exemple » (ProvenanceEcran) ;
// en panne, le motif et aucune ligne. « Creer l'audience » n'ecrit rien (aucune ecriture d'audience branchee) : il le dit, et
// n'ajoute plus de ligne a la liste -- une ligne ajoutee a l'ecran seul aurait ete un faux succes.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { dateCourte } from '../criteres'
import {
  CelluleChevron, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, normaliser, type Teinte,
} from '../composants/ListeModerne'

export interface AudienceLigne {
  /** aaaa-mm-jj */
  date: string
  heure: string
  type: string
  chambre: string
  section: string
  publique: boolean
  web: boolean
  nbAffaires: number
}

/** Ce que le pont rend (Pont/EcranAudiences.cs, VueAudiences). */
interface VueAudiences {
  lignes: AudienceLigne[]
  types: string[]
  chambres: string[]
  sections: string[]
  classements: string[]
  mode?: ModeLecture
  message?: string | null
  total: number
}

// Le @bind d'un TimeOnly ecrit « HH:mm:ss » dans <input type="time"> ; une
// heure illisible (champ efface) ne remet pas l'heure a zero.
const heureLisible = (texte: string): string | null =>
  /^\d{2}:\d{2}(:\d{2})?$/.test(texte) ? texte.slice(0, 5) : null

const TeinteType: Record<string, Teinte> = {
  'Audience de procedure collective': 'orange', 'Audience de reference': 'ardoise', 'Audience de plaidoirie': 'bleu',
  'Audience de conciliation': 'vert', 'Audience du juge-commissaire': 'violet',
}

/** Les colonnes du tableau a une ligne : chevron, Date, Heure, Type, Chambre, Publique, Affaires. */
const NbColonnes = 7

export function EcranAudiences() {
  // null tant que le pont n'a pas repondu : aucune ligne ni liste, jamais la demonstration a la place de la base.
  const [vue, setVue] = useState<VueAudiences | null>(null)
  const programmees = vue?.lignes ?? []
  const TypesAudience = vue?.types ?? []
  const Classements = vue?.classements ?? []
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateAudience, setDateAudience] = useState<string | null>(null)
  const [heureTexte, setHeureTexte] = useState('09:00:00')
  const [heure, setHeure] = useState('09:00')
  // Les choix partent sur la premiere valeur de chaque liste, une fois les listes arrivees.
  const [typeAudience, setTypeAudience] = useState('')
  const [chambre, setChambre] = useState('')
  const [section, setSection] = useState('')
  const [publique, setPublique] = useState(true)
  const [choixWeb, setChoixWeb] = useState(false)
  const mailActif = true
  const [admis, setAdmis] = useState<ReadonlySet<string>>(new Set())
  const [annonce, setAnnonce] = useState('')

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranAudiences.DateProposee')
      .then((d) => { if (!perime) setDateAudience((avant) => avant ?? d) })
    void appeler<VueAudiences>('EcranAudiences.Vue').then((v) => {
      if (perime) return
      setVue(v)
      setTypeAudience((avant) => avant || (v.types[0] ?? ''))
      setChambre((avant) => avant || (v.chambres[0] ?? ''))
      setSection((avant) => avant || (v.sections[0] ?? ''))
      setAdmis((avant) => (avant.size > 0 || v.classements.length === 0 ? avant : new Set([v.classements[0]!])))
    })
    return () => { perime = true }
  }, [])

  function saisirHeure(texte: string) {
    setHeureTexte(texte)
    const h = heureLisible(texte)
    if (h !== null) setHeure(h)
  }

  function basculerAdmis(c: string) {
    setAdmis((avant) => {
      const s = new Set(avant)
      if (!s.delete(c)) s.add(c)
      return s
    })
  }

  // Aucune ecriture d'audience n'est branchee : le geste le dit, rien ne s'ajoute a la liste (jamais un faux succes).
  function creer() {
    if (!dateAudience) return
    setAnnonce(vue?.mode === 'serveur'
      ? `La création d'une audience n'est pas encore branchée sur la base du greffe : l'audience du ${dateCourte(dateAudience)} à ${heure} n'est pas enregistrée.`
      : 'Démonstration : rien n\'est enregistré.')
  }

  // La ligne depliee par son chevron (une a la fois) : l'audience elle-meme, son rang change a chaque creation.
  const [ouverte, setOuverte] = useState<AudienceLigne | null>(null)

  // « Aller a une audience… » (Ctrl K) : filtre la liste AFFICHEE.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const affichees = cherche === '' ? programmees
    : programmees.filter((a) => normaliser([dateCourte(a.date), a.heure, a.type, a.chambre, a.section].join(' ')).includes(normaliser(cherche)))

  // Les chiffres de la tete et du pied : sur la liste AFFICHEE.
  const publiques = affichees.filter((a) => a.publique).length
  const affaires = affichees.reduce((n, a) => n + a.nbAffaires, 0)

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Creation d'une audience</div>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Date d'audience</span>
            <input className="rig-saisie-champ__saisie" type="date" value={dateAudience ?? ''}
                   onChange={(e) => setDateAudience(e.target.value || null)} />
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Heure de debut</span>
            <input className="rig-saisie-champ__saisie" type="time" value={heureTexte}
                   onChange={(e) => saisirHeure(e.target.value)} />
          </label>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Type d'audience</span>
          <select className="rig-saisie-champ__saisie" value={typeAudience}
                  onChange={(e) => setTypeAudience(e.target.value)}>
            {TypesAudience.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Chambre</span>
            <select className="rig-saisie-champ__saisie" value={chambre} onChange={(e) => setChambre(e.target.value)}>
              {(vue?.chambres ?? []).map((c) => <option key={c}>{c}</option>)}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Section</span>
            <select className="rig-saisie-champ__saisie" value={section} onChange={(e) => setSection(e.target.value)}>
              {(vue?.sections ?? []).map((s) => <option key={s}>{s}</option>)}
            </select>
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${publique ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setPublique(!publique)}>
            <span className="rig-bascule__temoin"></span>
            <span>Audience publique</span>
          </button>
          <button className={`rig-bascule${choixWeb ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setChoixWeb(!choixWeb)}>
            <span className="rig-bascule__temoin"></span>
            <span>Publiee sur le web</span>
          </button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Types de classement admis</div>
          {Classements.map((c) => (
            <button key={c} className={`rig-bascule${admis.has(c) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerAdmis(c)}>
              <span className="rig-bascule__temoin"></span>
              <span>{c}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={creer}>Creer l'audience</button>
        </div>
        {/* fix-ok: la classe d'annonce du socle est rig-eg-annonce (formes/commun.tsx:343) ; « rig-annonce » n'existe pas. */}
        {annonce !== '' && <p className="rig-eg-annonce" role="status">{annonce}</p>}
        <span className={`rig-etat rig-etat--${mailActif ? 'ok' : 'attente'}`}>
          {mailActif ? 'Envoi de mail actif' : 'Envoi de mail inactif'}
        </span>
      </aside>

      <ListeModerne etiquette="Audiences programmees">
        <EnteteListe titre="Audiences programmees"
                     synthese={<>{affichees.length}{affichees.length > 1 ? ' audiences · ' : ' audience · '}<b>{publiques}</b>
                       {publiques > 1 ? ' publiques · ' : ' publique · '}<b>{affaires}</b>{affaires > 1 ? ' affaires' : ' affaire'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une audience…' }} />

        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Audiences" />}

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col style={{ width: '15%' }} /><col style={{ width: '13%' }} /><col />
              <col style={{ width: '11%' }} /><col style={{ width: '12%' }} /><col style={{ width: '11%' }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Date d'audience</th>
                <th>Heure de debut</th>
                <th>Type d'audience</th>
                <th>Chambre</th>
                <th>Publique</th>
                <th className="rig-lm-droite">Affaires</th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((a, i) => {
                const estOuverte = ouverte === a
                return (
                  <Fragment key={i}>
                  <tr className={estOuverte ? 'rig-lm-ouverte' : undefined}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : a)} />
                    <td className="rig-table__mono">{dateCourte(a.date)}</td>
                    <td className="rig-table__mono">{a.heure}</td>
                    <td title={a.type}><Pastille teinte={TeinteType[a.type] ?? 'gris'}>{a.type}</Pastille></td>
                    <td className="rig-table__mono">{a.chambre}</td>
                    <td><PointEtat teinte={a.publique ? 'vert' : 'gris'}>{a.publique ? 'oui' : 'non'}</PointEtat></td>
                    <td className="rig-lm-droite">{a.nbAffaires}</td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} champs={[
                      { libelle: 'Section', valeur: a.section },
                      { libelle: 'Publiee sur le web', valeur: a.web ? 'oui' : '' },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {programmees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucune des ${programmees.length} audiences ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' audiences affichees' : ' audience affichee'}</span>
          <span>Publiques <b>{publiques}</b></span>
          <span>Affaires <b>{affaires}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
