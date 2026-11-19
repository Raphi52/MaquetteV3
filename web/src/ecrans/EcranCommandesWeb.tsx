// Ecran PROC_COMMANDES_WEB — « Traitement des commandes » de NeoRIG, porte dans
// la maquette pour le voir avec ses themes.
//
// MISE EN PAGE « COLONNES DE STATUT » : direction n° 4 choisie par l'utilisateur
// au /draft du 19/11/2026 (conv-1998), avec l'effet de l'accueil. L'en-tete est
// une PROUE (date en petites capitales, le compte « à traiter » en grand, une
// phrase pour les autres colonnes) ; chaque etat est une COLONNE coiffee d'une
// tuile chiffree de l'accueil (grand rond, anneau qui se remplit de sa part des
// commandes) et porte SES cartes ; la commande choisie s'ouvre en panneau a
// droite. GLISSER une carte vers « Mise en attente », « Validée » ou « Rejetée »
// joue la decision du pied (geste « Deposer », regle dans le pont).
// Remplace la « Boite de tri » (conv-1984), dont onglets et liste a plat
// deviennent les colonnes.
//
// COMMANDE EN « ESCALIER » : direction choisie au /draft du 19/11/2026 (conv-1993).
// Chaque carte dit ses marches faites (« 2/3 »). Le detail descend en trois MARCHES
// reliees par un trait — dossier, choix (types ou certificats), edition — avec
// leur voyant ; une marche faite se replie en une ligne, celle qui manque reste
// ouverte, et Valider est le bout du trait (« Valider 2 / 3 »). Les marches sont
// calculees par le pont, sur la regle meme qui ferme Valider.
//
// Contenu repris de D:\RIG-V3 (lu sans y toucher) :
//   ListeCommandesWebNode.cs   « Nouvelle commande », ordre par type,
//                              pastille « Rapprochement direct ».
//   DetailCommandeWebNode.cs   zones selon le type (ZonesCommandeWebRegle),
//                              edition repliee et decisions en pied.
// Les libelles sont ceux de Libelles.resx ; les regles sont dans
// Metier/TraitementCommandes.cs, controlees par tests/CommandesWebCheck.
// Aucune base n'est branchee : un jeu neuf a chaque ouverture.
//
// Porte depuis Components/Ecrans/EcranCommandesWeb.razor (lot 2, 18/11/2026) :
// meme balisage, memes classes CSS. ECRAN A SESSION (src/session.tsx) : l'etat
// et les regles de l'ancien @code vivent dans Pont/EcranCommandesWeb.cs
// (SessionCommandesWeb) ; cet ecran affiche la vue et remonte chaque geste.

import { Fragment, useState, type CSSProperties, type DragEvent, type KeyboardEvent, type ReactNode } from 'react'
import { appeler } from '../pont'
import { Saisie, useSession, type Geste, type PontDeSession } from '../session'

export interface OptionCw { valeur: string; libelle: string }
export interface MessageCw { texte: string; severite: string }
export interface RechercheCw {
  famille: string
  familles: OptionCw[]
  du: string
  au: string
  siren: string
  reference: string
  enAttenteEnvoi: boolean
  dateTravail: string
}
export interface LigneCw {
  id: number
  rang: number
  active: boolean
  nom: string
  precisions: string
  type: string
  direct: boolean
  avancement: string | null
  prete: boolean
  deplacable: boolean
}
export interface ColonneCw {
  etat: string
  libelle: string
  nombre: number
  actif: boolean
  part: number
  detail: string
  depot: string | null
  lignes: LigneCw[]
}
export interface ChiffreCw { nombre: number; texte: string; alerte: boolean }
export interface ProueCw { date: string; titre: ChiffreCw; phrase: ChiffreCw[] }
export interface EtatCw { icone: string; libelle: string }
export interface MembreCw { id: number; titre: string; icone: string; libelle: string }
export interface EnveloppeCw { reference: string; membres: MembreCw[]; etat: string; envoiEnAttente: boolean }
export interface ChampCw { libelle: string; valeur: string }
export interface DocumentCw { document: string; bac: string; exemplaires: string; origine: string }
export interface SaisieCw { famille: string; familles: OptionCw[]; denomination: string; siren: string; requerant: string }
export interface TraiteeCw { rejetee: boolean; motif: string; reference: string; envoyes: DocumentCw[] }
export interface LigneEntiteCw { nature: string; adresse: string; nbInscriptions: number }
export interface EntiteCw {
  cle: string
  nonInscrit: boolean
  gestion: string
  designation: string
  siren: string
  type: string
  resume: string
  radie: boolean
  retenue: boolean
  lignes: LigneEntiteCw[]
  ecarts: string[]
}
export interface RetenueCw { code: string; designation: string; radie: boolean; detail: string; ecarts: string[] }
export interface RapprochementCw {
  debiteur: boolean
  faillite: boolean
  aRetenue: boolean
  deplie: boolean
  traitee: boolean
  retenue: RetenueCw | null
  rechSiren: string
  rechRaison: string
  tousDebiteurs: boolean
  tousEtablissements: boolean
  message: string | null
  entites: EntiteCw[]
  resumeRapprochement: string
}
export interface ItemTypeCw { regroupement: boolean; code: string; libelle: string; enfant: boolean; coche: boolean }
export interface TypesCw { dateTravail: string; toutCoche: boolean; items: ItemTypeCw[]; quantite: number }
export interface SousTypeCw { valeur: string; libelle: string; inerte: boolean; coche: boolean }
export interface ControleCw { derniereFormalite: string; delai: string; seuil: number; issue: string; motif: string | null }
export interface NonOppositionCw {
  sousTypes: SousTypeCw[]
  quantite: number
  quantiteActive: boolean
  certificatDOpposition: boolean
  controle: ControleCw | null
}
export interface CertificatCw { valeur: string; libelle: string; quantite: number; actif: boolean; bloque: boolean; motif: string | null }
export interface CertificatsCw { dateTravail: string; lignes: CertificatCw[]; basculeFaillite: boolean; certificatDeFaillite: boolean }
export interface BandeCw { rejet: boolean; motifSaisi: string; confirmerRefuse: boolean; longueurMax: number }
export interface PiedCw {
  bande: BandeCw | null
  barre: string
  documents: DocumentCw[]
  envoiMail: boolean
  adresseMail: string
  attenteRefusee: boolean
  blocage: string | null
  avancement: string
}
export interface EtapeCw { nom: string; faite: boolean; resume: string; manque: string | null }
export interface PanneauCw {
  id: number
  titre: string
  rang: number
  type: string
  etat: EtatCw | null
  nom: string
  reference: string
  enveloppe: EnveloppeCw | null
  saisie: SaisieCw | null
  donneesRecues: ChampCw[]
  traitee: TraiteeCw | null
  motifAttente: string | null
  rapprochement: RapprochementCw | null
  types: TypesCw | null
  nonOpposition: NonOppositionCw | null
  certificats: CertificatsCw | null
  pied: PiedCw | null
  etapes: EtapeCw[]
  choixDeplie: boolean
}
export interface ChampAdresseCw { cle: string; libelle: string; large: boolean; valeur: string }
export interface CoordonneesCw { lectureSeule: boolean; champs: ChampAdresseCw[]; lignes: string[]; alertes: string[]; adresseCorrigee: boolean }
export interface VueCommandesWeb {
  session: string
  proue: ProueCw
  colonnes: ColonneCw[]
  rechercheOuverte: boolean
  rechercheAffinee: boolean
  resumeCriteres: string
  recherche: RechercheCw
  message: MessageCw | null
  directsEnTete: boolean
  panneau: PanneauCw | null
  coordonnees: CoordonneesCw | null
}

const pont: PontDeSession<VueCommandesWeb> = {
  ouvrir: () => appeler<VueCommandesWeb>('EcranCommandesWeb.Ouvrir'),
  agir: (session, geste, valeur) => appeler<VueCommandesWeb>('EcranCommandesWeb.Agir', session, geste, valeur),
  fermer: (session) => appeler<void>('EcranCommandesWeb.Fermer', session),
}

const InfobulleTypes = "Reçus dans la commande\nCommande web : seuls les types commandés sont affichés, pré-cochés. "
  + "Un regroupement (VN, ST…) inclut les types qu'il contient, affichés grisés. La quantité vaut pour chaque type coché."

/** Le compteur « − n + » ; « Un clic met 1, un autre remet 0 » au clic sur la valeur. */
function Compteur({ cible, valeur, actif, geste }: { cible: string; valeur: number; actif: boolean; geste: Geste }) {
  const poser = (sens: string) => geste('Compteur', { cible, sens })
  return (
    <span className={`rig-tcw__compteur-qte ${actif ? '' : 'rig-tcw__compteur-qte--inerte'}`}>
      <button className="rig-tcw__qte-bouton" title="Diminuer" aria-label="Diminuer" disabled={!actif || valeur <= 0}
              onClick={() => poser('moins')}>−</button>
      <button className="rig-tcw__qte-valeur" title="Un clic met 1, un autre remet 0" disabled={!actif}
              onClick={() => poser('valeur')}>{actif ? valeur : 0}</button>
      <button className="rig-tcw__qte-bouton" title="Augmenter" aria-label="Augmenter" disabled={!actif || valeur >= 99}
              onClick={() => poser('plus')}>+</button>
    </span>
  )
}

function Documents({ documents, titreLigne }: { documents: DocumentCw[]; titreLigne?: string }) {
  return (
    <table className="rig-table rig-tcw__table">
      <thead><tr><th>Document</th><th>Bac</th><th>Ex.</th><th>Origine de la règle</th></tr></thead>
      <tbody>
        {documents.map((d, i) => (
          <tr key={i} title={titreLigne}>
            <td>{d.document}</td><td>{d.bac}</td><td>{d.exemplaires}</td><td>{d.origine}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function ZoneRapprochement({ r, geste }: { r: RapprochementCw; geste: Geste }) {
  const resume = r.aRetenue && !r.deplie
  const texte = (champ: string) => (valeur: string) => geste('Texte', { champ, valeur })
  return (
    <section className="rig-tcw__zone">
      <div className="rig-tcw__zone-tete">
        <span className="rig-tcw__section">{r.debiteur ? 'Débiteur — rapprochement' : 'Dossier concerné'}</span>
        {!r.debiteur && !resume && (
          <span className="rig-tcw__discret">{r.faillite
            ? 'Résolution directe par le n° de gestion reçu : aucune recherche à faire dans le cas nominal.'
            : 'Un seul dossier attendu. Si la recherche ne ramène rien, la création du non-inscrit se fait dans RIG V2.'}</span>
        )}
        {r.aRetenue && !r.traitee && (
          <>
            <span className="rig-tcw__espace"></span>
            <button className="rig-tcw__lien" onClick={() => geste('Basculer', 'RapprochementDeplie')}>{resume ? 'Changer de dossier' : 'Replier'}</button>
          </>
        )}
      </div>
      {resume && r.retenue ? (
        <>
          {/* Le dossier retenu en UNE ligne (maquette « Boite de tri ») : son
              n° de gestion, son nom, la pastille, et l'adresse du siege. */}
          <div className="rig-tcw__retenue">
            <span className="rig-tcw__retenue-code">{r.retenue.code}</span>
            <b>{r.retenue.designation}</b>
            <span className="rig-tcw__pastille rig-tcw__pastille--retenue">RETENUE</span>
            {r.retenue.radie && <span className="rig-tcw__pastille rig-tcw__pastille--radie">RADIÉ</span>}
            <span className="rig-tcw__retenue-detail">{r.retenue.detail}</span>
          </div>
          {r.retenue.ecarts.map((ecart, i) => (
            <div key={i} className="rig-tcw__ligne-statut rig-tcw__ligne-statut--attente">≠ {ecart}</div>
          ))}
        </>
      ) : (
        <>
          <div className="rig-tcw__rech-debiteur">
            <Saisie className="rig-saisie-champ__saisie" aria-label="SIREN" placeholder="SIREN"
                    valeur={r.rechSiren} changer={texte('RechSiren')} />
            <Saisie className="rig-saisie-champ__saisie" aria-label="Raison sociale" placeholder="Raison sociale"
                    valeur={r.rechRaison} changer={texte('RechRaison')} />
            <button className="rig-bouton rig-tcw__bouton" onClick={() => geste('RelancerRapprochement')}>Rechercher</button>
            {r.debiteur && (
              <>
                <button className={`rig-bascule ${r.tousDebiteurs ? 'rig-bascule--actif' : ''}`} onClick={() => geste('Basculer', 'TousDebiteurs')}>
                  <span className="rig-bascule__temoin"></span><span>Tous les débiteurs</span>
                </button>
                <button className={`rig-bascule ${r.tousEtablissements ? 'rig-bascule--actif' : ''}`} onClick={() => geste('Basculer', 'TousEtablissements')}>
                  <span className="rig-bascule__temoin"></span><span>Tous les établissements</span>
                </button>
              </>
            )}
          </div>
          {r.message !== null ? (
            <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--info">{r.message}</div>
          ) : !r.debiteur && !r.aRetenue && r.entites.length > 0 ? (
            <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--info">{r.entites.length === 1
              ? 'Sélectionnez le dossier concerné pour rattacher le certificat.'
              : 'Plusieurs entités répondent alors qu\'un seul dossier est attendu : désignez la bonne.'}</div>
          ) : null}
          {r.entites.length > 0 && (
            <>
              <div className="rig-table-cadre rig-tcw__cadre">
                <table className="rig-table rig-tcw__rappro">
                  <thead>
                    <tr><th>Dossier</th><th>Nature</th><th>Désignation</th><th>Siren</th><th>Adresse</th><th>Type</th><th>Nb ins</th><th></th></tr>
                  </thead>
                  <tbody>
                    {r.entites.map((e) => [
                      <tr key={`b:${e.cle}`} className={`rig-tcw__bloc ${e.retenue ? 'rig-tcw__bloc--retenu' : ''}`}
                          onDoubleClick={() => geste('Retenir', e.cle)}>
                        {/* Cellule en texte courant : les blancs entre pastille, nom et
                            resume sont ceux que le .razor gardait (ni avant ni entre
                            les pastilles conditionnelles). */}
                        <td colSpan={7}>
                          <span className="rig-tcw__pastille rig-tcw__pastille--accent">{e.nonInscrit ? 'NON-INSCRIT' : 'DOSSIER RCS'}</span>{' '}
                          <b>{e.designation}</b>{' '}
                          <span className="rig-tcw__discret">{e.resume}</span>
                          {e.radie && <span className="rig-tcw__pastille rig-tcw__pastille--radie">RADIÉ</span>}
                          {e.retenue && <span className="rig-tcw__pastille rig-tcw__pastille--retenue">RETENUE</span>}
                        </td>
                        <td className="rig-tcw__cellule-action">
                          {!e.retenue && <button className="rig-tcw__lien" onClick={() => geste('Retenir', e.cle)}>Retenir</button>}
                        </td>
                      </tr>,
                      ...e.lignes.map((l, i) => (
                        <tr key={`l:${e.cle}:${i}`} className={e.retenue ? 'rig-tcw__ligne--retenue' : ''}
                            onDoubleClick={() => geste('Retenir', e.cle)}>
                          <td className="rig-table__mono">{e.gestion.length > 0 ? e.gestion : 'NI'}</td>
                          <td><span className="rig-tcw__nature">{l.nature}</span></td>
                          <td>{e.designation}</td>
                          <td className="rig-table__mono">{e.siren}</td>
                          <td title={l.adresse}>{l.adresse}</td>
                          <td>{e.type}</td>
                          <td className="rig-table__centre">{l.nbInscriptions}</td>
                          <td></td>
                        </tr>
                      )),
                      ...e.ecarts.map((ecart, i) => (
                        <tr key={`e:${e.cle}:${i}`} className="rig-tcw__ecart"><td colSpan={8} title="Voir ce qui ne correspond pas à la commande">≠ {ecart}</td></tr>
                      )),
                    ])}
                  </tbody>
                </table>
              </div>
              <div className="rig-tcw__resume">{r.resumeRapprochement}</div>
            </>
          )}
        </>
      )}
    </section>
  )
}

function ZoneTypes({ t, geste }: { t: TypesCw; geste: Geste }) {
  return (
    <section className="rig-tcw__zone"
             title={InfobulleTypes}>
      <div className="rig-tcw__zone-tete">
        <span className="rig-tcw__section">Types d'inscription commandés</span>
      </div>
      <div className="rig-tcw__discret">Travail {t.dateTravail} · Fraîcheur {t.dateTravail}</div>
      {t.items.length === 0 ? (
        <div className="rig-tcw__discret">Aucun type d'inscription n'a été reçu avec cette commande.</div>
      ) : (
        <>
          <label className="rig-tcw__case rig-tcw__case--tout">
            <input type="checkbox" checked={t.toutCoche} onChange={(e) => geste('CocherTous', e.currentTarget.checked)} />
            <span>Tout cocher</span>
          </label>
          {t.items.map((k) => k.regroupement ? (
            <div key={`g:${k.code}`} className="rig-tcw__regroupement">{k.code} — {k.libelle}</div>
          ) : (
            <label key={k.code} className={`rig-tcw__case ${k.enfant ? 'rig-tcw__case--enfant' : ''}`}>
              <input type="checkbox" checked={k.coche}
                     onChange={(e) => geste('CocherType', { code: k.code, coche: e.currentTarget.checked })} />
              <span className="rig-tcw__case-code">{k.code}</span><span>{k.libelle}</span>
            </label>
          ))}
          <div className="rig-tcw__quantite">
            <span>Qté</span>
            <Compteur cible="QuantiteInscriptions" valeur={t.quantite} actif={true} geste={geste} />
          </div>
        </>
      )}
    </section>
  )
}

function ZoneNonOpposition({ n, geste }: { n: NonOppositionCw; geste: Geste }) {
  const controle = n.controle
  return (
    <>
      <section className="rig-tcw__zone">
        <div className="rig-tcw__zone-tete"><span className="rig-tcw__section">Type de certificat de non-opposition</span></div>
        <div className="rig-tcw__discret">Coché d'après le sous-type reçu dans le flux ; les autres s'ajoutent à la main. CNO-IP reste affiché mais non sélectionnable : il relève du processus IP suivi.</div>
        {n.sousTypes.map((st) => (
          <label key={st.valeur} className={`rig-tcw__case ${st.inerte ? 'rig-tcw__case--inerte' : ''}`}>
            <input type="checkbox" disabled={st.inerte} checked={st.coche}
                   onChange={(e) => geste('SousType', { valeur: st.valeur, coche: e.currentTarget.checked })} />
            <span>{st.libelle}</span>
          </label>
        ))}
        <div className="rig-tcw__quantite">
          <span>Qté</span>
          <Compteur cible="QuantiteCno" valeur={n.quantite} actif={n.quantiteActive} geste={geste} />
        </div>
        <label className="rig-tcw__case" title="À cocher si une opposition a été formée : le certificat d'opposition remplace alors les certificats de non-opposition commandés.">
          <input type="checkbox" checked={n.certificatDOpposition} onChange={(e) => geste('CertificatDOpposition', e.currentTarget.checked)} />
          <span>Délivrer le certificat d'opposition</span>
        </label>
      </section>

      <section className="rig-tcw__zone">
        <div className="rig-tcw__zone-tete"><span className="rig-tcw__section">Contrôle de délivrance</span></div>
        {controle === null ? (
          <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--attente">Cochez un type de certificat pour lancer le contrôle de délivrance.</div>
        ) : (
          <>
            <div className="rig-tcw__mesures">
              <div><span className="rig-tcw__libelle-fiche">Dernière formalité</span><b>{controle.derniereFormalite}</b></div>
              <div><span className="rig-tcw__libelle-fiche">Délai</span><b>{controle.delai}</b></div>
            </div>
            <div className="rig-tcw__discret">Le certificat se délivre lorsque {controle.seuil} jours se sont écoulés depuis la dernière formalité enregistrée sur l'entreprise.</div>
            {controle.issue === 'Delivrance' ? (
              <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--ok">Délivrance possible : le délai est dépassé.</div>
            ) : controle.issue === 'Reserve' ? (
              <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--attente">{controle.motif} Délivrance possible malgré cette réserve. Vous pouvez aussi mettre la commande en attente.</div>
            ) : (
              <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--rejet">{controle.motif}</div>
            )}
          </>
        )}
      </section>
    </>
  )
}

function ZoneCertificats({ c, geste }: { c: CertificatsCw; geste: Geste }) {
  return (
    <section className="rig-tcw__zone">
      <div className="rig-tcw__zone-tete">
        <span className="rig-tcw__section">Certificats</span>
        <span className="rig-tcw__discret">Travail {c.dateTravail}</span>
      </div>
      {c.lignes.map((cert) => (
        <div key={cert.valeur} className={`rig-tcw__certificat ${cert.bloque ? 'rig-tcw__certificat--bloque' : ''}`}>
          <span className="rig-tcw__certificat-nom">{cert.libelle}</span>
          <Compteur cible={cert.valeur} valeur={cert.quantite} actif={cert.actif} geste={geste} />
          {cert.motif !== null && <span className="rig-tcw__motif">{cert.motif}</span>}
        </div>
      ))}
      {c.basculeFaillite && (
        <label className="rig-tcw__case" title="La non-faillite ne peut pas être certifiée : cochez pour délivrer le certificat de faillite à sa place.">
          <input type="checkbox" checked={c.certificatDeFaillite} onChange={(e) => geste('CertificatDeFaillite', e.currentTarget.checked)} />
          <span>Délivrer le certificat de faillite</span>
        </label>
      )}
    </section>
  )
}

/** Une marche de l'escalier : son voyant (coche quand elle est faite, anneau sinon)
 *  dans la gouttiere ou passe le trait, et son contenu a droite. */
function Marche({ e, children }: { e: EtapeCw; children: ReactNode }) {
  return (
    <div className={`rig-tcw__marche ${e.faite ? 'rig-tcw__marche--faite' : 'rig-tcw__marche--a-faire'}`}>
      <span className="rig-tcw__voyant" role="img" aria-label={e.faite ? `${e.nom} : fait` : `${e.nom} : à faire`}
            title={e.manque ?? e.nom}>
        {e.faite && <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>}
      </span>
      <div className="rig-tcw__marche-corps">{children}</div>
    </div>
  )
}

function Panneau({ p, geste }: { p: PanneauCw; geste: Geste }) {
  const texte = (champ: string) => (valeur: string) => geste('Texte', { champ, valeur })
  const pied = p.pied
  const [dossier, choix, edition] = p.etapes
  return (
    <section className="rig-tcw__panneau" aria-label={p.titre}>
      <div className="rig-tcw__panneau-haut">
        <div className="rig-tcw__panneau-tete">
          <span className={`rig-tcw__type rig-tcw__rang-${p.rang}`}>{p.type}</span>
          {p.etat && (
            <span className="rig-tcw__etat"><span className={`rig-tcw__icone rig-tcw__icone--${p.etat.icone}`}></span>{p.etat.libelle}</span>
          )}
          <span className="rig-tcw__espace"></span>
          <button className="rig-tcw__fermer rig-tcw__fermer--rond" title="Fermer" aria-label="Fermer" onClick={() => geste('FermerPanneau')}>×</button>
        </div>
        <div className="rig-tcw__titre">{p.nom}</div>
        <div className="rig-tcw__reference">{p.reference}</div>
        <div className="rig-tcw__documents">
          <button className="rig-tcw__doc" title="Consulter ou corriger l'adresse d'envoi de cette commande"
                  onClick={() => geste('OuvrirCoordonnees')}>Coordonnées du destinataire</button>
          <button className="rig-tcw__doc" onClick={() => geste('Consulter', 'Kbis')}>Kbis</button>
          <button className="rig-tcw__doc" onClick={() => geste('Consulter', 'EtatSynthese')}>État de synthèse</button>
          <button className="rig-tcw__doc" onClick={() => geste('Consulter', 'KbisHorsRessort')}>Kbis hors ressort</button>
          <button className="rig-tcw__doc" onClick={() => geste('Consulter', 'SyntheseRne')}>Synthèse RNE</button>
        </div>
        {p.enveloppe && (
          <div className="rig-tcw__enveloppe">
            <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5.5" width="18" height="13" rx="2" /><path d="M3.5 7l8.5 6 8.5-6" /></svg>
            {/* Un blanc apres « — avec » et un avant « · » : dans le .razor, ils
                finissaient et ouvraient un texte NON vide, que Razor garde (il ne
                retire que les blancs SEULS autour du @foreach, donc rien entre deux
                membres). Vu a la comparaison des captures du 19/11/2026 (IG-90812). */}
            <span className="rig-tcw__enveloppe-texte">
              <b>Enveloppe · {p.enveloppe.reference}</b> — avec{' '}{p.enveloppe.membres.map((mb) => (
                <button key={mb.id} className="rig-tcw__membre" title={mb.titre} onClick={() => geste('Choisir', mb.id)}>
                  <span className={`rig-tcw__icone rig-tcw__icone--${mb.icone}`}></span>{mb.libelle}
                </button>
              ))}{' '}· <span className="rig-tcw__discret">{p.enveloppe.etat}</span>
            </span>
            {p.enveloppe.envoiEnAttente && (
              <button className="rig-tcw__lien" onClick={() => geste('EnvoyerSansAttendre')}>Envoyer sans attendre</button>
            )}
          </div>
        )}
      </div>

      <div className="rig-tcw__detail">
        {p.saisie ? (
          <section className="rig-tcw__zone">
            <div className="rig-tcw__zone-tete">
              <span className="rig-tcw__section">Informations saisies</span>
            </div>
            <div className="rig-tcw__saisie">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Type de commande</span>
                <select className="rig-saisie-champ__saisie" value={p.saisie.famille}
                        onChange={(e) => geste('ChoisirTypeSaisi', e.currentTarget.value)}>
                  {p.saisie.familles.map((f) => <option key={f.valeur} value={f.valeur}>{f.libelle}</option>)}
                </select>
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Raison sociale</span>
                <Saisie className="rig-saisie-champ__saisie" valeur={p.saisie.denomination} changer={texte('Denomination')} />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">SIREN</span>
                <Saisie className="rig-saisie-champ__saisie" valeur={p.saisie.siren} changer={texte('Siren')} />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Nom du requérant</span>
                <Saisie className="rig-saisie-champ__saisie" valeur={p.saisie.requerant} changer={texte('Requerant')} />
              </label>
            </div>
            <div className="rig-tcw__actions-saisie">
              <button className="rig-bouton" onClick={() => geste('AnnulerSaisie')}>Annuler la commande</button>
              <button className="rig-bouton rig-bouton--fort" onClick={() => geste('EnregistrerSaisie')}>Enregistrer la saisie</button>
            </div>
          </section>
        ) : (
          <details className="rig-tcw__zone rig-tcw__recues">
            <summary className="rig-tcw__section">Données reçues</summary>
            <dl className="rig-tcw__champs">
              {p.donneesRecues.map((d, i) => [<dt key={`t${i}`}>{d.libelle}</dt>, <dd key={`d${i}`}>{d.valeur}</dd>])}
            </dl>
          </details>
        )}

        {p.traitee ? (
          <section className="rig-tcw__zone">
            <div className="rig-tcw__zone-tete"><span className="rig-tcw__section">Ce qui a été envoyé</span></div>
            {p.traitee.rejetee ? (
              <>
                <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--rejet">Motif du rejet : {p.traitee.motif}</div>
                <div className="rig-tcw__discret">Aucun document n'a été édité pour cette demande.</div>
              </>
            ) : p.traitee.envoyes.length === 0 ? (
              <div className="rig-tcw__discret">Aucun document n'a été édité pour cette demande.</div>
            ) : (
              <>
                <div className="rig-tcw__discret">Demande n° {p.traitee.reference} · {p.traitee.envoyes.length} document(s) envoyé(s)</div>
                <Documents documents={p.traitee.envoyes} />
              </>
            )}
            <div className="rig-tcw__discret">Commande traitée : consultation seule, aucune action possible.</div>
          </section>
        ) : !p.saisie && (
          <>
            {p.motifAttente !== null && (
              <div className="rig-tcw__ligne-statut rig-tcw__ligne-statut--attente">Motif de la mise en attente : {p.motifAttente}</div>
            )}
            {pied && dossier && choix && edition && (
              <div className="rig-tcw__escalier">
                <Marche e={dossier}>
                  {p.rapprochement && <ZoneRapprochement r={p.rapprochement} geste={geste} />}
                </Marche>
                <Marche e={choix}>
                  {choix.faite && !p.choixDeplie ? (
                    <div className="rig-tcw__marche-ligne">
                      <span className="rig-tcw__section">{choix.nom}</span>
                      <span className="rig-tcw__marche-resume">{choix.resume}</span>
                      <button className="rig-tcw__lien" onClick={() => geste('Basculer', 'ChoixDeplie')}>Modifier</button>
                    </div>
                  ) : (
                    <>
                      {p.types && <ZoneTypes t={p.types} geste={geste} />}
                      {p.nonOpposition && <ZoneNonOpposition n={p.nonOpposition} geste={geste} />}
                      {p.certificats && <ZoneCertificats c={p.certificats} geste={geste} />}
                      {choix.faite && (
                        <button className="rig-tcw__lien rig-tcw__marche-replier" onClick={() => geste('Basculer', 'ChoixDeplie')}>Replier</button>
                      )}
                    </>
                  )}
                </Marche>
                <Marche e={edition}>
                  <details className="rig-tcw__edition">
                    <summary title="Édition & impression">{pied.barre}</summary>
                    <Documents documents={pied.documents}
                               titreLigne="Voir le document tel que la validation l'éditera (rien n'est imprimé). Double-clic sur la ligne : même effet." />
                    <div className="rig-tcw__discret">Aucun paramètre d'impression ici : agrafage, coffre, envoi par mail et choix de l'imprimante restent dans le Tableau des éditions.</div>
                  </details>
                </Marche>
              </div>
            )}
          </>
        )}
      </div>

      {pied && (
        <footer className="rig-tcw__pied">
          {pied.bande && (
            <div className={`rig-tcw__bande ${pied.bande.rejet ? 'rig-tcw__bande--rejet' : 'rig-tcw__bande--attente'}`}>
              <label className="rig-tcw__bande-champ">
                <span>{pied.bande.rejet ? 'Motif du rejet' : 'Motif de la mise en attente (facultatif)'}</span>
                <Saisie className="rig-saisie-champ__saisie" maxLength={pied.bande.longueurMax}
                        placeholder="Saisir le motif…" valeur={pied.bande.motifSaisi} changer={texte('MotifSaisi')} />
              </label>
              <button className={`rig-bouton rig-tcw__bouton ${pied.bande.rejet ? 'rig-tcw__bouton--danger' : 'rig-bouton--fort'}`}
                      disabled={pied.bande.confirmerRefuse}
                      {...(pied.bande.rejet ? { title: 'Obligatoire pour un rejet' } : {})}
                      onClick={() => geste('Decider', pied.bande!.rejet ? 'Rejeter' : 'MettreEnAttente')}>{pied.bande.rejet ? 'Confirmer le rejet' : 'Confirmer la mise en attente'}</button>
              <button className="rig-bouton rig-tcw__bouton" onClick={() => geste('FermerBande')}>Annuler</button>
            </div>
          )}
          <div className="rig-tcw__pied-ligne">
            {/* Le bout du trait de l'escalier : anneau tant que Valider est ferme. */}
            <span className={`rig-tcw__voyant rig-tcw__voyant--fin ${pied.blocage === null ? 'rig-tcw__voyant--pret' : ''}`}
                  aria-hidden="true"></span>
            <div className="rig-tcw__mail">
              <button className={`rig-bascule ${pied.envoiMail ? 'rig-bascule--actif' : ''}`}
                      title="Les documents sont imprimés comme d'habitude, et envoyés en plus par mail une fois générés, avec le modèle de mail du greffe. Plusieurs adresses se séparent par « ; »."
                      onClick={() => geste('Basculer', 'EnvoiMail')}>
                <span className="rig-bascule__temoin"></span><span>Envoyer aussi par mail</span>
              </button>
              {pied.envoiMail && (
                <Saisie className="rig-saisie-champ__saisie rig-tcw__mail-adresse" title="Adresse du destinataire"
                        aria-label="Adresse du destinataire" valeur={pied.adresseMail} changer={texte('AdresseMail')} />
              )}
            </div>

            <div className="rig-tcw__decisions">
              <button className="rig-bouton rig-tcw__bouton rig-tcw__rejeter"
                      onClick={() => geste('OuvrirBande', 'Rejeter')}>Rejeter…</button>
              <button className="rig-bouton rig-tcw__bouton"
                      disabled={pied.attenteRefusee}
                      onClick={() => geste('OuvrirBande', 'MettreEnAttente')}>Mettre en attente…</button>
              <button className="rig-bouton rig-bouton--fort rig-tcw__valider" disabled={pied.blocage !== null}
                      {...(pied.blocage !== null ? { title: pied.blocage } : {})} onClick={() => geste('Decider', 'Valider')}>Valider<b className="rig-tcw__valider-compte">{pied.avancement}</b></button>
            </div>
          </div>
          {pied.blocage !== null && <div className="rig-tcw__blocage">{pied.blocage}</div>}
        </footer>
      )}
    </section>
  )
}

function Coordonnees({ d, geste }: { d: CoordonneesCw; geste: Geste }) {
  return (
    <div className="rig-voile" onClick={() => geste('FermerCoordonnees')}>
      <div className="rig-tcw__dialogue" role="dialog" aria-label="Coordonnées du destinataire" onClick={(e) => e.stopPropagation()}>
        <div className="rig-tcw__dialogue-titre">Coordonnées du destinataire</div>
        {d.lectureSeule && (
          <div className="rig-tcw__discret">Commande déjà traitée : l'adresse se consulte sans se modifier.</div>
        )}
        <div className="rig-tcw__dialogue-corps">
          <div className="rig-tcw__adresse">
            {d.champs.map((ch) => (
              <label key={ch.cle} className={`rig-saisie-champ ${ch.large ? 'rig-saisie-champ--pleine' : ''}`}>
                <span className="rig-saisie-champ__libelle">{ch.libelle}</span>
                <Saisie className="rig-saisie-champ__saisie" disabled={d.lectureSeule}
                        valeur={ch.valeur} changer={(valeur) => geste('Adresse', { cle: ch.cle, valeur })} />
              </label>
            ))}
          </div>
          <div className="rig-tcw__enveloppe-apercu">
            <span className="rig-tcw__libelle-fiche">Aperçu de l'enveloppe</span>
            <div className="rig-tcw__fenetre-enveloppe">
              {d.lignes.map((l, i) => <div key={i}>{l}</div>)}
            </div>
          </div>
        </div>
        {d.alertes.map((a, i) => (
          <div key={i} className="rig-tcw__ligne-statut rig-tcw__ligne-statut--attente">{a}</div>
        ))}
        {d.adresseCorrigee && (
          <div className="rig-tcw__discret">Adresse corrigée : c'est elle qui sera imprimée sur les documents.</div>
        )}
        <div className="rig-tcw__dialogue-pied">
          {!d.lectureSeule && d.adresseCorrigee && (
            <button className="rig-bouton" onClick={() => geste('RevenirAdresseRecue')}>Revenir à l'adresse reçue</button>
          )}
          {!d.lectureSeule && (
            <button className="rig-bouton rig-bouton--fort" onClick={() => geste('EnregistrerAdresse')}>Enregistrer l'adresse</button>
          )}
          <button className="rig-bouton" onClick={() => geste('FermerCoordonnees')}>Fermer</button>
        </div>
      </div>
    </div>
  )
}

// ------------------------------------------------------------ colonnes de statut

/** La teinte de tuile de chaque colonne (jetons --rig-chiffre-N de la charte, comme les tuiles de l'accueil). */
const TeinteColonne: Record<string, number> = { ATraiter: 1, EnAttente: 4, Validee: 2, Rejetee: 3, SaisieManuelle: 4 }

const trait = { fill: 'none', stroke: 'currentColor', strokeWidth: 1.6, strokeLinecap: 'round', strokeLinejoin: 'round' } as const

/** Le sujet de chaque colonne, dans le grand rond de sa tuile (dessins de l'accueil pour la corbeille, la coche et l'horloge). */
const IconeColonne: Record<string, ReactNode> = {
  ATraiter: <svg viewBox="0 0 16 16"><path d="M2.5 9.5h3l1 2h3l1-2h3M3.5 4h9l1 5.5V13h-11V9.5z" {...trait} /></svg>,
  EnAttente: <svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.6" {...trait} /><path d="M8 4.8V8l2.2 1.6" {...trait} /></svg>,
  Validee: <svg viewBox="0 0 16 16"><path d="m3.5 8.5 3 3 6-6.5" {...trait} strokeWidth={1.8} /></svg>,
  Rejetee: <svg viewBox="0 0 16 16"><path d="M4.5 4.5l7 7M11.5 4.5l-7 7" {...trait} strokeWidth={1.8} /></svg>,
  SaisieManuelle: <svg viewBox="0 0 16 16"><path d="M10.5 2.8l2.7 2.7-7.4 7.4H3.1v-2.7z" {...trait} /></svg>,
}

/** La zone ou poser une carte, selon la decision de la colonne (ColonneCw.Depot). */
const Depots: Record<string, { classe: string; libelle: string }> = {
  Valider: { classe: 'valider', libelle: 'Déposer ici pour valider' },
  MettreEnAttente: { classe: 'attente', libelle: 'Déposer ici pour mettre en attente' },
  Rejeter: { classe: 'rejeter', libelle: 'Déposer ici pour rejeter' },
}

/** La carte en cours de glisser : son identifiant et sa colonne de depart. */
interface Traine { id: number; etat: string }

function Carte({ cmd, rang, etat, traine, setTraine, geste }: {
  cmd: LigneCw; rang: number; etat: string; traine: Traine | null; setTraine: (t: Traine | null) => void; geste: Geste
}) {
  const commencer = (e: DragEvent) => {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', String(cmd.id))
    setTraine({ id: cmd.id, etat })
  }
  return (
    <button type="button"
            className={`rig-tcw__carte rig-tcw__rang-${cmd.rang} ${cmd.active ? 'rig-tcw__carte--active' : ''} ${traine?.id === cmd.id ? 'rig-tcw__carte--traine' : ''}`}
            style={{ '--tcw-ordre': rang } as CSSProperties}
            aria-current={cmd.active ? 'true' : undefined} draggable={cmd.deplacable}
            {...(cmd.deplacable ? { title: 'Glisser vers une autre colonne pour décider' } : {})}
            onClick={() => geste('Choisir', cmd.id)}
            onDragStart={cmd.deplacable ? commencer : undefined} onDragEnd={() => setTraine(null)}>
      <span className="rig-tcw__carte-tete">
        <span className="rig-tcw__carte-nom">{cmd.nom}</span>
        {cmd.avancement !== null && (
          <span className={`rig-tcw__avancement ${cmd.prete ? 'rig-tcw__avancement--prete' : ''}`}
                title="Marches faites : dossier, choix, édition">{cmd.avancement}</span>
        )}
      </span>
      <span className="rig-tcw__carte-type">{cmd.type}</span>
      <span className="rig-tcw__carte-pied">
        <span className="rig-tcw__carte-precisions">{cmd.precisions}</span>
        {cmd.direct && (
          <span className="rig-tcw__direct" title="Un seul dossier correspond à la commande : à traiter en premier.">Rapprochement direct</span>
        )}
      </span>
    </button>
  )
}

function Colonne({ c, traine, setTraine, geste }: {
  c: ColonneCw; traine: Traine | null; setTraine: (t: Traine | null) => void; geste: Geste
}) {
  const [survol, setSurvol] = useState(false)
  // Une colonne accepte la carte glissee si elle porte une decision et n'est pas sa colonne de depart.
  const depot = c.depot !== null && traine !== null && traine.etat !== c.etat ? Depots[c.depot] : undefined
  const glisser = depot && traine
    ? {
        onDragOver: (e: DragEvent) => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; setSurvol(true) },
        onDragLeave: (e: DragEvent) => {
          if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setSurvol(false)
        },
        onDrop: (e: DragEvent) => {
          e.preventDefault()
          setSurvol(false)
          setTraine(null)
          geste('Deposer', { id: traine.id, action: c.depot })
        },
      }
    : {}
  return (
    <section className={`rig-tcw__colonne ${depot ? 'rig-tcw__colonne--cible' : ''} ${depot && survol ? 'rig-tcw__colonne--survol' : ''}`}
             aria-label={c.libelle} {...glisser}>
      <button type="button" aria-pressed={c.actif ? 'true' : 'false'}
              className={`rig-chiffre rig-chiffre--${TeinteColonne[c.etat] ?? 4} rig-tcw__tete ${c.actif ? 'rig-chiffre--deplie' : ''}`}
              onClick={() => geste('ChoisirOnglet', c.etat)}>
        <span className="rig-chiffre__libelle">{c.libelle}</span>
        <span className="rig-chiffre__rang">
          <span className="rig-chiffre__anneau" aria-hidden="true">
            <span className="rig-chiffre__cercle rig-chiffre__cercle--jauge" style={{ '--rig-anneau-cible': c.part } as CSSProperties}></span>
            <span className="rig-chiffre__rond">{IconeColonne[c.etat]}</span>
          </span>
          <span className="rig-chiffre__texte">
            <b className="rig-chiffre__valeur">{c.nombre}</b>
            <span className="rig-chiffre__detail">{c.detail}</span>
          </span>
        </span>
      </button>
      <div className="rig-tcw__cartes">
        {c.lignes.length === 0 && !depot && <div className="rig-tcw__vide">Aucune commande ne correspond aux critères.</div>}
        {c.lignes.map((cmd, i) => (
          <Carte key={cmd.id} cmd={cmd} rang={i} etat={c.etat} traine={traine} setTraine={setTraine} geste={geste} />
        ))}
        {depot && <div className={`rig-tcw__depot rig-tcw__depot--${depot.classe}`}>{depot.libelle}</div>}
      </div>
    </section>
  )
}

export function EcranCommandesWeb() {
  const [vue, geste] = useSession(pont)
  const [traine, setTraine] = useState<Traine | null>(null)

  // Avant la premiere reponse du pont : le cadre seul, sans aucune valeur fausse.
  if (!vue) return <div className="rig-tcw"></div>

  const r = vue.recherche
  const proue = vue.proue
  const texte = (champ: string) => (valeur: string) => geste('Texte', { champ, valeur })
  const rechercher = () => geste('Rechercher')
  const surToucheRecherche = (e: KeyboardEvent) => {
    if (e.key === 'Enter') rechercher()
  }

  return (
    <div className="rig-tcw">
      {/* La proue de l'accueil : date, compte « à traiter » en grand, phrase des autres colonnes. */}
      <header className="rig-tcw__barre">
        <div className="rig-proue rig-tcw__proue">
          <div className="rig-proue__date">{proue.date}<span className="rig-proue__point">·</span>Affaires</div>
          <h2 className="rig-tcw__proue-titre"><b>{proue.titre.nombre}</b> {proue.titre.texte}</h2>
          <div className="rig-proue__phrase">
            {proue.phrase.map((c, i) => (
              <Fragment key={i}>
                {i > 0 && <span className="rig-proue__point">·</span>}
                <b className={c.alerte ? 'rig-proue__alerte' : undefined}>{c.nombre}</b> {c.texte}
              </Fragment>
            ))}
          </div>
        </div>
        <div className="rig-tcw__outils">
          <button className={`rig-bascule ${vue.directsEnTete ? 'rig-bascule--actif' : ''}`}
                  title="Place en tête de chaque groupe les commandes qui se rapprochent d'un seul dossier."
                  onClick={() => geste('BasculerDirects')}>
            <span className="rig-bascule__temoin"></span><span>Rapprochements directs en tête</span>
          </button>
          <button className="rig-bouton rig-tcw__bouton rig-tcw__nouvelle" onClick={() => geste('NouvelleCommande')}>+&nbsp;&nbsp;Nouvelle commande saisie au guichet</button>
          <button className={`rig-tcw__chercher ${vue.rechercheOuverte ? 'rig-tcw__chercher--ouvert' : ''} ${vue.rechercheAffinee ? 'rig-tcw__chercher--affinee' : ''}`}
                  title={vue.resumeCriteres} aria-expanded={vue.rechercheOuverte ? 'true' : 'false'}
                  onClick={() => geste('Basculer', 'RechercheOuverte')}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6" /><path d="M20 20l-4.5-4.5" /></svg>
            <span>Rechercher</span>
          </button>
        </div>
      </header>

      {vue.rechercheOuverte && (
        <section className="rig-tcw__recherche" onKeyDown={surToucheRecherche}>
          <div className="rig-tcw__criteres">
            <label className="rig-saisie-champ rig-tcw__champ">
              <span className="rig-saisie-champ__libelle">Type</span>
              <select className="rig-saisie-champ__saisie" value={r.famille} onChange={(e) => geste('Famille', e.currentTarget.value)}>
                <option value="">Toutes</option>
                {r.familles.map((f) => <option key={f.valeur} value={f.valeur}>{f.libelle}</option>)}
              </select>
            </label>

            <div className="rig-saisie-champ rig-tcw__champ">
              <span className="rig-saisie-champ__libelle">Période du</span>
              <div className="rig-tcw__periode">
                <Saisie type="date" className="rig-saisie-champ__saisie" aria-label="Période du" quand="change"
                        valeur={r.du} changer={(v) => geste('Du', v)} />
                <span className="rig-tcw__discret">au</span>
                <Saisie type="date" className="rig-saisie-champ__saisie" aria-label="au" quand="change"
                        valeur={r.au} changer={(v) => geste('Au', v)} />
              </div>
            </div>

            <label className="rig-saisie-champ rig-tcw__champ rig-tcw__champ--court">
              <span className="rig-saisie-champ__libelle">SIREN</span>
              <Saisie className="rig-saisie-champ__saisie" placeholder="123456789" valeur={r.siren} changer={texte('SirenCherche')} />
            </label>

            <label className="rig-saisie-champ rig-tcw__champ rig-tcw__champ--court">
              <span className="rig-saisie-champ__libelle">N° commande</span>
              <Saisie className="rig-saisie-champ__saisie" placeholder="IG-12345" valeur={r.reference} changer={texte('ReferenceCherchee')} />
            </label>

            <button className={`rig-bascule ${r.enAttenteEnvoi ? 'rig-bascule--actif' : ''} rig-tcw__bascule-recherche`}
                    onClick={() => geste('Basculer', 'EnAttenteEnvoi')}>
              <span className="rig-bascule__temoin"></span><span>Validées en attente d'envoi</span>
            </button>

            <button className="rig-bouton rig-bouton--fort" onClick={rechercher}>Rechercher</button>
          </div>

          <div className="rig-tcw__date">
            <span className="rig-tcw__libelle-fiche">Date de travail</span>
            <b className="rig-tcw__date-valeur">{r.dateTravail}</b>
          </div>
        </section>
      )}

      {vue.message && (
        <div className={`rig-tcw__message rig-tcw__message--${vue.message.severite}`} role="status">
          <span>{vue.message.texte}</span>
          <button className="rig-tcw__fermer" title="Fermer" aria-label="Fermer" onClick={() => geste('FermerMessage')}>×</button>
        </div>
      )}

      {/* Les colonnes a gauche, la commande ouverte a droite. La place du panneau
          reste prise sans commande (l'invite) : choisir une carte n'elargit ni ne
          resserre les colonnes. */}
      <div className="rig-tcw__scene">
        <div className="rig-tcw__colonnes" aria-label="Commandes par statut">
          {vue.colonnes.map((c) => <Colonne key={c.etat} c={c} traine={traine} setTraine={setTraine} geste={geste} />)}
        </div>

        {/* key : chaque commande ouverte est un panneau NEUF, qui glisse a
            nouveau et repart en haut de son defilement. */}
        {vue.panneau ? <Panneau key={vue.panneau.id} p={vue.panneau} geste={geste} /> : (
          <div className="rig-tcw__invite">
            <span>Sélectionnez une commande dans la liste pour la traiter.</span>
            <span className="rig-tcw__discret">Ou glissez sa carte vers « Mise en attente », « Validée » ou « Rejetée ».</span>
          </div>
        )}
      </div>

      {vue.coordonnees && <Coordonnees d={vue.coordonnees} geste={geste} />}
    </div>
  )
}
