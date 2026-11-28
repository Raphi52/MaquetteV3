// Ecran de la procedure PROC_DEPOTJUD.
//
// Etapes lues dans Processus/PROC_DEPOTJUD.Designer.cs (RIG v2), avec le
// libelle que v2 leur donne :
//   ETP_INFO_DEMANDE               « Demande »
//   ETP_INSTANCE_RECHERCHE         « Etape de recherche d'instance »
//   ETP_ENTITE_JUDICIAIRE_READONLY « Entite judiciaire (lecture seule) »
//   ETP_INSTANCE_SUMMARY           « Informations sur l'instance »
//   ETP_EVENEMENT_SURVENU          « Nouvel evenement survenu »
//   ETP_FACTU_GESTCOM              « Facturation et gestion commerciale »
//   ETP_POST_VAL_INSTANCE          « Recapitulatif instance »
// La maquette garde cet enchainement.
//
// Porte depuis Components/Ecrans/EcranDepotsJud.razor (lot 5) : meme balisage,
// memes classes CSS. La date du jour (valeur de depart de l'evenement) vient
// de Pont/EcranDepotsJud.cs ; le reste etait declare dans l'ecran et l'a suivi.
// La coquille .razor monte cet ecran et recoit ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 2) : la colonne des resultats prend
// les briques de composants/ListeModerne.tsx. Tete (le titre reste celui de l'etape, synthese, « Aller a une
// instance… » Ctrl K, Entree ouvre le dossier de l'instance restee seule), tableau a une ligne par instance
// (Instance, Nature, Enrolement, Dernier evenement, Etat) ; chambre, « A facturer » et dossier sont dans la
// ligne depliee, avec « Ouvrir le dossier ». Ni vues ni cartes : l'ecran n'a pas de critere d'etat (les etapes
// sont l'enchainement de la v2, pas un filtre d'etat).
// « Aller a une instance… » EST l'ancien critere « N d'instance » (meme etat, numero d'instance cherche) : la
// liste est filtree ici, sans pont, deux champs auraient fait le meme filtre (decision du plan, lot 7).
// « Denomination » reste un critere a part, comme en v2. Etapes, evenement et clic qui ouvre le dossier : inchanges.
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 5, 26/11/2026, marche du lot 17) : les instances viennent du
// pont (Pont/EcranDepotsJudBase.cs → Serveur/Socle/EcransMain/LectureDepotsJud.cs) : la serie JROP de la recherche de
// PROC_DEPOTJUD, date d'enrolement, dernier evenement survenu, « A facturer » quand aucun client (PROC_DEPOTJUD.cs:58-64),
// « En cours » quand une demande la tient. Sans serveur, les quatre instances de demonstration du pont, marquees
// « exemple » (ProvenanceEcran) ; en panne, le motif et aucune ligne. La mise en page ne change pas.
//
// LE GESTE (lot 8 du plan « parcours complet », 28/11/2026) : a l'etape « Nouvel evenement survenu », l'instance depliee
// recoit l'evenement et son depot numerote (Pont/EcranDepotsJudBase.cs, Deposer → Serveur/Socle/EcransMain/
// EcritureDepotsJud.cs). Ses codes sont ceux de RIG v2 (avec depot, compatibles avec le type de l'instance), lus dans la base ;
// aucun code : le motif est dit sous la liste. La date proposee est celle du jour.

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { Annonce, type CompteRendu, type ResultatGesteVue } from './formes/commun'
import { contientSansCasse, dateCourte, estBlanc } from '../criteres'
import {
  CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille, PiedListe, PointEtat,
  teinteStatut, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'
import type { ContexteDossierVue } from '../plan/types'
import { ProvenanceEcran, type ModeLecture } from '../provenance'

interface Etape {
  code: string
  libelle: string
}

interface Instance {
  numero: string
  denomination: string
  nature: string
  chambre: string
  /** aaaa-mm-jj */
  ouverture: string
  dernierEvenement: string
  etat: string
  statut: string
  aFacturer: boolean
  dossier: string
}

const Etapes: Etape[] = [
  { code: 'ETP_INFO_DEMANDE', libelle: 'Demande' },
  { code: 'ETP_INSTANCE_RECHERCHE', libelle: 'Etape de recherche d\'instance' },
  { code: 'ETP_ENTITE_JUDICIAIRE_READONLY', libelle: 'Entite judiciaire (lecture seule)' },
  { code: 'ETP_INSTANCE_SUMMARY', libelle: 'Informations sur l\'instance' },
  { code: 'ETP_EVENEMENT_SURVENU', libelle: 'Nouvel evenement survenu' },
  { code: 'ETP_FACTU_GESTCOM', libelle: 'Facturation et gestion commerciale' },
  { code: 'ETP_POST_VAL_INSTANCE', libelle: 'Recapitulatif instance' },
]

/** Ce que le pont rend (Pont/EcranDepotsJud.cs, VueDepotsJud). */
interface VueDepotsJud {
  lignes: Instance[]
  mode?: ModeLecture
  message?: string | null
  total: number
}

/** Le point d'etat suit le statut de l'instance (teinteStatut du socle) ; la pastille, la nature de la procedure. */
const TeinteNature: Record<string, Teinte> = { 'Redressement judiciaire': 'orange', 'Liquidation judiciaire': 'rouge', Sauvegarde: 'vert' }

/** Les colonnes du tableau a une ligne : chevron, Instance, Nature, Date d'ouverture, Dernier evenement, Etat, menu. */
const NbColonnes = 7

export interface ProprietesEcranDepotsJud {
  /** Remonte le NUMERO de dossier de l'instance cliquee : l'ecran ne connait pas la visionneuse. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranDepotsJud({ ouvrirDossier, dossier = null }: ProprietesEcranDepotsJud) {
  const [etapeCourante, setEtapeCourante] = useState('ETP_INSTANCE_RECHERCHE')
  // « Aller a une instance… » de la tete (l'ancien critere « N d'instance ») : ouvert depuis une affaire, son
  // instance y est cherchee par son numero.
  const [numInstance, setNumInstance] = useState(dossier?.numero ?? '')
  const [denomination, setDenomination] = useState('')
  // Le code de l'evenement survenu : la liste de l'instance depliee (Pont/EcranDepotsJudBase.cs, Codes ; lot 8 du plan « parcours
  // complet ») -- les codes avec depot compatibles avec son type, lus dans la base ; sans serveur, ceux de la demonstration.
  const [evenement, setEvenement] = useState('')
  const [codes, setCodes] = useState<{ code: string; libelle: string }[]>([])
  // Le motif d'une liste vide (aucun code compatible avec le type de l'instance), dit sous la liste.
  const [motifCodes, setMotifCodes] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<CompteRendu>(null)
  const [envoi, setEnvoi] = useState(false)
  const [relecture, setRelecture] = useState(0)
  // null tant que le pont n'a pas repondu (ou une fois la date effacee).
  const [dateEvenement, setDateEvenement] = useState<string | null>(null)
  // La case « Editer un certificat de dépôt » de l'operation du depot (OPE_DEPOT.Designer.cs:109), decochee par defaut
  // (OPE_EVENEMENT.Designer.cs:571) : cochee, le serveur pose le certificat dans la file des editions (lot 16 du plan-f4fb097f).
  // fix-ok: pas un correctif a l'aveugle -- ecart mesure a la reception du lot 8 (capture lot8\depotjud-banc-2-depose-relu.png : « le
  // certificat de dépôt et les éditions ne sont pas repris ») ; l'ecran n'avait pas la case de RIG v2, ajoutee ici (vitest rouge puis vert).
  const [certificat, setCertificat] = useState(false)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)
  // null tant que le pont n'a pas repondu : aucune ligne, jamais la demonstration a la place de la base.
  const [vue, setVue] = useState<VueDepotsJud | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueDepotsJud>('EcranDepotsJud.Vue').then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [relecture])

  // Les codes proposes pour l'instance depliee : relus a chaque instance choisie.
  useEffect(() => {
    if (ouverte === null) { setCodes([]); setMotifCodes(null); return }
    let perime = false
    void appeler<{ codes: { code: string; libelle: string }[]; message?: string | null }>('EcranDepotsJud.Codes', ouverte)
      .then((v) => {
        if (perime) return
        const liste = v?.codes ?? []
        setCodes(liste)
        setMotifCodes(v?.message ?? null)
        setEvenement((avant) => liste.some((c) => c.code === avant) ? avant : (liste[0]?.code ?? ''))
      })
      .catch(() => { if (!perime) { setCodes([]); setMotifCodes(null) } })
    return () => { perime = true }
  }, [ouverte])

  /** « Nouvel événement survenu » et son dépôt sur l'instance dépliée : écrits par le serveur, la liste se relit. */
  async function deposer(role: string) {
    setEnvoi(true)
    try {
      const r = await appeler<ResultatGesteVue>('EcranDepotsJud.Deposer', role, evenement, dateEvenement ?? '', certificat)
      setAnnonce(r.fait || r.sansEcrivain ? r.message : { refus: r.message })
      if (r.fait) setRelecture((n) => n + 1)
    } catch {
      setAnnonce({ refus: "Le pont n'a pas répondu : rien n'est enregistré." })
    } finally {
      setEnvoi(false)
    }
  }

  useEffect(() => {
    let perime = false
    void appeler<string>('EcranDepotsJud.DateDuJour')
      .then((d) => { if (!perime) setDateEvenement((avant) => avant ?? d) })
    return () => { perime = true }
  }, [])

  let q = vue?.lignes ?? []
  if (!estBlanc(numInstance)) q = q.filter((l) => contientSansCasse(l.numero, numInstance.trim()))
  if (!estBlanc(denomination)) q = q.filter((l) => contientSansCasse(l.denomination, denomination.trim()))
  if (etapeCourante === 'ETP_FACTU_GESTCOM') q = q.filter((l) => l.aFacturer)
  const trouvees = q

  // Les chiffres de la tete et du pied : sur la liste affichee.
  const enCours = trouvees.filter((l) => l.etat === 'En cours').length
  const aFacturer = trouvees.filter((l) => l.aFacturer).length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Etapes du depot</div>

        {Etapes.map((etape) => (
          <button key={etape.code} className={`rig-bascule${etapeCourante === etape.code ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setEtapeCourante(etape.code)}>
            <span className="rig-bascule__temoin"></span>
            <span>{etape.libelle}</span>
          </button>
        ))}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Denomination</span>
            <input className="rig-saisie-champ__saisie" value={denomination} onChange={(e) => setDenomination(e.target.value)} />
          </label>
        </div>
      </aside>

      <ListeModerne etiquette="Instances">
        <EnteteListe titre={Etapes.find((e) => e.code === etapeCourante)!.libelle}
                     synthese={<>{trouvees.length}{trouvees.length > 1 ? ' instances · ' : ' instance · '}<b>{enCours}</b>
                       {' en cours · '}<b>{aFacturer}</b>{' a facturer'}</>}
                     filtre={{ valeur: numInstance, changer: setNumInstance, indication: 'Aller a une instance…',
                               valider: () => { if (trouvees.length === 1) ouvrirDossier(trouvees[0]!.dossier) } }} />

        {vue && <ProvenanceEcran mode={vue.mode} message={vue.message} quoi="Instances" />}

        {etapeCourante === 'ETP_EVENEMENT_SURVENU' && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Evenement survenu</span>
              <select className="rig-saisie-champ__saisie" value={evenement} onChange={(e) => setEvenement(e.target.value)}
                      disabled={ouverte === null}>
                {ouverte === null && <option value="">Depliez une instance de la liste</option>}
                {codes.map((c) => <option key={c.code} value={c.code}>{c.code} — {c.libelle}</option>)}
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Date de l'evenement</span>
              <input className="rig-saisie-champ__saisie" type="date" value={dateEvenement ?? ''}
                     onChange={(e) => setDateEvenement(e.target.value || null)} />
            </label>
            <label className="rig-tcw__case">
              <input type="checkbox" checked={certificat} onChange={(e) => setCertificat(e.currentTarget.checked)} />
              <span>Editer un certificat de dépôt</span>
            </label>
            {ouverte !== null && motifCodes !== null && <p className="rig-eg-note" role="note">{motifCodes}</p>}
            {ouverte !== null && (
              <div className="rig-criteres__actions">
                <button className="rig-bouton rig-bouton--fort" disabled={envoi || evenement === ''} onClick={() => void deposer(ouverte)}>
                  Enregistrer le depot sur {ouverte}</button>
              </div>
            )}
            <Annonce texte={annonce} />
          </div>
        )}

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '19%' }} /><col style={{ width: '13%' }} />
              <col style={{ width: '18%' }} /><col style={{ width: '12%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Instance</th>
                <th>Nature</th>
                <th>Enrolement</th>
                <th>Dernier evenement</th>
                <th>Etat</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((inst) => {
                const estOuverte = ouverte === inst.numero
                const actions: ActionLigne[] = [{ libelle: 'Ouvrir le dossier', fort: true, agir: () => ouvrirDossier(inst.dossier) }]
                return (
                  <Fragment key={inst.numero}>
                  <tr className={estOuverte ? 'rig-lm-ouverte' : undefined} onClick={() => ouvrirDossier(inst.dossier)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : inst.numero)} />
                    <CelluleIdentite nom={inst.denomination} objet={inst.numero} titre={`${inst.denomination} · ${inst.numero}`} />
                    <td title={inst.nature}><Pastille teinte={TeinteNature[inst.nature] ?? 'gris'}>{inst.nature}</Pastille></td>
                    <td className="rig-table__mono">{dateCourte(inst.ouverture)}</td>
                    <td title={inst.dernierEvenement}>{inst.dernierEvenement}</td>
                    <td title={inst.etat}><PointEtat teinte={teinteStatut(inst.statut)}>{inst.etat}</PointEtat></td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de l'instance ${inst.numero}`} actions={[
                        ...actions,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : inst.numero) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={actions} champs={[
                      { libelle: 'Chambre', valeur: inst.chambre },
                      { libelle: 'A facturer', valeur: inst.aFacturer ? 'oui' : '' },
                      { libelle: 'Dossier', valeur: inst.dossier },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {trouvees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">Aucune instance ne repond a cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe>
          <span><b>{trouvees.length}</b>{trouvees.length > 1 ? ' instances affichees' : ' instance affichee'}</span>
          <span>En cours <b>{enCours}</b></span>
          <span>A facturer <b>{aFacturer}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
