// LA SAISIE GUIDEE de la visionneuse, en ACCORDEON (proposition 6 « En-tete en
// bas » du /draft du 19/11/2026, conv-2022). Chaque etape est une ligne
// (pastille, nom, defaut, compte) qui se deplie, plusieurs a la fois, dans une
// seule colonne qui defile a la verticale : toute la hauteur du haut va aux
// etapes. Le PIED porte ce que portait l'en-tete -- titre de la piece, oeil,
// croix -- et le controle du dossier : ce qui bloque, puis les champs de
// l'etape Controle (etat du dossier, mention) a cote de « Valider le dossier »,
// toujours visibles ; Controle n'est plus une ligne de la liste (19/11/2026).
// Seules les etapes ouvertes portent leurs champs (le C# ne
// calcule pas les autres). Les etapes et leurs champs sont calques sur les
// tables reelles du dossier RCS de RIG v2 ; leurs regles (visibilite, refus,
// requis, controle final) sont dans Pont/PlanTravail.cs, la vue de chaque champ
// arrive calculee.

import { Fragment, useEffect, useRef, type MutableRefObject } from 'react'
import { Liste } from '../accueil/Liste'
import { Saisie } from '../session'
import { allerEtape, suivreEtapeVisible } from './pagination'
import type { ChampSaisieVue, DefilementVue, EtapesVue, GestePlan, PageEtapeVue, PasEtapeVue, RecapVue } from './types'

export interface ProprietesEtapes {
  etapes: EtapesVue
  /** Avance quand le C# demande de rebrancher le suivi de l'etape visible (affaire ouverte). */
  pagination: number
  /** Le dernier defilement demande par le C# : joue UNE fois, apres le rendu de la zone. */
  defilement: DefilementVue | null
  /** Le numero du dernier defilement joue : tenu par l'application, il survit a la fermeture des etapes. */
  defilementJoue: MutableRefObject<number>
  plan: GestePlan
}

export function Etapes({ etapes, pagination, defilement, defilementJoue, plan }: ProprietesEtapes) {
  const zone = useRef<HTMLDivElement>(null)
  const planCourant = useRef(plan)
  useEffect(() => { planCourant.current = plan }, [plan])

  // Ouvrir une piece, suivre un defaut ou deplier une etape amene SA ligne en
  // vue : le C# l'a deja ouverte dans la vue qu'on vient de rendre.
  const numero = defilement?.numero ?? 0
  useEffect(() => {
    if (!defilement || defilement.numero <= defilementJoue.current) return
    defilementJoue.current = defilement.numero
    allerEtape(zone.current, defilement.index, defilement.doux)
  }, [numero]) // eslint-disable-line react-hooks/exhaustive-deps

  // L'observateur de defilement : branche quand la zone apparait, rebranche
  // quand une autre affaire s'ouvre.
  useEffect(() => {
    const z = zone.current
    if (!z) return
    return suivreEtapeVisible(z, (index) => planCourant.current('etapeVisible', index))
  }, [pagination])

  return (
    <div className="rig-vged__guide rig-vged__donnees rig-accordeon">
      <div className="rig-accordeon__etapes" ref={zone}>
        {etapes.pages.map((page, index) => {
          const pas = etapes.pas[index]
          return pas
            ? <Etape key={index} pas={pas} page={page} index={index} plan={plan} />
            : null
        })}
      </div>{' '}
      <Pied etapes={etapes} plan={plan} />
    </div>
  )
}

/** Une etape de l'accordeon : sa ligne, puis ses champs quand elle est depliee. */
function Etape({ pas, page, index, plan }: { pas: PasEtapeVue, page: PageEtapeVue, index: number, plan: GestePlan }) {
  const classes = 'rig-accordeon__etape rig-accordeon__etape--' + pas.etat
    + (page.ouverte ? ' rig-accordeon__etape--ouverte' : '')
    + (pas.courant ? ' rig-accordeon__etape--courante' : '')
  return (
    <section className={classes} id={'rig-etape-' + index} data-etape={index}>
      <button className="rig-accordeon__ligne" title={pas.titre} aria-expanded={page.ouverte}
              onClick={() => plan('basculerEtape', index)}>
        <span className="rig-accordeon__pastille">{pas.pastille}</span>{' '}
        <span className="rig-accordeon__nom">{pas.titre}</span>{' '}
        <span className="rig-accordeon__resume">
          {pas.fiches > 0 && <span className="rig-accordeon__fiches">{pas.fiches}{' '}{pas.unite}{'s'}</span>}
          {pas.refuses > 0
            ? <span className="rig-accordeon__defaut">{pas.refuses}{' valeur(s) refusee(s)'}</span>
            : pas.manquants > 0
              ? <span className="rig-accordeon__requis">{pas.manquants}{' requis manquant(s)'}</span>
              : page.ouverte ? <span className="rig-accordeon__source">{page.source}</span> : null}
        </span>{' '}
        <span className="rig-accordeon__compte">{pas.remplis}{' / '}{pas.total}</span>
        <svg className="rig-accordeon__chevron" viewBox="0 0 12 12" aria-hidden="true"><path d="M4.5 3 7.5 6 4.5 9"></path></svg>
      </button>
      {page.ouverte && <Corps page={page} plan={plan} />}
    </section>
  )
}

function Corps({ page, plan }: { page: PageEtapeVue, plan: GestePlan }) {
  const fiches = page.fiches
  return (
    <div className="rig-accordeon__corps">
      {/* Une etape multiple presente ses fiches en onglets. */}
      {fiches && (
        <div className="rig-fiches">
          {Array.from({ length: fiches.nombre }, (_, indexFiche) => (
            <button key={indexFiche} className={'rig-fiches__onglet ' + (indexFiche === fiches.actif ? 'rig-fiches__onglet--actif' : '')}
                    onClick={() => plan('choisirFiche', { etape: page.code, index: indexFiche })}>
              {fiches.unite}{' '}{indexFiche + 1}
            </button>
          ))}
          <button className="rig-fiches__ajout" title="Ajouter"
                  onClick={() => plan('ajouterFiche', page.code)}>+ Ajouter</button>
          {fiches.nombre > 1 && (
            <button className="rig-fiches__retrait" title="Retirer cette fiche"
                    onClick={() => plan('supprimerFiche', page.code)}>Retirer</button>
          )}
        </div>
      )}

      <Champs code={page.code} fiche={fiches?.actif ?? 0} champs={page.champs} plan={plan} />
    </div>
  )
}

/**
 * LE PIED : l'en-tete descendu en bas. Titre de la piece, bilan du controle
 * (ce qui bloque), les champs de l'etape Controle -- mention, etat du dossier
 * --, « Valider le dossier » (« Rouvrir la saisie » quand le dossier est
 * valide), oeil et croix. Son filet haut dit l'etat d'un coup d'oeil, de la
 * couleur de la pastille du bilan : alerte (brique en clair, jaune en sombre)
 * tant qu'un point bloque, vert des que plus rien ne bloque. L'ordre du
 * balisage est celui de la tabulation (bilan, champs, Valider, outils) ; la
 * grille du CSS place le reste : outils en haut a droite, et Valider sur la
 * ligne du bilan en colonne etroite, au bout des champs en colonne large.
 */
function Pied({ etapes, plan }: { etapes: EtapesVue, plan: GestePlan }) {
  const { recap, courante, total } = etapes
  const etat = recap.valide ? 'valide' : recap.nombre > 0 ? 'bloque' : 'pret'
  const n = recap.nombre
  return (
    <div className={'rig-guide__controle rig-guide__controle--' + etat}>
      <div className="rig-guide__titres">
        <div className="rig-kicker">{'Controle du dossier · etape '}{courante + 1}{' / '}{total}</div>{' '}
        <h3 className="rig-guide__titre">{etapes.titre}</h3>
      </div>{' '}
      <Bilan recap={recap} total={total} plan={plan} />{' '}
      <div className="rig-guide__validation">
        <Champs code="CONTROLE" fiche={0} champs={etapes.controle} plan={plan} />
      </div>{' '}
      {/* Valider n'est possible que sans aucun defaut -- l'etat du dossier
          « Conforme » compris -- et le C# le reverifie. */}
      {recap.valide
        ? <button className="rig-bouton rig-guide__valider" onClick={() => plan('rouvrir')}>Rouvrir la saisie</button>
        : <button className="rig-bouton rig-bouton--fort rig-guide__valider"
                  disabled={n > 0}
                  onClick={() => plan('valider')}>Valider le dossier</button>}{' '}
      <div className="rig-guide__outils">
        {/* Oeil : rouvre le panneau de droite (document) depuis le pied des etapes. */}
        {etapes.oeil && (
          <button className="rig-bouton rig-bouton--icone rig-bouton--plat"
                  title="Afficher le panneau de droite" aria-label="Afficher le panneau de droite"
                  onClick={() => plan('afficherDocument')}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"></path><circle cx="12" cy="12" r="3"></circle></svg>
          </button>
        )}
        <button className="rig-guide__fermer" title="Fermer les etapes"
                onClick={() => plan('fermerEtapes')}>{'×'}</button>
      </div>
    </div>
  )
}

/** Le bilan du controle final, dans le pied. Tant qu'un point bloque dans la
 *  LISTE, son texte mene au premier : l'etape se deplie, le champ dit son
 *  motif. Un point du controle lui-meme est deja sous les yeux, dans le pied. */
function Bilan({ recap, total, plan }: { recap: RecapVue, total: number, plan: GestePlan }) {
  if (recap.valide) {
    return (
      <div className="rig-bilan rig-bilan--valide">
        <span className="rig-recap__pastille rig-recap__pastille--ok">OK</span>{' '}
        <span className="rig-bilan__texte"><span className="rig-bilan__titre">Dossier valide</span>{' '}
          <span className="rig-bilan__detail">{' Les etapes et le controle sont complets et acceptes. '}</span></span>
      </div>
    )
  }
  const n = recap.nombre
  const premier = recap.groupes.find((g) => g.indexEtape < total)
  const texte = (
    <>
      <span className="rig-bilan__titre">{recap.titre}</span>{' '}
      {/* Deux morceaux insecables : en colonne etroite, la ligne se coupe au
          « · », jamais au milieu de « 1 valeur(s) refusee(s) ». */}
      <span className="rig-bilan__detail">
        <span>{recap.aManquer}{' a renseigner'}</span>{' · '}<span>{recap.refusees}{' valeur(s) refusee(s)'}</span>
      </span>
    </>
  )
  return (
    <div className="rig-bilan">
      <span className={'rig-recap__pastille ' + (n === 0 ? 'rig-recap__pastille--ok' : 'rig-recap__pastille--bloque')}>
        {n === 0 ? 'OK' : String(n)}
      </span>{' '}
      {premier
        ? <button className="rig-bilan__texte rig-bilan__texte--lien" title={'Aller a ' + premier.etape}
                  onClick={() => plan('allerAuDefaut', { etape: premier.indexEtape, fiche: premier.fiche })}>{texte}</button>
        : <span className="rig-bilan__texte">{texte}</span>}
    </div>
  )
}

/**
 * Les champs d'une etape, source UNIQUE (ChampsSaisie du .razor). Chaque
 * frappe porte la valeur sur l'apercu et les fenetres detachees (« saisie »,
 * EcrireEnDirect) ; la sortie du champ l'enregistre et la journalise
 * (« ecrire », Ecrire). Un champ CACHE par sa condition n'arrive pas ici.
 */
function Champs({ code, fiche, champs, plan }: { code: string, fiche: number, champs: ChampSaisieVue[], plan: GestePlan }) {
  return (
    <div className="rig-champs">
      {champs.map((c) => {
        const etatChamp = c.refus !== null ? 'rig-saisie-champ--refuse' : c.manque ? 'rig-saisie-champ--manque' : ''
        const ecrire = (valeur: string) => plan('ecrire', { etape: code, colonne: c.colonne, valeur })
        // La cle porte la fiche : passer au dirigeant suivant RECREE les champs
        // (jamais le texte d'une fiche affiche dans la suivante).
        return (
          <Fragment key={fiche + ':' + c.colonne}>
            <label className={'rig-saisie-champ rig-saisie-champ--' + c.largeur + ' ' + etatChamp}>
              <span className="rig-saisie-champ__libelle">
                {c.libelle}
                {c.obligatoire && <span className="rig-saisie-champ__requis">requis</span>}
              </span>
              {c.choix.length > 0
                ? (
                  <Liste className="rig-saisie-champ__saisie" valeur={c.valeur} changer={ecrire}>
                    <option value=""></option>
                    {c.choix.map((choix) => <option key={choix} value={choix}>{choix}</option>)}
                  </Liste>
                )
                : (
                  <Saisie className="rig-saisie-champ__saisie" type={c.type} placeholder={c.exemple}
                          valeur={c.valeur}
                          changer={(valeur) => plan('saisie', { etape: code, colonne: c.colonne, valeur })}
                          finir={ecrire} />
                )}
              {c.refus !== null
                ? <span className="rig-saisie-champ__refus">{c.refus}</span>
                : c.manque ? <span className="rig-saisie-champ__manque">a renseigner</span> : null}
              <span className="rig-saisie-champ__colonne">{c.colonne}</span>
            </label>
          </Fragment>
        )
      })}
    </div>
  )
}
