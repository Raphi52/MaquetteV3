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
// en panne, le motif et aucune ligne.
//
// « CREER L'AUDIENCE » (plan « RIG v3 complet, mesure par le banc du parcours complet », lot 9, 28/11/2026) : la saisie part au pont
// (EcranAudiences.Creer) et le serveur l'ecrit avec les controles de RIG v2 (Serveur/Socle/EcransMain/EcritureAudiences.cs) ; les
// listes envoient leur CODE (options du pont : deux sections de RIG_DEV portent le meme libelle), la section se choisit dans celles de
// la chambre (leur code contient celui de la chambre, OPE_CREATION_AUDIENCE.cs:84-98). Fait : la liste relit la base et montre
// l'audience creee ; refuse : le motif de RIG v2, rien d'ajoute. La case de RIG v2 est « Non disponible dans les choix d'Audience WEB »
// (decochee par defaut, AUDIENCE_CABINET.cs:271) : l'ecran la reprend telle quelle. Sans serveur, rien n'est enregistre et l'ecran le dit.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'
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
  /** Les valeurs des listes avec leur code (vide dans la demonstration) : ce que « Creer l'audience » envoie. */
  options?: OptionAudience[]
  mode?: ModeLecture
  message?: string | null
  total: number
}

/** Une valeur d'une liste de la creation (Pont/EcranAudiences.cs, OptionAudienceVue). */
interface OptionAudience { liste: 'type' | 'chambre' | 'section' | 'classement'; code: string; libelle: string }

/** Une valeur proposee par un choix : la valeur envoyee (le code, ou le libelle dans la demonstration) et son libelle. */
interface Choix { valeur: string; libelle: string }

/** Les choix d'une liste : les options du pont (codes) quand il en rend, sinon les libelles de la demonstration. */
function choixDe(vue: VueAudiences | null, liste: OptionAudience['liste'], libelles: string[]): Choix[] {
  const options = vue?.options ?? []
  return options.length > 0
    ? options.filter((o) => o.liste === liste).map((o) => ({ valeur: o.code, libelle: o.libelle }))
    : libelles.map((l) => ({ valeur: l, libelle: l }))
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
  const avecCodes = (vue?.options ?? []).length > 0
  const TypesAudience = choixDe(vue, 'type', vue?.types ?? [])
  const Chambres = choixDe(vue, 'chambre', vue?.chambres ?? [])
  const Classements = choixDe(vue, 'classement', vue?.classements ?? [])
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateAudience, setDateAudience] = useState<string | null>(null)
  const [heureTexte, setHeureTexte] = useState('09:00:00')
  const [heure, setHeure] = useState('09:00')
  // Les choix partent sur la premiere valeur de chaque liste, une fois les listes arrivees.
  const [typeAudience, setTypeAudience] = useState('')
  const [chambre, setChambre] = useState('')
  const [section, setSection] = useState('')
  const [publique, setPublique] = useState(true)
  // « Non disponible dans les choix d'Audience WEB » (ULTCheckBChoixWeb de RIG v2) : decochee par defaut.
  const [nonWeb, setNonWeb] = useState(false)
  const mailActif = true
  const [admis, setAdmis] = useState<ReadonlySet<string>>(new Set())
  const [annonce, setAnnonce] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)

  // Les sections de la chambre choisie : leur code contient celui de la chambre (RIG v2) ; dans la demonstration, toutes.
  const Sections = choixDe(vue, 'section', vue?.sections ?? []).filter((s) => !avecCodes || chambre === '' || s.valeur.includes(chambre))

  function recevoir(v: VueAudiences) {
    setVue(v)
    const premier = (liste: OptionAudience['liste'], libelles: string[]) => choixDe(v, liste, libelles)[0]?.valeur ?? ''
    setTypeAudience((avant) => avant || premier('type', v.types))
    setChambre((avant) => avant || premier('chambre', v.chambres))
    setSection((avant) => avant || ((v.options ?? []).length > 0 ? '' : premier('section', v.sections)))
    setAdmis((avant) => (avant.size > 0 || v.classements.length === 0 ? avant : new Set([premier('classement', v.classements)])))
  }

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranAudiences.DateProposee')
      .then((d) => { if (!perime) setDateAudience((avant) => avant ?? d) })
    void appeler<VueAudiences>('EcranAudiences.Vue').then((v) => { if (!perime) recevoir(v) })
    return () => { perime = true }
  }, [])

  // Une section qui n'est pas de la chambre choisie ne reste pas choisie.
  const sectionValable = section === '' || Sections.some((s) => s.valeur === section)
  useEffect(() => {
    if (avecCodes && !sectionValable) setSection('')
  }, [avecCodes, sectionValable])

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

  /** Le geste : ecrit par le serveur (controles de RIG v2) ; fait, la liste relit la base. Sans serveur : rien n'est enregistre. */
  async function creer() {
    if (vue?.mode !== 'serveur') {
      setAnnonce('Démonstration : rien n\'est enregistré.')
      return
    }
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranAudiences.Creer', {
        date: dateAudience ?? '', heure, type: typeAudience, chambre, section,
        classements: Classements.filter((c) => admis.has(c.valeur)).map((c) => c.valeur), publique, nonDisponibleWeb: nonWeb,
      })
      setAnnonce(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) recevoir(await appeler<VueAudiences>('EcranAudiences.Vue'))
    } catch {
      setAnnonce({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
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
            {TypesAudience.map((t) => <option key={t.valeur} value={t.valeur}>{t.libelle}</option>)}
          </select>
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Chambre</span>
            <select className="rig-saisie-champ__saisie" value={chambre} onChange={(e) => setChambre(e.target.value)}>
              {Chambres.map((c) => <option key={c.valeur} value={c.valeur}>{c.libelle}</option>)}
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Section</span>
            <select className="rig-saisie-champ__saisie" value={section} onChange={(e) => setSection(e.target.value)}>
              {avecCodes && <option value="">—</option>}
              {Sections.map((s) => <option key={s.valeur} value={s.valeur}>{s.libelle}</option>)}
            </select>
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${publique ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setPublique(!publique)}>
            <span className="rig-bascule__temoin"></span>
            <span>Audience publique</span>
          </button>
          <button className={`rig-bascule${nonWeb ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setNonWeb(!nonWeb)}>
            <span className="rig-bascule__temoin"></span>
            <span>Non disponible dans les choix d'audience web</span>
          </button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Types de classement admis</div>
          {Classements.map((c) => (
            <button key={c.valeur} className={`rig-bascule${admis.has(c.valeur) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerAdmis(c.valeur)}>
              <span className="rig-bascule__temoin"></span>
              <span>{c.libelle}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__actions">
          <button className="rig-bouton rig-bouton--fort" onClick={() => void creer()} disabled={envoi}>Creer l'audience</button>
        </div>
        <Annonce texte={annonce} />
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
