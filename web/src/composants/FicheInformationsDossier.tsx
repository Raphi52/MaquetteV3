// « Informations du dossier » (Dossier RCS) : la vue du milieu en formulaire.
// Les sections et les champs viennent de Metier/InformationsDossier.cs, liste
// generee a partir des processus RIG v2 REPRIAC, REPRIPP, REPRIPP2, REPRIPM2,
// XACORRI et XBCORRI. Un bloc par table v2, repliable ; un filtre retrouve un
// champ parmi les centaines de la liste. Le meme formulaire sert
// « Informations des Formalites » (XACTION, Metier/InformationsFormalite.cs).
//
// Porte en React (lot 6 de la migration, 19/11/2026) depuis
// Components/FicheInformationsDossier.razor : meme balisage, memes classes CSS.
// Etat et regles (etapes nommees, vues de remplissage, saisie guidee, verrou
// KBIS, mode tache, historique, actions XACTION) vivent dans la session de
// Pont/FicheInformationsDossier.cs, que la COQUILLE ouvre avec l'etat vivant de
// l'accueil : React recoit son identifiant (session) et « version », qui change
// quand un rendu de l'accueil change ce que la fiche affiche. Chaque geste part
// a la session ; les rappels qu'il leve (champModifie, toutAffiche) reviennent
// dans la vue et sont remontes a la coquille.

import { useCallback, useEffect, useLayoutEffect, useRef, useState, type MutableRefObject } from 'react'
import { appeler } from '../pont'
import { Saisie } from '../session'

export interface ActionFicheVue { code: string, libelle: string }

export interface EntreeHistoVue {
  initiales: string
  auteur: string
  champ: string
  element: string
  /** « (vide) » quand la valeur l'etait. */
  avant: string
  apres: string
  /** « dd/MM/yyyy HH:mm ». */
  quand: string
  colonne: string
}

export interface TacheFicheVue { code: string, titre: string, tousChamps: boolean, caches: number }

export interface VerrouFicheVue { enAttente: boolean, attente: number, enEdition: boolean, brouillon: number }

export interface PasFriseVue {
  index: number
  titre: string
  blocs: number
  remplis: number
  total: number
  /** complet, encours, partiel ou vide. */
  etat: string
  courant: boolean
  bloquants: number
  /** Largeur du filet de progression, en %. */
  jauge: number
}

export interface FriseFicheVue {
  page: number
  precedentInactif: boolean
  suivantInactif: boolean
  pas: PasFriseVue[]
}

export interface RemplissageVue {
  /** 0 renseignes, 1 a saisir, 2 tous. */
  choix: number
  remplis: number
  aSaisir: number
  total: number
  aucunChamp: boolean
  guide: boolean
}

export interface GuideVue {
  vide: boolean
  rang: number
  total: number
  nbRequis: number
  requis: boolean
  partRequis: number
  avance: number
  etape: string
  section: string
  libelle: string
  cle: string
  valeur: string
  suivant: string
  /** Change chaque fois que le champ doit reprendre le focus. */
  focus: number
}

export interface ChampFicheVue {
  c: number
  libelle: string
  processus: string
  cle: string
  valeur: string
  titreColonne: string
}

export interface SectionFicheVue {
  s: number
  titre: string
  table: string
  champs: ChampFicheVue[]
}

export interface VueFiche {
  session: string
  domaine: string
  champs: number
  etapes: number
  blocs: number
  titre: string
  numero: string
  sources: string
  menuActions: boolean
  actions: ActionFicheVue[]
  historique: boolean
  derniereAction: ActionFicheVue | null
  entrees: EntreeHistoVue[]
  tache: TacheFicheVue | null
  verrou: VerrouFicheVue | null
  filtre: string
  frise: FriseFicheVue | null
  remplissage: RemplissageVue | null
  guide: GuideVue | null
  lectureSeule: boolean
  sections: SectionFicheVue[]
  /** Les rappels que le dernier geste leve ; vide hors geste. */
  rappels: string[]
}

export interface ProprietesFicheInformationsDossier {
  /** La session ouverte par la coquille (Pont/FicheInformationsDossier.cs). */
  session: string
  /** Change quand un rendu de l'accueil a change ce que la fiche affiche. */
  version: number
  fermer: () => void
  /** Un clic sur « Afficher tous les champs » : il se COMPTE. */
  toutAffiche: () => void
  /** Un champ vient d'etre ecrit : la vue qui porte la tache recompte. */
  champModifie: () => void
}

type Geste = (geste: string, valeur?: unknown) => void

function signaler(erreur: unknown): void {
  console.error('[pont] appel en echec', erreur)
}

/**
 * La vue de la session, relue a chaque changement de session ou de version.
 * Seule la reponse du DERNIER appel est affichee (meme regle que useSession) ;
 * les rappels d'un geste sont toujours remontes, meme si sa vue est depassee :
 * le champ, lui, a bien ete ecrit.
 */
function useVueFiche(session: string, version: number,
                     rappels: Record<string, () => void>): [VueFiche | null, Geste] {
  const [vue, setVue] = useState<VueFiche | null>(null)
  const dernier = useRef(0)
  const courants = useRef(rappels)
  useEffect(() => { courants.current = rappels })

  useEffect(() => {
    const numero = ++dernier.current
    appeler<VueFiche>('FicheInformationsDossier.Vue', session).then((v) => {
      if (numero === dernier.current) setVue(v)
    }, signaler)
  }, [session, version])

  const geste = useCallback<Geste>((nom, valeur) => {
    const numero = ++dernier.current
    appeler<VueFiche>('FicheInformationsDossier.Agir', session, nom, valeur ?? null).then((v) => {
      for (const r of v.rappels) {
        const rappel = courants.current[r]
        if (!rappel) throw new Error(`Rappel inconnu de la fiche : « ${r} ».`)
        rappel()
      }
      if (numero === dernier.current) setVue(v)
    }).catch(signaler)
  }, [session])

  return [vue, geste]
}

export function FicheInformationsDossier({ session, version, fermer, toutAffiche, champModifie }: ProprietesFicheInformationsDossier) {
  const [vue, geste] = useVueFiche(session, version, { toutAffiche, champModifie })
  // Le dernier focus rendu au champ guide (RendreFocus du .razor) : un compteur
  // de la session qui avance quand le champ doit reprendre la main.
  const focusRendu = useRef(0)
  if (vue === null) return null

  return (
    <>
      <div className="rig-guide__entete">
        <div>
          <div className="rig-kicker">{vue.domaine} &middot; {vue.champs} champs &middot; {vue.etapes} &eacute;tapes &middot; {vue.blocs} blocs</div>
          <h3 className="rig-guide__titre">{vue.titre}</h3>
          {/* Une ligne, coupee en « … » dans une colonne etroite : le texte entier en infobulle. */}
          <div className="rig-guide__source" title={'source RIG v2 : ' + vue.sources}>{vue.numero} &middot; source RIG v2 : {vue.sources}</div>
        </div>
        <div className="rig-fiche__outils">
          {/* Bouton ACTIONS : les actions du processus v2 XACTION (Metier/ActionsXaction.cs). */}
          <div className="rig-fiche__menu">
            <button className="rig-bouton rig-bouton--plat" aria-expanded={vue.menuActions ? 'true' : undefined}
                    onClick={() => geste('menuActions')}>Actions &#9662;</button>
            {vue.menuActions && (
              <ul className="rig-fiche__actions" role="menu">
                {vue.actions.map((action) => (
                  <li key={action.code}><button role="menuitem" title={action.code} onClick={() => geste('choisir', action.code)}>
                    <span>{action.libelle}</span><span className="rig-infos__table">{action.code}</span>
                  </button></li>
                ))}
              </ul>
            )}
          </div>
          {/* Bouton HISTORIQUE : remplace le formulaire par le journal des modifications. */}
          <button className="rig-bouton rig-bouton--plat" aria-pressed={vue.historique ? 'true' : undefined}
                  onClick={() => geste('historique')}>{vue.historique ? 'Formulaire' : 'Historique'}</button>
          <button className="rig-guide__fermer" title="Fermer" onClick={() => fermer()}>&times;</button>
        </div>
      </div>

      {vue.derniereAction !== null && (
        <div className="rig-fiche__note">Action demandée : {vue.derniereAction.libelle} ({vue.derniereAction.code}) &mdash; notée au journal du dossier.</div>
      )}

      {vue.historique ? (
        <div className="rig-histo">
          {vue.entrees.length === 0 && (
            <p className="rig-histo__vide">Aucune modification enregistrée pour {vue.numero}.</p>
          )}
          {vue.entrees.map((m, i) => (
            <article key={i} className="rig-histo__entree">
              <div className="rig-histo__pastille">{m.initiales}</div>
              <div className="rig-histo__corps">
                <div className="rig-histo__phrase">
                  <strong>{m.auteur}</strong> a modifié <strong>{m.champ}</strong> dans <em>{m.element}</em>
                </div>
                <div className="rig-histo__valeurs">
                  <span className="rig-histo__avant">{m.avant}</span>
                  <span className="rig-histo__fleche">&rarr;</span>
                  <span className="rig-histo__apres">{m.apres}</span>
                </div>
                <div className="rig-histo__meta">{m.quand} &middot; {m.colonne}</div>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <div className="rig-infos">
          {/* MODE TACHE : la recette declare les champs a modifier ; la fiche ne
              montre qu'eux. Le bouton rouvre les 523 champs SANS fermer la tache. */}
          {vue.tache !== null && (
            <div className="rig-tache__bandeau">
              <span className="rig-tache__code">{vue.tache.code}</span>
              <span className="rig-tache__titre">{vue.tache.titre}</span>
              <button className="rig-bouton rig-bouton--plat rig-tache__tout"
                      aria-pressed={vue.tache.tousChamps ? 'true' : undefined}
                      title="Afficher tous les champs de la fiche sans quitter la tache"
                      onClick={() => geste('tousChamps')}>
                {vue.tache.tousChamps
                  ? 'Revenir aux champs de la tache'
                  : 'Afficher tous les champs (' + vue.tache.caches + ' masques)'}
              </button>
            </div>
          )}
          {/* VERROU KBIS (maquette du 18/09/2026, agent SANS droits) : la fiche
              s'ouvre en lecture seule ; « Demander une modification » ouvre les
              champs, « Envoyer la demande » part vers un responsable et le KBIS
              reste celui en vigueur, marque « Modification en attente ». */}
          {vue.verrou !== null && (
            <div className={`rig-verrou ${vue.verrou.enAttente ? 'rig-verrou--attente' : vue.verrou.enEdition ? 'rig-verrou--edition' : ''}`}>
              {vue.verrou.enAttente ? (
                <>
                  <span className="rig-verrou__badge">Modification en attente</span>
                  <span className="rig-verrou__texte">{vue.verrou.attente} champ(s) soumis a la validation d'un responsable &mdash; le KBIS affiche reste celui en vigueur.</span>
                </>
              ) : vue.verrou.enEdition ? (
                <>
                  <span className="rig-verrou__texte">Modification en cours &mdash; {vue.verrou.brouillon} champ(s) modifie(s).</span>
                  <button className="rig-bouton rig-bouton--plat" onClick={() => geste('annulerEdition')}>Annuler</button>
                  <button className="rig-bouton rig-bouton--fort" disabled={vue.verrou.brouillon === 0}
                          onClick={() => geste('envoyerDemande')}>Envoyer la demande</button>
                </>
              ) : (
                <>
                  <span className="rig-verrou__cadenas" aria-hidden="true">&#128274;</span>
                  <span className="rig-verrou__texte">Lecture seule &mdash; vous n'avez pas les droits de modification du KBIS.</span>
                  <button className="rig-bouton rig-bouton--fort" onClick={() => geste('demanderModification')}>Demander une modification</button>
                </>
              )}
            </div>
          )}
          <Saisie className="rig-saisie-champ__saisie rig-infos__filtre" type="search"
                  placeholder="Filtrer un champ (libelle ou colonne)..."
                  title="Filtrer un champ (libelle ou colonne)"
                  valeur={vue.filtre} changer={(texte) => geste('filtre', texte)} />

          {/* FRISE A JAUGES, la meme que l'ecran de saisie d'une formalite
              (Home.razor, .rig-frise--h) : pastille OK / numero, libelle,
              avancement « remplis / total » et filet de progression. Une etape a
              la fois (EtapesInfoDossier) ; un filtre actif montre tout ce qui
              correspond. En mode tache, la frise n'est pas rendue. */}
          {vue.frise !== null && <Frise frise={vue.frise} geste={geste} />}

          {/* Le VIDE est un choix, plus un mur : par defaut on ne montre que les
              champs RENSEIGNES (41 sur 523 dans un dossier courant). */}
          {vue.remplissage !== null && (
            <div className="rig-infos__vue">
              <button className={`rig-infos__vue-choix ${vue.remplissage.choix === 0 ? 'rig-infos__vue-choix--actif' : ''}`}
                      onClick={() => geste('remplissage', 0)}>Renseignes {vue.remplissage.remplis}</button>
              <button className={`rig-infos__vue-choix ${vue.remplissage.choix === 1 ? 'rig-infos__vue-choix--actif' : ''}`}
                      onClick={() => geste('remplissage', 1)}>A saisir {vue.remplissage.aSaisir}</button>
              <button className={`rig-infos__vue-choix ${vue.remplissage.choix === 2 ? 'rig-infos__vue-choix--actif' : ''}`}
                      onClick={() => geste('remplissage', 2)}>Tous {vue.remplissage.total}</button>
              {/* SAISIE GUIDEE : un champ a la fois, au clavier. Elle prend TOUS
                  les champs de l'etape (pas seulement ceux de la vue courante). */}
              <button className={`rig-infos__vue-guide ${vue.remplissage.guide ? 'rig-infos__vue-guide--actif' : ''}`}
                      title="Saisir les champs de l'etape un par un, au clavier"
                      onClick={() => geste('guide')}>{vue.remplissage.guide ? 'Quitter la saisie guidee' : 'Saisie guidee'}</button>
            </div>
          )}

          {/* VUE VIDE : la carte ne reste pas blanche sous « aucun champ » (un
              dossier sans aucun champ renseigne s'ouvrait ainsi) ; elle dit
              pourquoi et propose la vue qui en montre. */}
          {vue.guide === null && vue.remplissage?.aucunChamp && (
            <div className="rig-infos__vide">
              <b>{vue.remplissage.choix === 0 && vue.remplissage.remplis === 0
                ? 'Aucun champ renseigne pour ce dossier.'
                : 'Aucun champ dans cette vue.'}</b>
              {vue.remplissage.choix !== 1 && vue.remplissage.aSaisir > 0 && (
                <button className="rig-bouton rig-bouton--plat" onClick={() => geste('remplissage', 1)}>
                  Voir les champs a saisir ({vue.remplissage.aSaisir})
                </button>
              )}
              {vue.remplissage.choix !== 0 && vue.remplissage.remplis > 0 && (
                <button className="rig-bouton rig-bouton--plat" onClick={() => geste('remplissage', 0)}>
                  Voir les champs renseignes ({vue.remplissage.remplis})
                </button>
              )}
            </div>
          )}
          {vue.guide !== null ? (
            vue.guide.vide
              ? <div className="rig-guidee__vide">Cette etape n'a aucun champ a saisir.</div>
              : <Guidee guide={vue.guide} geste={geste} focusRendu={focusRendu} />
          ) : vue.sections.map((bloc) => (
            <details key={bloc.s} className="rig-infos__section" open>
              <summary className="rig-infos__titre">
                {bloc.titre}
                <span className="rig-infos__table">{bloc.table}</span>
                <span className="rig-infos__compte">{bloc.champs.length}</span>
              </summary>
              <div className="rig-champs">
                {bloc.champs.map((c) => (
                  <label key={c.c} className="rig-saisie-champ rig-saisie-champ--moitie" title={c.processus}>
                    <span className="rig-saisie-champ__libelle">{c.libelle}</span>
                    <Saisie className="rig-saisie-champ__saisie" valeur={c.valeur} readOnly={vue.lectureSeule}
                            quand="change" changer={(texte) => geste('saisir', { s: bloc.s, c: c.c, valeur: texte })} />
                    {/* Sous le champ : la cle de trace. En infobulle, la COLONNE
                        REELLE de la base -- « INSPP_PATRONYME » et non « PATRONYME ». */}
                    <span className="rig-saisie-champ__colonne" title={c.titreColonne}>{c.cle}</span>
                  </label>
                ))}
              </div>
            </details>
          ))}
        </div>
      )}
    </>
  )
}

function Frise({ frise, geste }: { frise: FriseFicheVue, geste: Geste }) {
  return (
    <ol className="rig-frise rig-frise--h rig-infos__frise">
      <li className="rig-frise__fleche">
        <button className="rig-frise__fleche-bouton" title="Etape precedente"
                disabled={frise.precedentInactif}
                onClick={() => geste('page', frise.page - 1)}>&#8249;</button>
      </li>
      {frise.pas.map((g) => (
        <li key={g.index} className={`rig-frise__pas rig-frise__pas--${g.etat} ${g.courant ? 'rig-frise__pas--courant' : ''}`}>
          <button className="rig-frise__bouton"
                  title={g.titre + ' - ' + g.blocs + ' bloc(s), ' + g.total + ' champs'}
                  onClick={() => geste('etape', g.index)}>
            <span className="rig-frise__libelle">{g.titre}</span>
            {g.bloquants > 0
              ? <span className="rig-frise__requis-restants">{g.bloquants} requis</span>
              : <span className="rig-frise__compte">{g.remplis} / {g.total}</span>}
            <span className="rig-frise__jauge">
              <span className="rig-frise__jauge-part" style={{ width: g.jauge + '%' }}></span>
            </span>
          </button>
        </li>
      ))}
      <li className="rig-frise__fleche">
        <button className="rig-frise__fleche-bouton" title="Etape suivante"
                disabled={frise.suivantInactif}
                onClick={() => geste('page', frise.page + 1)}>&#8250;</button>
      </li>
    </ol>
  )
}

interface ProprietesGuidee {
  guide: GuideVue
  geste: Geste
  focusRendu: MutableRefObject<number>
}

/**
 * La SAISIE GUIDEE : un champ a la fois, au clavier. Entree passe au suivant,
 * Maj+Entree revient, Echap sort (regles dans la session : ClavierGuide).
 *
 * Le champ est recree a chaque champ guide (key = sa cle) : il montre la valeur
 * de CE champ. Dans le .razor, le meme <input> servait tous les champs et le
 * texte tape restait affiche dans le suivant sans y etre ecrit (vu le 19/11 sur
 * la capture « avant »). Le texte tape part avec le geste qui le valide.
 */
function Guidee({ guide, geste, focusRendu }: ProprietesGuidee) {
  const champ = useRef<HTMLInputElement>(null)
  const saisie = useRef('')

  // useLayoutEffect pour les deux (19/11/2026) : ils tournent pendant que
  // React ecrit le nouveau champ dans la page. Un useEffect peut etre repousse
  // a une tache suivante (rendu de plus de 5 ms, machine chargee) : le champ
  // etait alors a l'ecran sans focus, et une frappe tombee entre les deux etait
  // effacee par la remise a zero (meme cause que Saisie, session.tsx).

  // Un nouveau champ guide : rien n'y est encore tape.
  useLayoutEffect(() => { saisie.current = '' }, [guide.cle])

  // Le champ guide prend le focus : sans cela, il faudrait cliquer dans la case
  // apres chaque Entree -- la saisie au clavier perdrait tout son interet.
  useLayoutEffect(() => {
    if (guide.focus === focusRendu.current) return
    focusRendu.current = guide.focus
    champ.current?.focus()
  }, [guide.focus, guide.cle, focusRendu])

  const valider = () => {
    const texte = saisie.current
    saisie.current = ''
    geste('validerGuide', texte)
  }

  return (
    <div className="rig-guidee">
      {/* Le FOND de la jauge marque la portion obligatoire du parcours ; la part
          qui avance passe du rouge (on est dans les requis) a l'accent. */}
      <div className="rig-guidee__jauge"
           style={{ background: 'linear-gradient(90deg,var(--rig-alerte-fond,#f6dcd8) 0 ' + guide.partRequis + '%,var(--rig-bord) ' + guide.partRequis + '% 100%)' }}>
        <span className={`rig-guidee__jauge-part ${guide.requis ? 'rig-guidee__jauge-part--requis' : ''}`}
              style={{ width: guide.avance + '%' }}></span>
      </div>
      <div className="rig-kicker">
        {guide.requis && (
          <>
            <span className="rig-guidee__requis">Requis {guide.rang} / {guide.nbRequis}</span>
            {' · '}
          </>
        )}
        {guide.etape}{' · '}{guide.section}{' · champ '}{guide.rang}{' / '}{guide.total}
      </div>
      <h2 className="rig-guidee__libelle">{guide.libelle}</h2>
      <input key={guide.cle} ref={champ}
             className={`rig-guidee__saisie ${guide.requis ? 'rig-guidee__saisie--requis' : ''}`}
             defaultValue={guide.valeur}
             onInput={(e) => { saisie.current = e.currentTarget.value }}
             onKeyDown={(e) => {
               // Seules Entree et Echap ont un effet (ClavierGuide) : les autres
               // touches ne font pas l'aller-retour.
               if (e.key !== 'Enter' && e.key !== 'Escape') return
               const texte = saisie.current
               if (e.key === 'Enter') saisie.current = ''
               geste('clavierGuide', { key: e.key, shiftKey: e.shiftKey, saisie: texte })
             }} />
      <div className="rig-guidee__aide">
        <span><b>Entree</b> valider et suivant</span>
        <span><b>Maj+Entree</b> revenir</span>
        <span><b>Echap</b> tout voir</span>
        <span className="rig-guidee__suivant">suivant : {guide.suivant}</span>
      </div>
      <div className="rig-guidee__pied">
        <button className="rig-bouton rig-bouton--plat" disabled={guide.rang === 1}
                onClick={() => geste('precedentGuide')}>&#8249; Precedent</button>
        <button className="rig-bouton rig-bouton--plat" disabled={guide.rang === guide.total}
                onClick={valider}>Suivant &#8250;</button>
        <span className="rig-guidee__colonne">{guide.cle}</span>
      </div>
    </div>
  )
}
