// Ecran de la procedure PROC_REQUI - requisitions / commande de documents (fiche 275).
//
// Il porte le PARCOURS de PROC_REQUI/Operations, ecrit par William LIVIO le
// 22/05/2026 sur la branche Feature/RCS/275_Refonte_Requi :
//   OPE_REQUI_RECHERCHE  -> les criteres, la limite de dossiers, le compteur
//   OPE_REQUI_RESULTATS  -> la grille des entreprises trouvees
//   OPE_REQUI_PANIER     -> les commandes groupees par entreprise + adresse
//   OPE_REQUI_ARTICLES   -> les documents et les 37 cases d inscription
// Les libelles, les colonnes et les regles viennent de ces fichiers ; rien n est
// invente. Aucune base n est branchee : les societes sont un jeu d exemple.
//
// Porte depuis Components/Ecrans/EcranRequisitions.razor (lot 2, 18/11/2026) :
// meme balisage, memes classes CSS. ECRAN A SESSION (src/session.tsx) : l'etat
// et les regles de l'ancien @code vivent dans Pont/EcranRequisitions.cs
// (SessionRequisitions) ; cet ecran affiche la vue et remonte chaque geste.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 6) : les deux listes de resultats prennent
// les briques de composants/ListeModerne.tsx.
//   - Zone haute, les commandes web : tete (titre, synthese), vues = la bascule « Masquer les traitees » d'avant
//     (Toutes, A traiter), « Trier » sur les 9 cles du pont et « Marquer traitee » dans la barre ; societe visee en
//     avatar + nom, famille en pastille, etat en point colore. Neuf colonnes : origine, demandeur, EDI et SIREN passent
//     dans la ligne depliee par le chevron, avec « Rapprocher d'un dossier » (le double-clic, toujours la).
//     Ni pied ni filtre Ctrl K ici : le compte est deja dans la synthese, il n'y a aucun montant a totaliser, l'aide
//     reste l'infobulle de la ligne, et la bande est bornee a 32vh (rig-accueil.css, .rig-cmdweb) ; Ctrl K appartient
//     a la liste des resultats, un seul champ par ecran.
//   - A droite : les onglets Resultats, Panier, Articles devenus des puces de vues (meme geste « Volet »), tete avec
//     le compteur du pont, « Aller a une societe… » Ctrl K qui filtre les resultats affiches. Resultats : societe en
//     avatar + nom (gestion et SIREN en gris), type en pastille, etat (radiee, non inscrite, inscrite) en point colore ;
//     l'Id, le SIREN, la gestion et l'adresse detaillee sont dans la ligne depliee, avec « Ajouter au panier » (le
//     double-clic). Panier : meme identite ; pied de liste sous chacun. Le volet Articles est un formulaire, pas une
//     liste : il ne change pas.
// fix-ok: application du socle des listes en plusieurs editions (lot 6), pas un correctif a l'aveugle.
//
// LA BASE DU GREFFE (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026, marche du lot 17) : « Rechercher » lit les dossiers
// de la base (Pont/EcranRequisitionsBase.cs → Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs, la requete de « Dossier RCS »)
// par raison sociale, SIREN et adresse du siege ; une note dit ce que la recherche ne reprend pas encore du moteur de RIG v2. Les
// commandes web restent la demonstration, marquee « exemple ». Sans serveur, toute la demonstration ; en panne, le motif.

import { Fragment, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { EnteteTriable } from '../composants/EnteteTriable'
import {
  CelluleChevron, CelluleIdentite, ChoixTri, EnteteListe, LigneDetail, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe, normaliser,
  type Teinte,
} from '../composants/ListeModerne'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'
import { Exemple } from './formes/commun'
import { Saisie, useSession, type PontDeSession } from '../session'
import type { ContexteDossierVue } from '../plan/types'
import { toucheDeRecherche } from '../plan/clavier'

export interface LigneCommandeWebRequi {
  /** Sa place dans le jeu de l'ecran : elle revient avec le clic. */
  rang: number
  classe: string
  origine: string
  numero: string
  recue: string
  demandeur: string
  famille: string
  codeEdi: string
  societe: string
  siren: string
  etat: string
}

export interface LigneEntrepriseRequi {
  rang: number
  radie: boolean
  infobulle: string
  id: string
  siren: string
  gestion: string
  designation: string
  typeAdresse: string
  adresse: string
  type: string
  nonInscrit: boolean
  nbInscriptions: number
}

export interface LignePanierRequi {
  rang: number
  enCours: boolean
  numCommande: number
  siren: string
  gestion: string
  designation: string
  adresse: string
  resume: string
}

export interface CaseArticleRequi {
  libelle: string
  titre: string
  cochee: boolean
  qte: number
}

export interface CriteresRequi {
  raisonSociale: string
  siren: string
  dirigeant: string
  activite: string
  numVoie: string
  adresse: string
  codePostal: string
  ville: string
  phonetique: boolean
  etendre: boolean
  tousEtb: boolean
  supprCompris: boolean
  guichet: boolean
  limiteDossiers: number
}

/** La commande en cours ; les dates sont au format d'un <input type="date">. */
export interface ArticlesRequi {
  numCommande: number
  designation: string
  kBisPossible: boolean
  nbKBis: number
  nbHistorique: number
  nbDAS: number
  cnfPossible: boolean
  nbCertifNonFaillite: number
  dateCnf: string
  nbCertifNonRcs: number
  dateCnRcs: string
  nbCertifNonRsac: number
  dateCnRsac: string
  nbCertifNonOppo: number
  dateCnOppo: string
  nbDjCopie: number
  nbDjExpedition: number
  nbNavire: number
  nbBeInterne: number
  nbBeExterne: number
  nomSci: string
  nomRequi: string
  dateTravail: string
  travailFrais: boolean
  ageTravailJours: number
  joursFraicheur: number
  toutCoche: boolean
  cases: CaseArticleRequi[]
  nbInscr: number
}

export interface VueRequisitions {
  session: string
  nbATraiter: number
  masquerTraitees: boolean
  cleTri: string | null
  triAscendant: boolean
  commandesWeb: LigneCommandeWebRequi[]
  commandeWebChoisie: boolean
  criteres: CriteresRequi
  raisonSocialeInvalide: boolean
  volet: string
  nbPanier: number
  compteur: string
  message: string
  trouvees: LigneEntrepriseRequi[]
  aRecherche: boolean
  interrompue: boolean
  limiteRetenue: number
  panier: LignePanierRequi[]
  articles: ArticlesRequi | null
  /** D'ou viennent les entreprises trouvees (Pont/EcranRequisitionsBase.cs) ; absent : la demonstration d'avant le lot 6. */
  modeResultats?: ModeLecture
  /** En base : ce que la recherche reprend ou non de RIG v2 ; en panne : le motif. */
  messageResultats?: string | null
}

const pont: PontDeSession<VueRequisitions> = {
  ouvrir: () => appeler<VueRequisitions>('EcranRequisitions.Ouvrir'),
  agir: (session, geste, valeur) => appeler<VueRequisitions>('EcranRequisitions.Agir', session, geste, valeur),
  fermer: (session) => appeler<void>('EcranRequisitions.Fermer', session),
}

/** Les colonnes triables de la zone haute : libelle, cle (celle du pont), classe. */
const colonnesCommandesWeb: ReadonlyArray<readonly [string, string, string?]> = [
  ['Origine', 'Origine'],
  ['N commande', 'Numero'],
  ['Recue le', 'Recue'],
  ['Demandeur', 'Demandeur'],
  ['Famille', 'Famille'],
  ['EDI', 'CodeEdi', 'rig-table__centre'],
  ['Societe visee', 'Societe'],
  ['Siren', 'Siren'],
  ['Etat', 'Etat', 'rig-table__centre'],
]

const bascule = (actif: boolean) => `rig-bascule ${actif ? 'rig-bascule--actif' : ''}`

/** Les colonnes de la zone haute restees au tableau, dans leur ordre ; les autres sont dans la ligne depliee
 *  (et toutes dans « Trier »). */
const entetesCommandesWeb = ['Societe', 'Numero', 'Recue', 'Famille', 'Etat']
  .map((cle) => colonnesCommandesWeb.find(([, c]) => c === cle)!)
/** Chevron, Societe visee, N commande, Recue le, Famille, Etat. */
const NbColonnesWeb = 6
/** Chevron, Societe, Adresse, Type, Etat, Nb inscriptions. */
const NbColonnesResultats = 6

const TeinteFamille: Record<string, Teinte> = { Endettement: 'orange', Faillite: 'rouge', 'Non opposition': 'bleu' }
const TeinteTypeSociete: Record<string, Teinte> = { PM: 'bleu', PP: 'violet' }
const TitreVolet: Record<string, string> = { resultats: 'Resultats', panier: 'Panier', articles: 'Articles commandes' }

/** L'objet gris d'une societe : « 2001B00042 · 412 345 678 », sans « · » quand l'un des deux manque. */
function GestionSiren({ gestion, siren }: { gestion: string; siren: string }) {
  return <>{gestion !== '' && <span>{gestion}</span>}{gestion !== '' && siren !== '' && ' · '}{siren}</>
}

export interface ProprietesEcranRequisitions {
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranRequisitions({ dossier = null }: ProprietesEcranRequisitions) {
  const [vue, geste] = useSession(pont)
  // Les lignes depliees par leur chevron (une par liste), par rang ; reglage d'affichage, hors session.
  const [webOuverte, setWebOuverte] = useState<number | null>(null)
  const [societeOuverte, setSocieteOuverte] = useState<number | null>(null)
  // « Aller a une societe… » (Ctrl K) : filtre les resultats AFFICHES, sans geste vers la session.
  const [rapide, setRapide] = useState('')

  // Ouvert depuis une affaire : des que la session repond, son entreprise est
  // cherchee par son SIREN -- les gestes de l'agent qui le saisit puis clique
  // « Rechercher ». Une seule fois : ensuite, les criteres sont les siens. Sans
  // SIREN (affaire de test), rien n'est pose : le critere reste a saisir.
  const preRempli = useRef(false)
  const ouverte = vue !== null
  useEffect(() => {
    if (!ouverte || dossier === null || dossier.siren.length === 0 || preRempli.current) return
    preRempli.current = true
    geste('Critere', { champ: 'CritSiren', valeur: dossier.siren })
    geste('Rechercher')
  }, [ouverte, dossier, geste])

  // Avant la premiere reponse du pont : le cadre seul, sans aucune valeur fausse.
  if (!vue) return <div className="rig-proc__corps rig-proc__corps--cmdweb"></div>

  const crit = vue.criteres
  const a = vue.articles
  const critere = (champ: string) => (valeur: string) => geste('Critere', { champ, valeur })
  const article = (champ: string) => (valeur: string) => geste('Article', { champ, valeur })
  const rechercher = () => geste('Rechercher')
  const surToucheCritere = (e: KeyboardEvent) => {
    // F5 efface les criteres et la liste, comme RAZ dans RIG v2 (frmREQUI.cs:421) : le bouton Reinitialiser.
    if (e.key === 'F5') { e.preventDefault(); geste('Effacer'); return }
    // Entree ou F7 dans le bloc des criteres = clic sur Rechercher : le geste de RIG v2 (frmREQUI.cs:424-425).
    if (toucheDeRecherche(e)) rechercher()
  }

  /** Un champ nombre de la commande en cours (@bind d'un int : remonte au « change »). */
  const nombre = (champ: string, valeur: number, desactive = false) => (
    <Saisie className="rig-saisie-champ__saisie" type="number" min="0" disabled={desactive}
            quand="change" valeur={String(valeur)} changer={article(champ)} />
  )
  const date = (champ: string, valeur: string) => (
    <Saisie className="rig-saisie-champ__saisie" type="date" quand="change" valeur={valeur} changer={article(champ)} />
  )
  const cherche = normaliser(rapide.trim())
  const trouvees = cherche === '' ? vue.trouvees : vue.trouvees.filter((soc) => normaliser(
    `${soc.designation} ${soc.siren} ${soc.siren.replace(/\s/g, '')} ${soc.gestion} ${soc.id} ${soc.adresse}`).includes(cherche))
  const radiees = trouvees.filter((soc) => soc.radie).length

  return (
    <div className="rig-proc__corps rig-proc__corps--cmdweb">

      {/* Zone HAUTE : les commandes passees sur le web et arrivees par EDI.
          C est le raccordement demande par la fiche 275 : choisir une commande
          remplit les criteres et lance la recherche pour rapprocher un dossier. */}
      <section className="rig-cmdweb">
        <EnteteListe titre="Commandes web a traiter"
                     synthese={<><b>{vue.nbATraiter}</b>{' a traiter · '}{vue.commandesWeb.length}{' affichee(s)'}</>} />
        {/* Lot 6 du plan « RIG v3 100 % fonctionnel » : les commandes web restent la demonstration (elles se lisent avec
            l'ecran « Commandes web », hors de ce lot) ; seule la recherche des entreprises lit la base du greffe. */}
        <p className="rig-eg-note" role="note"><Exemple /> Commandes web de démonstration : elles ne sont pas encore lues dans la base du greffe.</p>
        <VuesListe vues={[{ cle: 'toutes', libelle: 'Toutes' }, { cle: 'a-traiter', libelle: 'A traiter', compte: vue.nbATraiter }]}
                   active={vue.masquerTraitees ? 'a-traiter' : 'toutes'}
                   choisir={(cle) => { if ((cle === 'a-traiter') !== vue.masquerTraitees) geste('MasquerTraitees') }}>
          {/* Le pont ne connait que la bascule d'une cle : l'ordre d'arrivee se retrouve en rouvrant l'ecran. */}
          <ChoixTri cles={colonnesCommandesWeb.map(([libelle, cle]) => [libelle, cle] as const)} cleActive={vue.cleTri}
                    ascendant={vue.triAscendant} origine="Ordre d'arrivee"
                    choisir={(cle) => { if (cle !== null && cle !== vue.cleTri) geste('BasculerTri', cle) }}
                    inverser={() => { if (vue.cleTri !== null) geste('BasculerTri', vue.cleTri) }} />
          <button type="button" className="rig-lm-outil" disabled={!vue.commandeWebChoisie}
                  onClick={() => geste('MarquerTraitee')}>Marquer traitee</button>
        </VuesListe>
        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col style={{ width: '30px' }} /><col /><col style={{ width: '14%' }} /><col style={{ width: '16%' }} />
              <col style={{ width: '15%' }} /><col style={{ width: '13%' }} />
            </colgroup>
            <thead>
              <tr>
                <th aria-label="Deplier"></th>
                {entetesCommandesWeb.map(([libelle, cle]) => (
                  <EnteteTriable key={cle} libelle={libelle} cle={cle} cleActive={vue.cleTri}
                                 ascendant={vue.triAscendant} trier={(c) => geste('BasculerTri', c)} />
                ))}
              </tr>
            </thead>
            <tbody>
              {vue.commandesWeb.map((web) => {
                const ouverte = webOuverte === web.rang
                const classes = [web.classe, ouverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                return (
                  <Fragment key={web.rang}>
                    <tr className={classes || undefined}
                        title="Double-cliquez pour rapprocher cette commande d un dossier."
                        onClick={() => geste('ChoisirCommandeWeb', web.rang)}
                        onDoubleClick={() => geste('RapprocherCommandeWeb', web.rang)}>
                      <CelluleChevron ouverte={ouverte} basculer={() => setWebOuverte(ouverte ? null : web.rang)} />
                      <CelluleIdentite nom={web.societe} objet={web.siren} titre={`${web.societe} · ${web.siren}`} />
                      <td className="rig-table__mono">{web.numero}</td>
                      <td className="rig-table__mono">{web.recue}</td>
                      <td title={web.famille}><Pastille teinte={TeinteFamille[web.famille] ?? 'ardoise'}>{web.famille}</Pastille></td>
                      <td>
                        <PointEtat teinte={web.etat === 'Traitee' ? 'vert' : 'orange'}>{web.etat}</PointEtat>
                      </td>
                    </tr>
                    {ouverte && (
                      <LigneDetail colonnes={NbColonnesWeb} champs={[
                        { libelle: 'Origine', valeur: <>{web.origine}</> },
                        { libelle: 'Demandeur', valeur: web.demandeur },
                        { libelle: 'EDI', valeur: <span className="rig-table__mono">{web.codeEdi}</span> },
                        { libelle: 'Siren', valeur: <span className="rig-table__mono">{web.siren}</span> },
                      ]} actions={[
                        { libelle: 'Rapprocher d\'un dossier', fort: true, agir: () => geste('RapprocherCommandeWeb', web.rang) },
                      ]} />
                    )}
                  </Fragment>
                )
              })}
              {vue.commandesWeb.length === 0 && (
                <tr><td colSpan={NbColonnesWeb} className="rig-table__vide">Aucune commande web en attente.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>


      <aside className="rig-criteres" onKeyDown={surToucheCritere}>
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Raison sociale</span>
          <Saisie className="rig-saisie-champ__saisie" placeholder="nom de la societe"
                  valeur={crit.raisonSociale} changer={critere('CritRaisonSociale')} />
        </label>
        {vue.raisonSocialeInvalide && (
          <div className="rig-saisie-champ__refus">Designation invalide : saisissez autre chose que des chiffres.</div>
        )}

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N identification</span>
          <Saisie className="rig-saisie-champ__saisie" placeholder="siren"
                  valeur={crit.siren} changer={critere('CritSiren')} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Dirigeant</span>
          <Saisie className="rig-saisie-champ__saisie" valeur={crit.dirigeant} changer={critere('CritDirigeant')} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Activite</span>
          <Saisie className="rig-saisie-champ__saisie" valeur={crit.activite} changer={critere('CritActivite')} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Adresse</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">N voie</span>
              <Saisie className="rig-saisie-champ__saisie" valeur={crit.numVoie} changer={critere('CritNumVoie')} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Adresse</span>
              <Saisie className="rig-saisie-champ__saisie" valeur={crit.adresse} changer={critere('CritAdresse')} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code postal</span>
              <Saisie className="rig-saisie-champ__saisie" valeur={crit.codePostal} changer={critere('CritCodePostal')} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Ville</span>
              <Saisie className="rig-saisie-champ__saisie" valeur={crit.ville} changer={critere('CritVille')} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__bloc">
          <button className={bascule(crit.phonetique)} onClick={() => geste('BasculerCritere', 'CritPhonetique')}>
            <span className="rig-bascule__temoin"></span><span>Phonetique</span>
          </button>
          <button className={bascule(crit.etendre)} onClick={() => geste('BasculerCritere', 'CritEtendre')}>
            <span className="rig-bascule__temoin"></span><span>Etendre la recherche</span>
          </button>
          <button className={bascule(crit.tousEtb)} onClick={() => geste('BasculerCritere', 'CritTousEtb')}>
            <span className="rig-bascule__temoin"></span><span>Tous les etablissements</span>
          </button>
          <button className={bascule(crit.supprCompris)} onClick={() => geste('BasculerCritere', 'CritSupprCompris')}>
            <span className="rig-bascule__temoin"></span><span>Inclure les supprimes</span>
          </button>
          <button className={bascule(crit.guichet)} onClick={() => geste('BasculerCritere', 'CritGuichet')}>
            <span className="rig-bascule__temoin"></span><span>Guichet</span>
          </button>
        </div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Limite max dossiers (50 a 500)</span>
          <Saisie className="rig-saisie-champ__saisie" type="number" min="50" max="500" quand="change"
                  valeur={String(crit.limiteDossiers)} changer={(v) => geste('LimiteDossiers', v)} />
        </label>

        <div className="rig-criteres__actions">
          {/* .rig-criteres__actions n'est pas en colonne souple : le blanc que
              Razor gardait entre les deux boutons les espace (web/README.md). */}
          <button className="rig-bouton rig-bouton--fort" onClick={rechercher}>Rechercher</button>{' '}
          <button className="rig-bouton" onClick={() => geste('Effacer')}>Reinitialiser</button>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Commandes particulieres</div>
          <button className="rig-bouton rig-bouton--plat" onClick={() => geste('NonPorte', "L edition de l Etat de synthese")}>Etat de synthese</button>
          <button className="rig-bouton rig-bouton--plat" onClick={() => geste('NonPorte', 'La visualisation du KBIS')}>Voir le KBIS</button>
          <button className="rig-bouton rig-bouton--plat" onClick={() => geste('NonPorte', 'La commande de decisions judiciaires')}>Cmde decisions</button>
          <button className="rig-bouton rig-bouton--plat" onClick={() => geste('NonPorte', 'La commande par navire')}>Cmde par navire</button>
        </div>
      </aside>

      <ListeModerne etiquette={TitreVolet[vue.volet] ?? 'Resultats'}>
        <EnteteListe titre={TitreVolet[vue.volet] ?? 'Resultats'} synthese={vue.compteur}
                     filtre={vue.volet === 'resultats'
                       ? { valeur: rapide, changer: setRapide, indication: 'Aller a une societe…' } : undefined} />
        {vue.volet === 'resultats' && vue.aRecherche && (
          <>
            <ProvenanceEcran mode={vue.modeResultats} message={vue.messageResultats} quoi="Entreprises" />
            {vue.modeResultats === 'serveur' && vue.messageResultats && <p className="rig-eg-note" role="note">{vue.messageResultats}</p>}
          </>
        )}
        <VuesListe vues={[
                     { cle: 'resultats', libelle: 'Resultats', compte: vue.aRecherche ? vue.trouvees.length : undefined },
                     { cle: 'panier', libelle: 'Panier', compte: vue.nbPanier },
                     { cle: 'articles', libelle: 'Articles commandes' },
                   ]}
                   active={vue.volet} choisir={(cle) => geste('Volet', cle)} />

        {vue.message !== '' && (
          <div className="rig-nonmaq__texte" style={{ padding: '0 var(--rig-esp-4) var(--rig-esp-3)' }}>{vue.message}</div>
        )}

        {vue.volet === 'resultats' ? (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table rig-lm-table">
                <colgroup>
                  <col style={{ width: '30px' }} /><col style={{ width: '34%' }} /><col /><col style={{ width: '9%' }} />
                  <col style={{ width: '15%' }} /><col style={{ width: '12%' }} />
                </colgroup>
                <thead>
                  <tr>
                    <th aria-label="Deplier"></th><th>Designation</th><th>Adresse</th>
                    <th>Type</th><th>Etat</th>
                    <th className="rig-lm-droite">Nb Inscriptions</th>
                  </tr>
                </thead>
                <tbody>
                  {trouvees.map((soc) => {
                    const ouverte = societeOuverte === soc.rang
                    const classes = [soc.radie ? 'rig-table__ligne--inerte' : '', ouverte ? 'rig-lm-ouverte' : ''].filter(Boolean).join(' ')
                    return (
                      <Fragment key={soc.rang}>
                        <tr className={classes || undefined}
                            title={soc.infobulle}
                            onDoubleClick={() => geste('AjouterAuPanier', soc.rang)}>
                          <CelluleChevron ouverte={ouverte} basculer={() => setSocieteOuverte(ouverte ? null : soc.rang)} />
                          <CelluleIdentite nom={soc.designation}
                                           objet={<GestionSiren gestion={soc.gestion} siren={soc.siren} />} />
                          <td title={`${soc.typeAdresse} · ${soc.adresse}`}>{soc.adresse}</td>
                          <td><Pastille teinte={TeinteTypeSociete[soc.type] ?? 'ardoise'}>{soc.type}</Pastille></td>
                          <td>
                            {soc.radie ? <PointEtat teinte="rouge">Radiee</PointEtat>
                              : soc.nonInscrit ? <PointEtat teinte="gris">Non inscrite</PointEtat>
                              : <PointEtat teinte="vert">Inscrite</PointEtat>}
                          </td>
                          <td className="rig-lm-droite">{soc.nbInscriptions}</td>
                        </tr>
                        {ouverte && (
                          <LigneDetail colonnes={NbColonnesResultats} champs={[
                            { libelle: 'Id', valeur: <span className="rig-table__mono">{soc.id}</span> },
                            { libelle: 'Siren', valeur: <span className="rig-table__mono">{soc.siren}</span> },
                            { libelle: 'Gestion', valeur: <span className="rig-table__mono">{soc.gestion}</span> },
                            { libelle: 'Type Adresse', valeur: soc.typeAdresse },
                            { libelle: 'Adresse', valeur: soc.adresse },
                            { libelle: 'Non Inscrit', valeur: soc.nonInscrit ? 'oui' : 'non' },
                          ]} actions={[{ libelle: 'Ajouter au panier', fort: true, agir: () => geste('AjouterAuPanier', soc.rang) }]} />
                        )}
                      </Fragment>
                    )
                  })}
                  {vue.trouvees.length === 0 && (
                    <tr><td colSpan={NbColonnesResultats} className="rig-table__vide">
                      {vue.aRecherche ? 'Aucune societe ne repond a ces criteres.' : 'Saisissez un critere puis lancez la recherche.'}
                    </td></tr>
                  )}
                  {vue.trouvees.length > 0 && trouvees.length === 0 && (
                    <tr><td colSpan={NbColonnesResultats} className="rig-table__vide">
                      {`Aucune des ${vue.trouvees.length} societes ne contient « ${rapide.trim()} ».`}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
            {vue.interrompue && (
              <div className="rig-nonmaq__texte" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
                Votre recherche a ete interrompue a {vue.limiteRetenue} dossiers.
                Veuillez preciser votre recherche pour reduire le nombre de resultats.
              </div>
            )}
            <PiedListe aide="Double-clic sur une ligne pour l'ajouter au panier.">
              <span><b>{trouvees.length}</b>{' societe(s) affichee(s)'}</span>
              <span>Radiees <b>{radiees}</b></span>
            </PiedListe>
          </>
        ) : vue.volet === 'panier' ? (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table rig-lm-table">
                <colgroup>
                  <col style={{ width: '44px' }} /><col style={{ width: '32%' }} /><col style={{ width: '26%' }} /><col />
                </colgroup>
                <thead>
                  <tr>
                    <th>N</th><th>Designation</th><th>Adresse</th><th>Articles commandes</th>
                  </tr>
                </thead>
                <tbody>
                  {vue.panier.map((cmd) => (
                    <tr key={cmd.rang} className={cmd.enCours ? 'rig-table__ligne--alerte' : ''}
                        onClick={() => geste('Selectionner', cmd.rang)}
                        onDoubleClick={() => geste('Supprimer', cmd.rang)}>
                      <td className="rig-table__mono">{cmd.numCommande}</td>
                      <CelluleIdentite nom={cmd.designation} objet={<GestionSiren gestion={cmd.gestion} siren={cmd.siren} />} />
                      <td title={cmd.adresse}>{cmd.adresse}</td>
                      <td title={cmd.resume}>{cmd.resume}</td>
                    </tr>
                  ))}
                  {vue.panier.length === 0 && (
                    <tr><td colSpan={4} className="rig-table__vide">
                      Le panier est vide : double-cliquez une ligne des resultats pour l y ajouter.
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
            <div className="rig-criteres__actions" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
              <button className="rig-bouton" onClick={() => geste('Valider')}>Valider</button>{' '}
              <button className="rig-bouton rig-bouton--fort" onClick={() => geste('Terminer')}>Terminer</button>
            </div>
            <PiedListe aide="Clic : ses articles ; double-clic : la retirer du panier.">
              <span><b>{vue.panier.length}</b>{' commande(s) au panier'}</span>
            </PiedListe>
          </>
        ) : a === null ? (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Selectionnez une commande dans le panier pour choisir ses articles.
          </div>
        ) : (
          <div style={{ padding: '0 var(--rig-esp-4) var(--rig-esp-4)', overflowY: 'auto' }}>
            <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>
              Commande {a.numCommande} - {a.designation}
            </div>

            <div className="rig-criteres__paire">
              <label className={`rig-saisie-champ ${a.kBisPossible ? '' : 'rig-saisie-champ--refuse'}`}>
                <span className="rig-saisie-champ__libelle">KBIS</span>
                {nombre('NbKBis', a.nbKBis, !a.kBisPossible)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Histo</span>
                {nombre('NbHistorique', a.nbHistorique)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">DAS</span>
                {nombre('NbDAS', a.nbDAS)}
              </label>
            </div>
            {!a.kBisPossible && (
              <div className="rig-saisie-champ__refus">KBIS interdit : cette ligne n est pas inscrite au registre.</div>
            )}

            <div className="rig-criteres__paire">
              <label className={`rig-saisie-champ ${a.cnfPossible ? '' : 'rig-saisie-champ--refuse'}`}>
                <span className="rig-saisie-champ__libelle">Certif. Non Faillite</span>
                {nombre('NbCertifNonFaillite', a.nbCertifNonFaillite, !a.cnfPossible)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date CNF</span>
                {date('DateCnf', a.dateCnf)}
              </label>
            </div>
            {!a.cnfPossible && (
              <div className="rig-saisie-champ__refus">
                Certificat de non faillite bloque : procedure collective en cours ou dossier radie.
              </div>
            )}

            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Certif. Non RCS</span>
                {nombre('NbCertifNonRcs', a.nbCertifNonRcs)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date CN RCS</span>
                {date('DateCnRcs', a.dateCnRcs)}
              </label>
            </div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Certif. Non RSAC</span>
                {nombre('NbCertifNonRsac', a.nbCertifNonRsac)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date CN RSAC</span>
                {date('DateCnRsac', a.dateCnRsac)}
              </label>
            </div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Certif. Non Opposition</span>
                {nombre('NbCertifNonOppo', a.nbCertifNonOppo)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date CN Opposition</span>
                {date('DateCnOppo', a.dateCnOppo)}
              </label>
            </div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">DJ Copie</span>
                {nombre('NbDjCopie', a.nbDjCopie)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">DJ Expedition</span>
                {nombre('NbDjExpedition', a.nbDjExpedition)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Navire</span>
                {nombre('NbNavire', a.nbNavire)}
              </label>
            </div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">BE Interne</span>
                {nombre('NbBeInterne', a.nbBeInterne)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">BE Externe</span>
                {nombre('NbBeExterne', a.nbBeExterne)}
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Nom de la SCI</span>
                <Saisie className="rig-saisie-champ__saisie" quand="change" valeur={a.nomSci} changer={article('NomSci')} />
              </label>
            </div>

            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Nom du requi</span>
                <Saisie className="rig-saisie-champ__saisie" quand="change" valeur={a.nomRequi} changer={article('NomRequi')} />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date de travail</span>
                {date('DateTravail', a.dateTravail)}
              </label>
            </div>
            {a.travailFrais ? (
              <div className="rig-kicker">
                Travail frais : {a.ageTravailJours} jour(s), limite {a.joursFraicheur} jours.
              </div>
            ) : (
              <div className="rig-saisie-champ__refus">
                Travail perime : {a.ageTravailJours} jour(s), au-dela des {a.joursFraicheur} jours. Pieces a refaire.
              </div>
            )}

            <div className="rig-criteres__bloc">
              <div className="rig-kicker">Inscriptions</div>
              <div className="rig-inscriptions">
                <button className={bascule(a.toutCoche)} onClick={() => geste('ToutCocher')}>
                  <span className="rig-bascule__temoin"></span><span>Tout cocher</span>
                </button>
                {a.cases.map((cas) => (
                  <button key={cas.libelle} className={bascule(cas.cochee)} title={cas.titre}
                          onClick={() => geste('BasculerInscription', cas.libelle)}>
                    <span className="rig-bascule__temoin"></span><span>{cas.libelle}</span>
                  </button>
                ))}
              </div>
              {a.cases.some((cas) => cas.cochee) && (
                <div className="rig-criteres__paire">
                  {a.cases.filter((cas) => cas.cochee).map((cas) => (
                    <label key={cas.libelle} className="rig-saisie-champ">
                      <span className="rig-saisie-champ__libelle">Qte {cas.libelle}</span>
                      <Saisie className="rig-saisie-champ__saisie" type="number" min="0" quand="change"
                              valeur={String(cas.qte)}
                              changer={(saisie) => geste('ChangerQteInscription', { libelle: cas.libelle, saisie })} />
                    </label>
                  ))}
                </div>
              )}
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Qte inscriptions (total)</span>
                <Saisie className="rig-saisie-champ__saisie" type="number" min="0" readOnly quand="change"
                        valeur={String(a.nbInscr)} changer={article('NbInscr')} />
              </label>
            </div>
          </div>
        )}
      </ListeModerne>
    </div>
  )
}
