// LES CORPS DES TUILES de l'accueil : Performance, A traiter, A traiter BIS,
// Mes taches du jour, Nouveautes, Actualites, Activite des utilisateurs et
// les huit widgets METIER (un par categorie de la feuille « Alertes accueil
// v2 Rig »), plus le panneau de reglages d'une tuile.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis les
// RenderFragment Corps* et ReglagesDe de Home.razor : meme balisage, memes
// classes, memes morceaux de texte (lus dans le code que Razor generait).
// Toutes les valeurs viennent de SessionAccueil (Pont/Accueil.cs) ; chaque
// geste lui remonte par `geste`.

import { Fragment } from 'react'
import { IconeNouveaute } from '../composants/IconeNouveaute'
import { Saisie } from '../session'
import { Liste } from './Liste'
import {
  arreter, type ActiviteTuileVue, type ActualitesTuileVue, type AlerteLigneVue, type AlertesBisVue,
  type AlertesTuileVue, type GesteAccueil, type GroupeAlertesVue, type MesureVue, type MetierVue,
  type NouveauteVue, type PanneauDemandesVue, type ReglagesVue,
} from './types'

const MAQUETTE = "Maquette : ce geste n'est pas encore branché"

/** Le compte d'une alerte : le COMBIEN, colore par son palier d'importance. */
function Compte({ a }: { a: AlerteLigneVue }) {
  return <span className={'rig-alerte__compte rig-alerte__compte--' + a.palier}>{a.nombre}</span>
}

// --- Performance : activite des DOSSIERS (traites, en cours, hors delai...).
export function Performance({ mesures }: { mesures: MesureVue[] }) {
  return (
    <div className="rig-perf">
      {mesures.map((m) => (
        <div key={m.libelle} className={'rig-perf__mesure rig-perf__mesure--' + m.statut}>
          <div className="rig-perf__entete">
            <span className="rig-perf__libelle" title={m.libelle}>{m.libelle}</span>
            {' '}
            <span className="rig-perf__chiffres">
              <b className="rig-perf__valeur">{m.valeur}</b>
              {' '}
              <span className="rig-perf__objectif-texte">{m.objectifTexte}</span>
            </span>
          </div>
          {' '}
          <div className="rig-perf__barre" role="img"
               aria-label={m.libelle + ' : ' + m.valeur + ', ' + m.objectifTexte + ', objectif atteint a ' + m.atteinte + ' %'}>
            <span style={{ width: m.part + '%', background: 'var(--rig-grad-' + m.statut + ')' }}></span>
            {' '}
            <i className="rig-perf__repere" style={{ left: m.objectif + '%' }}></i>
          </div>
        </div>
      ))}
    </div>
  )
}

/** Le titre repliable d'une categorie d'alertes (« A traiter » et « A traiter BIS »). */
function TeteGroupe({ g, geste, arret }: { g: GroupeAlertesVue, geste: GesteAccueil, arret: boolean }) {
  return (
    <button className="rig-kicker rig-groupe-tete" type="button"
            title={(g.repliee ? 'Deplier' : 'Replier') + ' la categorie ' + g.categorie}
            aria-expanded={g.repliee ? 'false' : 'true'}
            onClick={() => geste('repli', g.categorie)}
            onMouseDown={arret ? arreter : undefined}>
      <span className={'rig-groupe-tete__chevron ' + (g.repliee ? 'rig-groupe-tete__chevron--replie' : '')}>{'›'}</span>
      {' '}
      <span>{g.categorie}</span>
      {' '}
      {/* 17/09/2026 : le compte de la categorie n'est plus le nombre de LIGNES
          mais la CHARGE -- la somme des elements a traiter de ses lignes. */}
      <span className="rig-groupe-tete__compte">{g.charge}</span>
    </button>
  )
}

// --- « A traiter » : le corps ne montre QUE les alertes surveillees ; les
// interrupteurs vivent dans les reglages de la tuile.
export function Alertes({ vue, geste }: { vue: AlertesTuileVue, geste: GesteAccueil }) {
  return (
    <div>
      {vue.vide && <div className="rig-vide">Aucune alerte a afficher. Ouvrir les reglages de la tuile pour elargir.</div>}
      {vue.groupes.map((g) => (
        <div key={g.categorie} className="rig-alerte-groupe">
          <TeteGroupe g={g} geste={geste} arret={false} />
          {g.lignes.map((a) => (
            <button key={a.id} className="rig-alerte rig-alerte--lien" type="button"
                    title={'Aller vers ' + a.libelleCible}
                    onClick={() => geste('alerte', a.id)}>
              {/* 17/09/2026 : le COMBIEN remplace la barre de couleur ; le
                  niveau d'importance colore le chiffre. */}
              <Compte a={a} />
              {' '}
              <span className="rig-alerte__corps">
                <span className="rig-alerte__titre">{a.titre}</span>
                {vue.afficherDetail && (
                  <>
                    <br />
                    {' '}
                    <span className="rig-alerte__detail">{a.detail}</span>
                  </>
                )}
              </span>
              {' '}
              <span className="rig-alerte__vers">{a.libelleCible}{' ›'}</span>
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}

// --- « Mes taches du jour » : les constats calcules sur les dossiers. Le clic
// passe par le MEME chemin que les alertes : une ligne task driven ouvre la tache.
export function Taches({ lignes, geste }: { lignes: AlerteLigneVue[], geste: GesteAccueil }) {
  return (
    <div>
      {lignes.length === 0 && <div className="rig-vide">Aucune tache en attente sur les dossiers connus.</div>}
      {lignes.map((a) => (
        <button key={a.id} className="rig-alerte rig-alerte--lien" type="button"
                title={'Aller vers ' + a.libelleCible}
                onClick={() => geste('tache', a.id)}>
          <Compte a={a} />
          {' '}
          <span className="rig-alerte__corps">
            <span className="rig-alerte__titre">{a.titre}</span><br />
            {' '}
            <span className="rig-alerte__detail">{a.detail}</span>
          </span>
          {' '}
          <span className="rig-alerte__vers">{a.libelleCible}{' ›'}</span>
        </button>
      ))}
    </div>
  )
}

// --- Actualites : des messages REDIGES, lus dans un fichier partage ; la
// derniere ligne dit d'ou vient ce qu'on lit (a jour, ou copie hors ligne).
export function Actualites({ vue, geste }: { vue: ActualitesTuileVue, geste: GesteAccueil }) {
  return (
    <div>
      {vue.actifs.length === 0 && <div className="rig-vide">Aucune actualite en cours.</div>}
      {vue.actifs.map((b, rang) => (
        <div key={rang} className="rig-alerte">
          <span className={'rig-filet-v rig-filet-v--' + b.filet}></span>
          {' '}
          <span className="rig-alerte__corps">
            <span className={'rig-actualite__type rig-actualite__type--' + b.filet}>{b.type}</span><span className="rig-alerte__titre">{b.titre}</span><br />
            {' '}
            <span className="rig-alerte__detail">{b.message}</span><br />
            {' '}
            <span className="rig-alerte__detail">{b.auteur}{' — '}{b.horodatage}</span>
          </span>
        </div>
      ))}
      <button className="rig-alerte rig-alerte--lien" type="button"
              title="Ouvrir la redaction des actualites"
              onClick={() => geste('rediger')}>
        <span className="rig-alerte__corps">
          <span className="rig-alerte__detail">{vue.etat}</span>
        </span>
        {' '}
        <span className="rig-alerte__vers">{'Rediger ›'}</span>
      </button>
    </div>
  )
}

// --- Activite des utilisateurs : le journal des gestes de l'equipe. Le filtre
// est une question du moment, il n'est pas retenu dans accueil.json.
export function Activite({ vue, geste }: { vue: ActiviteTuileVue, geste: GesteAccueil }) {
  return (
    <div className="rig-activite">
      <div className="rig-activite__barre">
        <Liste className="rig-activite__filtre" aria-label="Filtrer par utilisateur"
               onMouseDown={arreter}
               valeur={vue.filtre} changer={(login) => geste('activite', login)}>
          <option value="">Tous les utilisateurs</option>
          {vue.agents.map((a) => <option key={a.login} value={a.login}>{a.nom}{' ('}{a.login}{')'}</option>)}
        </Liste>
        {' '}
        <span className="rig-activite__total">{vue.total}{' geste(s)'}</span>
      </div>
      {' '}
      <div className="rig-activite__comptes">
        {vue.comptes.map((c) => (
          <span key={c.agent} className="rig-activite__compte"
                title={c.agent + ' : ' + c.nombre + " geste(s) aujourd'hui"}>
            <b>{c.nombre}</b>{' '}{c.agent}
          </span>
        ))}
      </div>
      {' '}
      <ul className="rig-activite__liste">
        {vue.gestes.map((a, rang) => (
          <li key={rang} className="rig-activite__ligne">
            <span className="rig-filet-v rig-filet-v--action"></span>
            {' '}
            <span className="rig-activite__texte"><b>{a.agent}</b>{' '}{a.verbe}{' '}{a.objet}</span>
            {' '}
            <span className="rig-activite__quand">{a.depuis}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

// --- Nouveautes : une ligne par nouveaute livree ; un seul detail deplie a la fois.
export function Nouveautes({ nouveautes, geste }: { nouveautes: NouveauteVue[], geste: GesteAccueil }) {
  return (
    <div className="rig-nouv">
      {nouveautes.map((n) => (
        <Fragment key={n.module}>
          <button type="button"
                  className={'rig-nouv__ligne ' + (n.ouverte ? 'rig-nouv__ligne--ouverte' : '')}
                  aria-expanded={n.ouverte ? 'true' : 'false'}
                  title={n.ouverte ? 'Replier' : 'Voir le detail'}
                  onClick={() => geste('nouveaute', n.module)}
                  onMouseDown={arreter}>
            <span className={'rig-nouv__module rig-nouv__module--' + n.teinte}>
              <IconeNouveaute nom={n.pictogramme} classe="rig-nouv__icone" />{n.module}
            </span>
            {' '}
            <span className="rig-nouv__texte"><b>{n.action}</b>{' '}{n.suite}</span>
            {' '}
            <span className="rig-nouv__chevron">{n.ouverte ? '▾' : '▸'}</span>
          </button>
          {n.ouverte && <p className={'rig-nouv__detail rig-nouv__detail--' + n.teinte}>{n.detail}</p>}
        </Fragment>
      ))}
    </div>
  )
}

/** Le panneau de droite : les demandes de l'alerte ouverte. PARTAGE par les
 *  widgets metier et « A traiter BIS » -- un seul tableau. */
function PanneauDemandes({ idTuile, panneau, geste }: { idTuile: string, panneau: PanneauDemandesVue, geste: GesteAccueil }) {
  const ouverte = panneau.ouverte
  return (
    <>
      {' '}
      <div className="rig-metier__droite">
        {ouverte === null
          ? <div className="rig-vide">Choisir une alerte à gauche pour voir ses demandes.</div>
          : (
            <>
              <div className="rig-metier__droite-tete">
                <Compte a={ouverte} />
                {' '}
                <span className="rig-metier__droite-titre">{ouverte.titre}</span>
                {' '}
                <span className="rig-metier__droite-compte">{panneau.demandes.length}{' demande(s)'}</span>
                {' '}
                <button className="rig-metier__ouvrir" type="button"
                        title={'Aller vers ' + ouverte.libelleCible}
                        onClick={() => geste('metierAlerte', idTuile)}
                        onMouseDown={arreter}>{'Ouvrir ›'}</button>
                {' '}
                <button className="rig-metier__fermer" type="button"
                        title={panneau.titreFermer}
                        onClick={() => geste('metierReduire', idTuile)}
                        onMouseDown={arreter}>{'×'}</button>
              </div>
              {panneau.demandes.length === 0
                ? <div className="rig-vide">Aucune demande ne correspond à la recherche.</div>
                : (
                  // Les blancs entre cellules du .razor ne sont pas recopies : un
                  // tableau ne dessine pas le texte blanc entre ses lignes.
                  <table className="rig-metier__table">
                    <thead><tr><th>N° chrono</th><th>Société</th><th>SIREN</th><th>Type</th><th>Reçu le</th><th>Anc.</th><th>État</th></tr></thead>
                    <tbody>
                      {panneau.demandes.map((d) => (
                        <tr key={d.chrono}>
                          <td><button className="rig-metier__chrono" type="button"
                                      title={'Ouvrir le dossier ' + d.chrono}
                                      onClick={() => geste('numero', d.chrono)}
                                      onMouseDown={arreter}>{d.chrono}</button></td>
                          <td>{d.societe}</td>
                          <td className="rig-metier__siren">{d.siren}</td>
                          <td>{d.type}</td>
                          <td>{d.recuLe}</td>
                          <td>{d.anciennete}{' j'}</td>
                          <td>{d.etat}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
            </>
          )}
      </div>
      {' '}
    </>
  )
}

/**
 * Le corps d'un widget metier : deux panneaux, comme la maquette metier du
 * 17/09/2026. A GAUCHE la recherche, les gestes de la categorie, trois
 * compteurs et la liste des alertes ; A DROITE (deploye) les demandes de
 * l'alerte selectionnee. Les BOUTONS DE GESTE sont de la maquette : ils
 * nomment le geste sans l'executer -- desactives, pas trompeurs.
 */
export function Metier({ id, vue, geste }: { id: string, vue: MetierVue, geste: GesteAccueil }) {
  return (
    <div className={'rig-metier ' + (vue.deploye ? 'rig-metier--deploye' : '')}>
      <div className="rig-metier__gauche">
        <div className="rig-metier__tete">
          <span className="rig-metier__registre">{vue.registre}</span>
          {' '}
          <span className="rig-metier__titre">{vue.titre}</span>
          <span className={'rig-metier__charge rig-metier__charge--' + vue.palierCharge}
                title={vue.charge + ' element(s) a traiter dans cette categorie'}>{vue.charge}</span>
          {' '}
          <button className="rig-metier__deployer" type="button"
                  title={vue.deploye ? 'Replier le widget' : 'Deployer pour voir la liste en grand'}
                  aria-pressed={vue.deploye ? 'true' : 'false'}
                  onClick={() => geste('metierDeployer', id)}
                  onMouseDown={arreter}>{vue.deploye ? '⤡' : '⛶'}</button>
          {' '}
          {/* Le menu des GESTES de la categorie : l'en-tete ne peut pas porter six
              boutons, et les gestes rares n'ont pas a prendre la place des courants. */}
          <button className={'rig-metier__menu-bouton ' + (vue.menuOuvert ? 'rig-metier__menu-bouton--ouvert' : '')}
                  type="button" title="Autres gestes de ce widget"
                  aria-expanded={vue.menuOuvert ? 'true' : 'false'}
                  onClick={() => geste('metierMenu', id)}
                  onMouseDown={arreter}>{'⋮'}</button>
        </div>
        {vue.menuOuvert && (
          <div className="rig-metier__menu" onMouseDown={arreter}>
            {vue.gestes.length > 0 && (
              <button className="rig-metier__menu-ligne rig-metier__menu-ligne--phare"
                      type="button" disabled title={MAQUETTE}>
                <span className="rig-metier__menu-glyphe">+</span>{vue.gestes[0]}
              </button>
            )}
            <div className="rig-metier__menu-filet"></div>
            {' '}
            <button className="rig-metier__menu-ligne" type="button"
                    disabled={vue.titreOuvrir === null}
                    title={vue.titreOuvrir ?? "Choisir une alerte d'abord"}
                    onClick={() => geste('metierOuvrir', id)}>
              <span className="rig-metier__menu-glyphe">{'↗'}</span>{'Ouvrir le dossier\n                    '}
            </button>
            {vue.gestes.slice(1).map((g) => (
              <button key={g} className="rig-metier__menu-ligne" type="button" disabled title={MAQUETTE}>
                <span className="rig-metier__menu-glyphe">{'□'}</span>{g}
              </button>
            ))}
            <div className="rig-metier__menu-filet"></div>
            {' '}
            <button className="rig-metier__menu-ligne" type="button"
                    title="Relire les alertes et les compteurs"
                    onClick={() => geste('metierRafraichir', id)}>
              <span className="rig-metier__menu-glyphe">{'↻'}</span>{'Rafraichir les compteurs\n                    '}
            </button>
            {' '}
            <button className="rig-metier__menu-ligne" type="button"
                    title="Ouvrir les reglages de cette tuile"
                    onClick={() => geste('metierParametrer', id)}>
              <span className="rig-metier__menu-glyphe">{'⚙'}</span>{'Parametrer le widget\n                    '}
            </button>
          </div>
        )}
        <div className="rig-metier__recherche">
          <span className="rig-metier__loupe">{'\u{1F50D}'}</span>
          {' '}
          <Saisie className="rig-metier__saisie" placeholder="N° de dossier ou société"
                  valeur={vue.filtre} quand="input"
                  changer={(texte) => geste('metierFiltre', { id, texte })}
                  onMouseDown={arreter} />
          {' '}
          {/* Etiquette de la maquette : elle dit ce que la recherche CIBLE,
              elle est donc toujours lisible -- pas seulement une fois tape. */}
          <span className={'rig-metier__puce ' + (vue.filtreActif ? 'rig-metier__puce--active' : '')}>dossier ciblé</span>
        </div>
        {' '}
        <div className="rig-metier__gestes">
          {vue.gestes.map((g) => (
            <button key={g} className="rig-metier__geste" type="button" disabled title={MAQUETTE}>{g}</button>
          ))}
        </div>
        {' '}
        <div className="rig-metier__compteurs">
          <div className="rig-metier__compteur">
            <span className="rig-metier__chiffre">{vue.charge}</span>
            {' '}
            <span className="rig-metier__legende">à traiter</span>
          </div>
          {' '}
          <div className="rig-metier__compteur">
            <span className="rig-metier__chiffre">{vue.urgents}</span>
            {' '}
            <span className="rig-metier__legende">urgents</span>
          </div>
          {' '}
          <div className="rig-metier__compteur">
            <span className="rig-metier__chiffre">{vue.demat}</span>
            {' '}
            <span className="rig-metier__legende">démat.</span>
          </div>
        </div>
        {' '}
        <div className="rig-kicker">Alertes</div>
        {vue.lignes.length === 0 && <div className="rig-vide">Aucune alerte declaree dans cette categorie.</div>}
        {vue.lignes.map((a) => (
          <button key={a.id} className={'rig-metier__ligne ' + (vue.active === a.id ? 'rig-metier__ligne--active' : '')}
                  type="button"
                  title={vue.deploye ? 'Voir les demandes de : ' + a.titre : 'Agrandir le widget sur les demandes de : ' + a.titre}
                  onClick={() => geste('metierChoisir', { id, alerte: a.id })}
                  onMouseDown={arreter}>
            <Compte a={a} />
            {' '}
            <span className="rig-metier__ligne-titre">{a.titre}</span>
          </button>
        ))}
      </div>
      {vue.panneau && <PanneauDemandes idTuile={id} panneau={vue.panneau} geste={geste} />}
    </div>
  )
}

/** « A traiter BIS » : l'arbre de toutes les categories a gauche ; le clic sur
 *  une alerte la SELECTIONNE et montre ses demandes a droite. */
export function AlertesBis({ vue, geste }: { vue: AlertesBisVue, geste: GesteAccueil }) {
  return (
    <div className="rig-metier rig-metier--bis">
      <div className="rig-metier__gauche">
        {vue.vide && <div className="rig-vide">Aucune alerte a afficher. Ouvrir les reglages de « A traiter » pour elargir.</div>}
        {vue.groupes.map((g) => (
          <div key={g.categorie} className="rig-alerte-groupe">
            <TeteGroupe g={g} geste={geste} arret={true} />
            {g.lignes.map((a) => (
              <button key={a.id} className={'rig-metier__ligne ' + (vue.active === a.id ? 'rig-metier__ligne--active' : '')}
                      type="button" title={'Voir les demandes de : ' + a.titre}
                      onClick={() => geste('bisChoisir', a.id)}
                      onMouseDown={arreter}>
                <Compte a={a} />
                {' '}
                <span className="rig-metier__ligne-titre">{a.titre}</span>
              </button>
            ))}
          </div>
        ))}
      </div>
      {' '}
      <PanneauDemandes idTuile="alertes-bis" panneau={vue.panneau} geste={geste} />
    </div>
  )
}

const MARGE_HAUT = { marginBottom: 'var(--rig-esp-2)' }
const MARGE_SECTION = { margin: 'var(--rig-esp-3) 0 var(--rig-esp-2)' }

/** Les reglages d'une tuile : ils portent sur SON contenu (quelles alertes,
 *  quelles mesures). La retirer de l'accueil se fait dans l'entete. */
export function Reglages({ vue, geste }: { vue: ReglagesVue, geste: GesteAccueil }) {
  return (
    <div className="rig-reglages">
      {vue.genre === 'alertes' && (
        <>
          <div className="rig-kicker" style={MARGE_HAUT}>Categories surveillees</div>
          {vue.categories.map((c) => (
            <button key={c.id} className={'rig-bascule ' + (c.actif ? 'rig-bascule--actif' : '')}
                    title={c.titre ?? undefined}
                    onClick={() => geste('categorie', c.id)}>
              <span className="rig-bascule__temoin"></span>
              {' '}
              <span>{c.libelle}</span>
            </button>
          ))}
          <div className="rig-kicker" style={MARGE_SECTION}>Alertes surveillees</div>
          {vue.alertes.map((a) => (
            <button key={a.id} className={'rig-bascule ' + (a.actif ? 'rig-bascule--actif' : '')}
                    title={a.titre ?? undefined}
                    onClick={() => geste('surveiller', a.id)}>
              <span className="rig-bascule__temoin"></span>
              {' '}
              <span>{a.libelle}</span>
            </button>
          ))}
          <div className="rig-kicker" style={MARGE_SECTION}>Ce que la tuile affiche</div>
          {' '}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">A partir de</span>
            {' '}
            <Liste className="rig-saisie-champ__saisie" valeur={String(vue.niveauMini)}
                   changer={(v) => geste('niveau', v)}>
              <option value="0">Tout, meme ce qui va bien</option>
              <option value="1">A surveiller et au-dela</option>
              <option value="2">A traiter et au-dela</option>
              <option value="3">Hors delai seulement</option>
            </Liste>
          </label>
          {' '}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Ordre</span>
            {' '}
            <Liste className="rig-saisie-champ__saisie" valeur={vue.tri} changer={(v) => geste('tri', v)}>
              <option value="gravite">Le plus grave en tete</option>
              <option value="titre">Par libelle</option>
            </Liste>
          </label>
          {' '}
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Lignes au plus (0 = toutes)</span>
            {' '}
            <Saisie className="rig-saisie-champ__saisie" type="number" min="0" max="20"
                    valeur={String(vue.limite)} quand="change" changer={(v) => geste('limite', v)} />
          </label>
          {' '}
          <button className={'rig-bascule ' + (vue.afficherDetail ? 'rig-bascule--actif' : '')}
                  onClick={() => geste('detail')}>
            <span className="rig-bascule__temoin"></span>
            {' '}
            <span>Afficher le detail sous chaque alerte</span>
          </button>
        </>
      )}
      {vue.genre === 'performance' && (
        <>
          <div className="rig-kicker" style={MARGE_HAUT}>Mesures affichees</div>
          {vue.mesures.map((m) => (
            <button key={m.id} className={'rig-bascule ' + (m.actif ? 'rig-bascule--actif' : '')}
                    onClick={() => geste('mesure', m.id)}>
              <span className="rig-bascule__temoin"></span>
              {' '}
              <span>{m.libelle}</span>
            </button>
          ))}
        </>
      )}
      {vue.genre === 'aucun' && (
        <div className="rig-vide">{"\n                    Cette tuile n'a pas de reglage propre.\n                    Pour la retirer de l'accueil, ouvrir les reglages en haut a droite.\n                "}</div>
      )}
    </div>
  )
}
