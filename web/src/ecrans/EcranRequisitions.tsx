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

import { useEffect, useRef, type KeyboardEvent } from 'react'
import { EnteteTriable } from '../composants/EnteteTriable'
import { appeler } from '../pont'
import { Saisie, useSession, type PontDeSession } from '../session'
import type { ContexteDossierVue } from '../plan/types'

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

export interface ProprietesEcranRequisitions {
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

export function EcranRequisitions({ dossier = null }: ProprietesEcranRequisitions) {
  const [vue, geste] = useSession(pont)

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
    // Entree dans le bloc des criteres = clic sur Rechercher : le geste de RIG v2.
    if (e.key === 'Enter') rechercher()
  }

  /** Un champ nombre de la commande en cours (@bind d'un int : remonte au « change »). */
  const nombre = (champ: string, valeur: number, desactive = false) => (
    <Saisie className="rig-saisie-champ__saisie" type="number" min="0" disabled={desactive}
            quand="change" valeur={String(valeur)} changer={article(champ)} />
  )
  const date = (champ: string, valeur: string) => (
    <Saisie className="rig-saisie-champ__saisie" type="date" quand="change" valeur={valeur} changer={article(champ)} />
  )

  return (
    <div className="rig-proc__corps rig-proc__corps--cmdweb">

      {/* Zone HAUTE : les commandes passees sur le web et arrivees par EDI.
          C est le raccordement demande par la fiche 275 : choisir une commande
          remplit les criteres et lance la recherche pour rapprocher un dossier. */}
      <section className="rig-cmdweb">
        <div className="rig-cmdweb__entete">
          <div className="rig-kicker">Commandes web a traiter ({vue.nbATraiter})</div>
          <button className={bascule(vue.masquerTraitees)} onClick={() => geste('MasquerTraitees')}>
            <span className="rig-bascule__temoin"></span><span>Masquer les traitees</span>
          </button>
        </div>
        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                {colonnesCommandesWeb.map(([libelle, cle, classe]) => (
                  <EnteteTriable key={cle} libelle={libelle} cle={cle} {...(classe ? { classe } : {})} cleActive={vue.cleTri}
                                 ascendant={vue.triAscendant} trier={(c) => geste('BasculerTri', c)} />
                ))}
              </tr>
            </thead>
            <tbody>
              {vue.commandesWeb.map((web) => (
                <tr key={web.rang} className={web.classe}
                    title="Double-cliquez pour rapprocher cette commande d un dossier."
                    onClick={() => geste('ChoisirCommandeWeb', web.rang)}
                    onDoubleClick={() => geste('RapprocherCommandeWeb', web.rang)}>
                  <td>{web.origine}</td>
                  <td className="rig-table__mono">{web.numero}</td>
                  <td className="rig-table__mono">{web.recue}</td>
                  <td>{web.demandeur}</td>
                  <td>{web.famille}</td>
                  <td className="rig-table__centre rig-table__mono">{web.codeEdi}</td>
                  <td>{web.societe}</td>
                  <td className="rig-table__mono">{web.siren}</td>
                  <td className="rig-table__centre">{web.etat}</td>
                </tr>
              ))}
              {vue.commandesWeb.length === 0 && (
                <tr><td colSpan={9} className="rig-table__vide">Aucune commande web en attente.</td></tr>
              )}
            </tbody>
          </table>
        </div>
        <div className="rig-criteres__actions" style={{ padding: 'var(--rig-esp-2) var(--rig-esp-4)' }}>
          <button className="rig-bouton" disabled={!vue.commandeWebChoisie}
                  onClick={() => geste('MarquerTraitee')}>Marquer traitee</button>
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

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet ${vue.volet === 'resultats' ? 'rig-onglet--actif' : ''}`}
                    onClick={() => geste('Volet', 'resultats')}>Resultats</button>
            <button className={`rig-onglet ${vue.volet === 'panier' ? 'rig-onglet--actif' : ''}`}
                    onClick={() => geste('Volet', 'panier')}>Panier ({vue.nbPanier})</button>
            <button className={`rig-onglet ${vue.volet === 'articles' ? 'rig-onglet--actif' : ''}`}
                    onClick={() => geste('Volet', 'articles')}>Articles commandes</button>
          </div>
          <span className="rig-resultats-proc__compte">{vue.compteur}</span>
        </div>

        {vue.message !== '' && (
          <div className="rig-nonmaq__texte" style={{ padding: '0 var(--rig-esp-4) var(--rig-esp-3)' }}>{vue.message}</div>
        )}

        {vue.volet === 'resultats' ? (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table">
                <thead>
                  <tr>
                    <th>Id</th><th>Siren</th><th>Gestion</th><th>Designation</th>
                    <th>Type Adresse</th><th>Adresse</th><th className="rig-table__centre">Type</th>
                    <th className="rig-table__centre">Non Inscrit</th>
                    <th className="rig-table__centre">Nb Inscriptions</th>
                  </tr>
                </thead>
                <tbody>
                  {vue.trouvees.map((soc) => (
                    <tr key={soc.rang} className={soc.radie ? 'rig-table__ligne--inerte' : ''}
                        title={soc.infobulle}
                        onDoubleClick={() => geste('AjouterAuPanier', soc.rang)}>
                      <td className="rig-table__mono">{soc.id}</td>
                      <td className="rig-table__mono">{soc.siren}</td>
                      <td className="rig-table__mono">{soc.gestion}</td>
                      <td>{soc.designation}</td>
                      <td>{soc.typeAdresse}</td>
                      <td>{soc.adresse}</td>
                      <td className="rig-table__centre">{soc.type}</td>
                      <td className="rig-table__centre">{soc.nonInscrit ? 'oui' : ''}</td>
                      <td className="rig-table__centre">{soc.nbInscriptions}</td>
                    </tr>
                  ))}
                  {vue.trouvees.length === 0 && (
                    <tr><td colSpan={9} className="rig-table__vide">
                      {vue.aRecherche ? 'Aucune societe ne repond a ces criteres.' : 'Saisissez un critere puis lancez la recherche.'}
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
          </>
        ) : vue.volet === 'panier' ? (
          <>
            <div className="rig-table-cadre">
              <table className="rig-table">
                <thead>
                  <tr>
                    <th>N</th><th>Siren</th><th>Gestion</th><th>Designation</th>
                    <th>Adresse</th><th>Articles commandes</th>
                  </tr>
                </thead>
                <tbody>
                  {vue.panier.map((cmd) => (
                    <tr key={cmd.rang} className={cmd.enCours ? 'rig-table__ligne--alerte' : ''}
                        onClick={() => geste('Selectionner', cmd.rang)}
                        onDoubleClick={() => geste('Supprimer', cmd.rang)}>
                      <td className="rig-table__mono">{cmd.numCommande}</td>
                      <td className="rig-table__mono">{cmd.siren}</td>
                      <td className="rig-table__mono">{cmd.gestion}</td>
                      <td>{cmd.designation}</td>
                      <td>{cmd.adresse}</td>
                      <td>{cmd.resume}</td>
                    </tr>
                  ))}
                  {vue.panier.length === 0 && (
                    <tr><td colSpan={6} className="rig-table__vide">
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
      </section>
    </div>
  )
}
