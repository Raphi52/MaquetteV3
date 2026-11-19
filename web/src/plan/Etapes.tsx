// LA SAISIE GUIDEE de la visionneuse (Components/Pages/Home.razor, portee au
// lot 2 du plan « finir le passage a React », 19/11/2026). Les 7 etapes cote
// a cote, a l'horizontale : la molette passe a l'etape suivante (scroll-snap) ;
// seules l'etape visible et ses deux voisines sont rendues (demande du
// 2026-09-15). Les etapes et leurs champs sont calques sur les tables reelles
// du dossier RCS de RIG v2 ; leurs regles (visibilite, refus, requis, controle
// final) sont dans Pont/PlanTravail.cs, la vue de chaque champ arrive calculee.

import { Fragment, useEffect, useRef, type MutableRefObject } from 'react'
import { Liste } from '../accueil/Liste'
import { Saisie } from '../session'
import { allerEtape, montrerPasCourant, suivrePagination } from './pagination'
import type { ChampSaisieVue, DefilementVue, EtapesVue, GestePlan, PageEtapeVue, RecapVue } from './types'

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

  // Ouvrir une piece amene la frise sur SON etape ; la frise et le controle
  // final font DEFILER la page geante jusqu'a l'etape voulue.
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
    return suivrePagination(z, (index) => planCourant.current('etapeVisible', index))
  }, [pagination])

  const { courante, total } = etapes
  // La frise suit l'etape : son pas allume reste dans le champ.
  const frise = useRef<HTMLOListElement>(null)
  useEffect(() => { montrerPasCourant(frise.current) }, [courante])
  return (
    <div className="rig-vged__guide rig-vged__donnees">
      <div className="rig-guide__entete">
        <div>
          <div className="rig-kicker">{'Controle du dossier · etape '}{courante + 1}{' / '}{total}</div>{' '}
          <h3 className="rig-guide__titre">{etapes.titre}</h3>
        </div>
        {/* Oeil : rouvre le panneau de droite (document) depuis l'en-tete des etapes. */}
        {etapes.oeil && (
          <button className="rig-bouton rig-bouton--icone rig-bouton--plat" style={{ marginLeft: 'auto' }}
                  title="Afficher le panneau de droite" aria-label="Afficher le panneau de droite"
                  onClick={() => plan('afficherDocument')}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"></path><circle cx="12" cy="12" r="3"></circle></svg>
          </button>
        )}
        <button className="rig-guide__fermer" title="Fermer les etapes"
                onClick={() => plan('fermerEtapes')}>{'×'}</button>
      </div>{' '}
      <ol className="rig-frise rig-frise--h rig-frise--haut" ref={frise}>
        {/* Fleches de part et d'autre des etapes : elles remplacent les boutons
            Precedent / Suivant (demande du 2026-09-15). */}
        <li className="rig-frise__fleche">
          <button className="rig-frise__fleche-bouton" title="Etape precedente"
                  disabled={courante === 0}
                  onClick={() => plan('allerEtape', courante - 1)}>{'‹'}</button>
        </li>
        {etapes.pas.map((p, index) => (
          <li key={index} className={'rig-frise__pas rig-frise__pas--' + p.etat + ' ' + (p.courant ? 'rig-frise__pas--courant' : '')}>
            <button className="rig-frise__bouton" title={p.titre} onClick={() => plan('allerEtape', index)}>
              <span className="rig-frise__pastille">{p.pastille}</span>{' '}
              <span className="rig-frise__libelle">
                {/* Le nom dans son element : texte nu dans le libelle (flex en
                    colonne), il etait coupe net, sans « … » (19/11/2026). */}
                <span className="rig-frise__titre">{p.titre}</span>
                {p.fiches > 0 && <span className="rig-frise__fiches">{p.fiches}{' '}{p.unite}{'s'}</span>}
                {p.refuses > 0
                  ? <span className="rig-frise__defaut">{p.refuses}{' valeur(s) refusee(s)'}</span>
                  : p.manquants > 0
                    ? <span className="rig-frise__requis">{p.manquants}{' requis manquant(s)'}</span>
                    : null}
              </span>{' '}
              <span className="rig-frise__compte">{p.remplis}{' / '}{p.total}</span>
            </button>
          </li>
        ))}
        <li className="rig-frise__fleche">
          <button className="rig-frise__fleche-bouton" title="Etape suivante"
                  disabled={courante === total - 1}
                  onClick={() => plan('allerEtape', courante + 1)}>{'›'}</button>
        </li>
      </ol>{' '}
      <div className="rig-saisie__pages rig-saisie__pages--h" ref={zone}>
        {etapes.pages.map((page, indexEtape) => (
          <section key={indexEtape} className={'rig-saisie__page ' + (page.courante ? 'rig-saisie__page--courante' : '')}
                   id={'rig-etape-' + indexEtape} data-etape={indexEtape}>
            {page.rendue
              ? <Page page={page} indexEtape={indexEtape} total={total} plan={plan} />
              : <div className="rig-saisie__attente">{page.titre}</div>}
          </section>
        ))}
      </div>
    </div>
  )
}

function Page({ page, indexEtape, total, plan }: { page: PageEtapeVue, indexEtape: number, total: number, plan: GestePlan }) {
  const fiches = page.fiches
  return (
    <div className="rig-saisie__corps">
      <div className="rig-saisie__entete">
        <div>
          <div className="rig-kicker">{'Etape '}{indexEtape + 1}{' sur '}{total}</div>{' '}
          <h2 className="rig-saisie__titre">{page.titre}</h2>{' '}
          <div className="rig-saisie__source">{page.source}</div>
        </div>
      </div>

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

      {/* --- Controle final : le recapitulatif de TOUT ce qui bloque --- */}
      {page.recap && <Recap recap={page.recap} plan={plan} />}

      <Champs code={page.code} fiche={fiches?.actif ?? 0} champs={page.champs} plan={plan} />
    </div>
  )
}

function Recap({ recap, plan }: { recap: RecapVue, plan: GestePlan }) {
  if (recap.valide) {
    return (
      <div className="rig-recap">
        <div className="rig-recap__entete rig-recap__entete--valide">
          <span className="rig-recap__pastille rig-recap__pastille--ok">OK</span>{' '}
          <div><div className="rig-recap__titre">Dossier valide</div>{' '}
            <div className="rig-recap__detail">{' Les 7 etapes sont completes et acceptees. '}</div></div>{' '}
          <button className="rig-bouton" style={{ marginLeft: 'auto' }}
                  onClick={() => plan('rouvrir')}>Rouvrir la saisie</button>
        </div>
      </div>
    )
  }
  const n = recap.nombre
  return (
    <div className="rig-recap">
      <div className="rig-recap__entete">
        <span className={'rig-recap__pastille ' + (n === 0 ? 'rig-recap__pastille--ok' : 'rig-recap__pastille--bloque')}>
          {n === 0 ? 'OK' : String(n)}
        </span>{' '}
        <div>
          <div className="rig-recap__titre">{recap.titre}</div>{' '}
          <div className="rig-recap__detail">
            {recap.aManquer}{' a renseigner · '}{recap.refusees}{' valeur(s) refusee(s) '}
          </div>
        </div>{' '}
        {/* Valider n'est possible que sans aucun defaut (et le C# le revérifie). */}
        <button className="rig-bouton rig-bouton--fort" style={{ marginLeft: 'auto' }}
                disabled={n > 0}
                onClick={() => plan('valider')}>Valider le dossier</button>
      </div>
      {n > 0 && (
        <ul className="rig-recap__liste">
          {recap.groupes.map((g, i) => (
            <li key={i} className="rig-recap__groupe">
              <button className="rig-recap__aller"
                      onClick={() => plan('allerAuDefaut', { etape: g.indexEtape, fiche: g.fiche })}>
                <span className="rig-recap__rang">{g.indexEtape + 1}</span>{' '}
                <span>
                  {g.etape}
                  {g.multi && <span className="rig-recap__fiche">{g.unite}{' '}{g.fiche + 1}</span>}
                </span>{' '}
                <span className="rig-recap__nb">{g.nombre}</span>
              </button>{' '}
              <ul className="rig-recap__points">
                {g.points.map((d, j) => (
                  <li key={j} className={'rig-recap__point ' + (d.manquant ? '' : 'rig-recap__point--refuse')}>
                    <span className="rig-recap__champ">{d.champ}</span>{' '}
                    <span className="rig-recap__motif">{d.motif}</span>{' '}
                    <span className="rig-recap__colonne">{d.colonne}</span>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
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
