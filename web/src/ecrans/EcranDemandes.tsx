// Ecran de la procedure PROC_DEMANDE.
// Motif de RIG v2 : CRITERES DE RECHERCHE a gauche, RESULTATS a droite
// (PROC_DEMANDE/RECHERCHE/OPE_SEARCH.cs et PROC_DEMANDE/RESULTATS).
// Les colonnes viennent de PROC_DEMANDE/DemandeDataGridView.cs.
//
// Porte depuis Components/Ecrans/EcranDemandes.razor (lot 3) : meme balisage,
// memes classes CSS. Le filtre et l'etat AFFICHE (la validation au controle
// final prime) sont calcules par Pont/EcranDemandes.cs sur JeuDemo.Demandes.
// La coquille .razor passe la liste des dossiers valides (sa fonction EstValide
// ne traverse pas vers React) et recoit ouvrir.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : les resultats prennent les briques
// de composants/ListeModerne.tsx. Tete (synthese, « Aller a une demande… » Ctrl K qui filtre la liste affichee,
// Entree ouvre le dossier si une seule demande reste), trois cartes qui POSENT les criteres existants (etats,
// « En cours uniquement », « Alerte uniquement »), des vues = jeux de criteres de gauche, export CSV. Le tableau
// passe a UNE ligne par demande (Demande, Traitement, Etat, Creation, Utilisateur) ; N Demande, N Dossier, Siren,
// Client, En cours et Date de validation sont dans la ligne depliee par le chevron. Les criteres, le clic qui
// ouvre le dossier et la ligne en alerte ne changent pas. Les vues ajoutees vivent le temps de la SESSION
// (decision du lot 1 : aucun reglage d'ecran ne les porte).

import { Fragment, useEffect, useState } from 'react'
import { appeler } from '../pont'
import { dateSaisie, useNombreSaisi } from '../saisie'
import type { ContexteDossierVue } from '../plan/types'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, EnteteListe, LigneDetail, ListeModerne, MenuLigne, Pastille,
  PiedListe, PointEtat, VuesListe, exporterCsv, type ActionLigne, type Teinte,
} from '../composants/ListeModerne'

export interface LigneDemande {
  numero: string
  nom: string
  dossier: string
  siren: string
  client: string
  traitement: string
  /** L'etat AFFICHE : « Validee » des que le dossier a passe le controle final. */
  etat: string
  statut: string
  enCours: boolean
  creation: string
  validation: string
  utilisateur: string
  alerte: boolean
  /** Le traitement nomme par son ecran (« Depots judiciaires ») : c'est lui qui s'affiche,
   *  jamais le code (Pont/EcranDemandes.cs : LibelleTraitement). */
  traitementLibelle: string
}

export interface VueDemandes {
  /** Les codes des traitements : la valeur du filtre « Processus ». */
  traitements: string[]
  etats: string[]
  lignes: LigneDemande[]
  total: number
  /** Le nom de chaque traitement, dans l'ordre des codes : ce que la liste montre. */
  libellesTraitements: string[]
}

export interface ProprietesEcranDemandes {
  /** Les dossiers qui ont passe le controle final (EstValide de Home.razor). */
  valides: string[]
  /** Remonte le numero du dossier a ouvrir dans la visionneuse. */
  ouvrir: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

/** Les criteres de gauche, tels que saisis. « Nombre maxi. » n'en est pas : c'est une limite d'affichage. */
export interface CriteresDemandes {
  critDemande: string
  critClient: string
  critProcessus: string
  critDu: string
  critAu: string
  critEtats: string[]
  critAlerte: boolean
  critEnCours: boolean
}

const SansCritere: CriteresDemandes = {
  critDemande: '', critClient: '', critProcessus: '', critDu: '', critAu: '', critEtats: [], critAlerte: false, critEnCours: false,
}

/** Ce que le pont recoit (Pont/EcranDemandes.cs, CriteresDemandes) : les dates vides deviennent null. */
function pourLePont(c: CriteresDemandes, topMax: number) {
  return {
    critDemande: c.critDemande, critClient: c.critClient, critProcessus: c.critProcessus,
    critDu: dateSaisie(c.critDu), critAu: dateSaisie(c.critAu),
    critEtats: c.critEtats, critAlerte: c.critAlerte, critEnCours: c.critEnCours, critTopMax: topMax,
  }
}

function memesCriteres(a: CriteresDemandes, b: CriteresDemandes): boolean {
  return a.critDemande === b.critDemande && a.critClient === b.critClient && a.critProcessus === b.critProcessus
    && a.critDu === b.critDu && a.critAu === b.critAu && a.critAlerte === b.critAlerte && a.critEnCours === b.critEnCours
    && [...a.critEtats].sort().join('|') === [...b.critEtats].sort().join('|')
}

/** Une vue enregistree : un nom et un jeu complet de criteres. */
export interface VueDemandesEnregistree {
  cle: string
  libelle: string
  criteres: CriteresDemandes
}

/** Les vues toujours la : les criteres d'etat de l'ecran, preposes. */
export const VuesPrereglees: VueDemandesEnregistree[] = [
  { cle: 'toutes', libelle: 'Toutes', criteres: SansCritere },
  { cle: 'en-cours', libelle: 'En cours', criteres: { ...SansCritere, critEnCours: true } },
  { cle: 'alerte', libelle: 'En alerte', criteres: { ...SansCritere, critAlerte: true } },
  { cle: 'validees', libelle: 'Validees', criteres: { ...SansCritere, critEtats: ['Validee'] } },
]

/** Les vues ajoutees par « + Nouvelle vue » : elles vivent le temps de la session (aucun reglage ne les porte). */
const VuesDeLaSession: VueDemandesEnregistree[] = []

/** Pour les tests : repartir d'une session sans vue ajoutee. */
export function oublierVuesDeLaSession(): void {
  VuesDeLaSession.length = 0
}

/** La teinte d'un etat de demande (JeuDemo.EtatsDemande) : la meme dans la ligne et dans la carte des etats. */
const TeinteEtat: Record<string, Teinte> = { 'En saisie': 'bleu', 'En controle': 'orange', Validee: 'vert', Rejetee: 'rouge' }

/**
 * La teinte de chaque traitement connu, une par traitement et dans le sens de la Recherche d'affaires (judiciaire
 * ardoise, radiation rouge, cloture gris). Le cycle sur cinq teintes donnait la meme couleur a « Demandes » et
 * « Actes numerises », a « Archivage / radiation » et « Depots judiciaires » (capture orange du lot 3, 24/11/2026).
 * La cle est le NOM du traitement, celui que la liste montre (Pont/EcranDemandes.cs, LibelleTraitement) : un ecran
 * ne porte aucun code interne, seul l'aiguillage aiguille par code (tests/NomMetierCheck).
 * fix-ok: NomMetierCheck rouge sur la premiere version, cles = codes internes ; cles passees aux noms affiches.
 */
const TeinteTraitement: Record<string, Teinte> = {
  Demandes: 'bleu', 'Depots judiciaires': 'ardoise', 'Archivage / radiation': 'rouge', Cloture: 'gris',
  'Actes numerises': 'violet', 'Arrivee du courrier': 'vert', BODACC: 'orange',
}
/** Un traitement que la liste ne connait pas encore : une teinte selon son rang dans la liste des codes. */
const TeintesTraitement: Teinte[] = ['bleu', 'violet', 'ardoise', 'vert', 'orange']

/** La societe et l'objet d'une demande : « Modification - SCI DU PARC » -> SCI DU PARC, Modification. */
export function identiteDemande(nom: string): { societe: string; objet: string } {
  const tiret = nom.indexOf(' - ')
  return tiret < 0 ? { societe: nom, objet: '' } : { societe: nom.slice(tiret + 3), objet: nom.slice(0, tiret) }
}

/** Les colonnes du tableau a une ligne : chevron, Demande, Traitement, Etat, Creation, Utilisateur, menu. */
const NbColonnes = 7

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « démande » trouve « Demande ».
 * fix-ok: contientSansCasse (criteres.ts) garde les accents ; test « Aller a une demande… » rouge sur « démande d2 », vert ici.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

export function EcranDemandes({ valides, ouvrir, dossier = null }: ProprietesEcranDemandes) {
  // Ouvert depuis une affaire : ses demandes, cherchees par son numero dans le
  // critere « Demande » (une demande RCS porte le numero de son dossier,
  // JeuDemo.Demandes), sans saisie. « Effacer » le vide, comme tout critere.
  const [critDemande, setCritDemande] = useState(dossier?.numero ?? '')
  const [critClient, setCritClient] = useState('')
  const [critProcessus, setCritProcessus] = useState('')
  const [critDu, setCritDu] = useState('')
  const [critAu, setCritAu] = useState('')
  const [critAlerte, setCritAlerte] = useState(false)
  const [critEnCours, setCritEnCours] = useState(false)
  const [critEtats, setCritEtats] = useState<string[]>([])
  const topMax = useNombreSaisi(50)
  // null tant que le pont n'a pas repondu : ni ligne ni « aucune demande ».
  const [vue, setVue] = useState<VueDemandes | null>(null)
  // La ligne depliee par son chevron (une a la fois).
  const [ouverte, setOuverte] = useState<string | null>(null)

  const criteres: CriteresDemandes = { critDemande, critClient, critProcessus, critDu, critAu, critEtats, critAlerte, critEnCours }
  function poserCriteres(c: CriteresDemandes) {
    setCritDemande(c.critDemande)
    setCritClient(c.critClient)
    setCritProcessus(c.critProcessus)
    setCritDu(c.critDu)
    setCritAu(c.critAu)
    setCritEtats(c.critEtats)
    setCritAlerte(c.critAlerte)
    setCritEnCours(c.critEnCours)
  }

  // La liste des valides est comparee par son CONTENU : la coquille la
  // recalcule a chaque rendu de l'accueil, sans qu'elle ait change.
  const clefValides = valides.join('|')

  useEffect(() => {
    let perime = false
    void appeler<VueDemandes>('EcranDemandes.Trouver', pourLePont({
      critDemande, critClient, critProcessus, critDu, critAu, critEtats, critAlerte, critEnCours,
    }, topMax.valeur), clefValides === '' ? [] : clefValides.split('|')).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critDemande, critClient, critProcessus, critDu, critAu, critEtats, critAlerte, critEnCours, topMax.valeur, clefValides])

  const trouvees = vue?.lignes ?? []

  // « Aller a une demande… » (Ctrl K) : filtre la liste AFFICHEE, sans toucher aux criteres du pont.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const chercheNormal = normaliser(cherche)
  const affichees = cherche === '' ? trouvees : trouvees.filter((d) => normaliser(
    [d.numero, d.nom, d.dossier, d.siren, d.siren.replace(/\s/g, ''), d.client, d.traitementLibelle, d.utilisateur].join(' ')).includes(chercheNormal))

  // Les vues : les preposees, puis celles de la session. Leur compte est demande au pont, critere par critere,
  // avec la meme limite d'affichage : c'est le nombre de lignes que la vue MONTRERAIT.
  const [vuesAjoutees, setVuesAjoutees] = useState<VueDemandesEnregistree[]>(() => [...VuesDeLaSession])
  const vues = [...VuesPrereglees, ...vuesAjoutees]
  const [comptes, setComptes] = useState<number[] | null>(null)
  const cleVues = JSON.stringify(vues.map((v) => v.criteres))
  useEffect(() => {
    let perime = false
    const listeValides = clefValides === '' ? [] : clefValides.split('|')
    Promise.all((JSON.parse(cleVues) as CriteresDemandes[]).map((c) =>
      appeler<VueDemandes>('EcranDemandes.Trouver', pourLePont(c, topMax.valeur), listeValides).then((v) => v.lignes.length))).then(
      (c) => { if (!perime) setComptes(c) },
      // Sans reponse, les puces restent sans compte.
      () => { if (!perime) setComptes(null) })
    return () => { perime = true }
  }, [cleVues, clefValides, topMax.valeur])
  const vueActive = vues.find((v) => memesCriteres(v.criteres, criteres))?.cle ?? null
  function enregistrerVue(nom: string) {
    VuesDeLaSession.push({ cle: `session-${VuesDeLaSession.length + 1}`, libelle: nom, criteres })
    setVuesAjoutees([...VuesDeLaSession])
  }

  function basculerEtat(etat: string) {
    setCritEtats((avant) => (avant.includes(etat) ? avant.filter((e) => e !== etat) : [...avant, etat]))
  }

  function effacer() {
    setCritDemande('')
    setCritClient('')
    setCritProcessus('')
    setCritDu('')
    setCritAu('')
    setCritAlerte(false)
    setCritEnCours(false)
    topMax.poser(50)
    setCritEtats([])
  }

  // Les chiffres de la tete, des cartes et du pied : sur la liste AFFICHEE.
  const enCours = affichees.filter((d) => d.enCours).length
  const alertes = affichees.filter((d) => d.alerte).length
  const etats = vue?.etats ?? []
  const traitements = vue?.traitements ?? []
  const teinteTraitement = (dem: LigneDemande): Teinte => {
    const connue = TeinteTraitement[dem.traitementLibelle]
    if (connue) return connue
    const rang = traitements.indexOf(dem.traitement)
    return rang < 0 ? 'gris' : TeintesTraitement[rang % TeintesTraitement.length]!
  }

  function exporter() {
    exporterCsv('demandes.csv',
      ['N Demande', 'Nom de la demande', 'N Dossier', 'Siren', 'Client', 'Traitement', 'Etat', 'En cours',
       'Date de creation', 'Date de validation', 'Utilisateur'],
      affichees.map((d) => [d.numero, d.nom, d.dossier, d.siren, d.client, d.traitementLibelle, d.etat, d.enCours ? 'oui' : '',
                            d.creation, d.validation, d.utilisateur]))
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Demande</span>
          <input className="rig-saisie-champ__saisie" placeholder="numero ou nom"
                 value={critDemande} onChange={(e) => setCritDemande(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Client</span>
          <input className="rig-saisie-champ__saisie" placeholder="nom ou numero de compte"
                 value={critClient} onChange={(e) => setCritClient(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Processus</span>
          <select className="rig-saisie-champ__saisie" value={critProcessus} onChange={(e) => setCritProcessus(e.target.value)}>
            <option value="">Tous</option>
            {(vue?.traitements ?? []).map((trait, i) => (
              <option key={trait} value={trait}>{vue?.libellesTraitements[i] ?? trait}</option>
            ))}
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Dates de la demande</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critDu} onChange={(e) => setCritDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date" value={critAu} onChange={(e) => setCritAu(e.target.value)} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Etats de la demande</div>
          {(vue?.etats ?? []).map((e) => (
            <button key={e} className={`rig-bascule${critEtats.includes(e) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerEtat(e)}>
              <span className="rig-bascule__temoin"></span>
              <span>{e}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <button className={`rig-bascule${critAlerte ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritAlerte(!critAlerte)}>
            <span className="rig-bascule__temoin"></span>
            <span>Alerte uniquement</span>
          </button>
          <button className={`rig-bascule${critEnCours ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setCritEnCours(!critEnCours)}>
            <span className="rig-bascule__temoin"></span>
            <span>En cours uniquement</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nombre maxi. de demandes affichees</span>
          <input className="rig-saisie-champ__saisie" type="number" min="1" max="500"
                 value={topMax.texte} onChange={(e) => topMax.saisir(e.target.value)} />
        </label>

        <div className="rig-criteres__actions">
          <button className="rig-bouton" onClick={effacer}>Effacer</button>
        </div>
      </aside>

      <ListeModerne etiquette="Demandes">
        <EnteteListe titre="Resultats de la recherche"
                     synthese={vue && <>{affichees.length}{' affichee(s) sur '}{vue.total}{' · '}<b>{enCours}</b>{' en cours · '}
                       <b>{alertes}</b>{' en alerte'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a une demande…',
                               valider: () => { if (affichees.length === 1) ouvrir(affichees[0]!.dossier) } }} />

        {vue && (
          <CartesChiffrees cartes={[
            { cle: 'etats', titre: 'Etats', valeur: affichees.length, complement: affichees.length > 1 ? 'demandes' : 'demande', legende: true,
              ...(critEtats.length > 0 ? { choisir: () => setCritEtats([]), indication: 'Tous les etats' } : {}),
              segments: etats.map((e) => ({
                cle: e, libelle: e, teinte: TeinteEtat[e] ?? 'gris',
                valeur: affichees.filter((d) => d.etat === e).length,
                texte: String(affichees.filter((d) => d.etat === e).length),
                actif: critEtats.includes(e), choisir: () => basculerEtat(e),
              })) },
            { cle: 'en-cours', titre: 'En cours', valeur: enCours, complement: `sur ${affichees.length}`,
              anneau: { part: enCours, total: affichees.length, teinte: 'bleu' },
              actif: critEnCours, choisir: () => setCritEnCours(!critEnCours),
              indication: critEnCours ? 'Toutes les demandes' : 'Seulement les demandes en cours' },
            { cle: 'alerte', titre: 'En alerte', valeur: alertes, complement: `sur ${affichees.length}`,
              anneau: { part: alertes, total: affichees.length, teinte: 'rouge' },
              actif: critAlerte, choisir: () => setCritAlerte(!critAlerte),
              indication: critAlerte ? 'Toutes les demandes' : 'Seulement les demandes en alerte' },
          ]} />
        )}

        <VuesListe vues={vues.map((v, i) => ({ cle: v.cle, libelle: v.libelle, compte: comptes?.[i] }))} active={vueActive}
                   choisir={(cle) => { const v = vues.find((x) => x.cle === cle); if (v) poserCriteres(v.criteres) }}
                   enregistrer={enregistrerVue}>
          <button type="button" className="rig-lm-outil" disabled={affichees.length === 0} onClick={exporter}>Exporter</button>
        </VuesListe>

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: 30 }} /><col /><col style={{ width: '20%' }} /><col style={{ width: '14%' }} />
              <col style={{ width: '12%' }} /><col style={{ width: '13%' }} /><col style={{ width: 34 }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                <th>Demande</th>
                <th>Traitement</th>
                <th>Etat</th>
                <th>Date de creation</th>
                <th>Utilisateur</th>
                <th aria-label="Actions"></th>
              </tr>
            </thead>
            <tbody>
              {affichees.map((dem, i) => {
                const cle = `${i}:${dem.numero}`
                const estOuverte = ouverte === cle
                const { societe, objet } = identiteDemande(dem.nom)
                const ouvrirDossier: ActionLigne = { libelle: 'Ouvrir le dossier', fort: true, agir: () => ouvrir(dem.dossier) }
                const classes = [dem.alerte ? 'rig-table__ligne--alerte' : '', estOuverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={cle}>
                  <tr className={classes || undefined} onClick={() => ouvrir(dem.dossier)}>
                    <CelluleChevron ouverte={estOuverte} basculer={() => setOuverte(estOuverte ? null : cle)} />
                    <CelluleIdentite nom={societe} titre={`${dem.nom} · ${dem.numero}`}
                                     objet={<>{objet}{objet !== '' ? ' · ' : ''}<span>{dem.numero}</span></>} />
                    <td title={dem.traitementLibelle}><Pastille teinte={teinteTraitement(dem)}>{dem.traitementLibelle}</Pastille></td>
                    <td title={dem.etat}><PointEtat teinte={TeinteEtat[dem.etat] ?? 'gris'}>{dem.etat}</PointEtat></td>
                    <td className="rig-table__mono">{dem.creation}</td>
                    <td title={dem.utilisateur}>{dem.utilisateur}</td>
                    <td className="rig-lm-cellule-menu">
                      <MenuLigne libelle={`Actions de la demande ${dem.numero}`} actions={[
                        ouvrirDossier,
                        { libelle: estOuverte ? 'Replier la ligne' : 'Deplier la ligne', agir: () => setOuverte(estOuverte ? null : cle) },
                      ]} />
                    </td>
                  </tr>
                  {estOuverte && (
                    <LigneDetail colonnes={NbColonnes} actions={[ouvrirDossier]} champs={[
                      { libelle: 'N Demande', valeur: dem.numero },
                      { libelle: 'N Dossier', valeur: dem.dossier },
                      { libelle: 'Siren', valeur: dem.siren },
                      { libelle: 'Client', valeur: dem.client },
                      { libelle: 'En cours', valeur: dem.enCours ? 'oui' : 'non' },
                      { libelle: 'Date de validation', valeur: dem.validation },
                    ]} />
                  )}
                  </Fragment>
                )
              })}
              {vue && trouvees.length === 0 && (
                <tr>
                  <td colSpan={NbColonnes} className="rig-table__vide">
                    Aucune demande ne repond a ces criteres.
                  </td>
                </tr>
              )}
              {trouvees.length > 0 && affichees.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucune des ${trouvees.length} demandes ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur une demande pour ouvrir son dossier.">
          <span><b>{affichees.length}</b>{affichees.length > 1 ? ' demandes affichees' : ' demande affichee'}</span>
          <span>En cours <b>{enCours}</b></span>
          <span>En alerte <b>{alertes}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
