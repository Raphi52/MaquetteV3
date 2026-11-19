// LA VISIONNEUSE D'AFFAIRE (esprit VGED, div.rig-vged) de
// Components/Pages/Home.razor, portee au lot 2 du plan « finir le passage a
// React » (19/11/2026). Quatre zones cote a cote (proposition 3 « Onglets
// Suivi / Fil » du /draft du 19/11/2026, conv-2009) : l'ARBRE documentaire, qui
// se replie en un chemin au-dessus des donnees ; les DONNEES (fiche
// d'informations ou saisie guidee) ; le DOCUMENT ; et la colonne de SUIVI, a
// deux onglets (reste a faire et historique des pieces / fil du dossier), qui se
// replie a droite. Les donnees et le document se ferment, et les separateurs se
// glissent pour regler les largeurs -- celle du suivi comprise.

import { useEffect, useRef } from 'react'
import type { CSSProperties, MutableRefObject } from 'react'
import { ClasseurArbre } from '../composants/ClasseurArbre'
import { FicheInformationsDossier } from '../composants/FicheInformationsDossier'
import { Saisie } from '../session'
import { Apercu } from './Apercu'
import { Etapes } from './Etapes'
import { SuiviDossier } from './Suivi'
import { BandeauTache, ChecklistsTache } from './Tache'
import type { DefilementVue, DossierVue, GestePlan } from './types'

/**
 * Le style des colonnes calcule par le C# (StyleCorps), en objet React :
 * « grid-template-columns: 400px 6px minmax(0, 1fr); user-select: none ».
 */
export function styleDepuisTexte(css: string): CSSProperties {
  const style: Record<string, string> = {}
  for (const declaration of css.split(';')) {
    const i = declaration.indexOf(':')
    if (i < 0) continue
    const nom = declaration.slice(0, i).trim().replace(/-([a-z])/g, (_, c: string) => c.toUpperCase())
    if (nom) style[nom] = declaration.slice(i + 1).trim()
  }
  return style as CSSProperties
}

export interface ProprietesVisionneuse {
  dossier: DossierVue
  pagination: number
  defilement: DefilementVue | null
  defilementJoue: MutableRefObject<number>
  plan: GestePlan
}

export function Visionneuse({ dossier: d, pagination, defilement, defilementJoue, plan }: ProprietesVisionneuse) {
  const s = d.sommaire
  // Une piece ouverte DEPUIS L'HISTORIQUE peut etre loin dans l'arbre (branche
  // Judiciaire, 40 lignes plus bas) : quand la piece ouverte change, l'arbre
  // defile jusqu'a sa ligne choisie. « nearest » : une ligne deja visible (clic
  // dans l'arbre lui-meme) ne bouge pas.
  const colonneArbre = useRef<HTMLDivElement>(null)
  const pieceOuverte = d.historique.find((p) => p.actif)?.id
  useEffect(() => {
    colonneArbre.current?.querySelector('.rig-arbre__ligne--actif')?.scrollIntoView?.({ block: 'nearest' })
  }, [pieceOuverte])
  // La liste des points a corriger se deroule PAR-DESSUS les boutons du
  // document : un clic ailleurs, ou le choix d'un lien, la referme.
  const defauts = useRef<HTMLDetailsElement>(null)
  useEffect(() => {
    const fermer = (e: PointerEvent) => {
      const liste = defauts.current
      if (liste?.open && !liste.contains(e.target as Node)) liste.open = false
    }
    document.addEventListener('pointerdown', fermer)
    return () => document.removeEventListener('pointerdown', fermer)
  }, [])
  return (
    <div className="rig-vged">
      <div className="rig-vged__entete">
        {/* La fleche ramene a l'accueil comme la maison : l'onglet de l'affaire
            reste dans la barre (19/11/2026). La croix, a droite, la ferme.
            Dans la fenetre d'un onglet SORTI, il n'y a pas d'accueil : pas de fleche
            (fermer la fenetre ramene l'affaire dans son onglet). */}
        {!d.sortie && (
          <button className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Retour a l'accueil" aria-label="Retour a l'accueil" onClick={() => plan('accueil')}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5"></path><path d="M12 19l-7-7 7-7"></path></svg>
          </button>
        )}{' '}
        <span className={'rig-filet-v rig-filet-v--' + d.statut}></span>{' '}
        {/* IDENTITE (proposition 5 du /draft du 19/11/2026) : le numero et l'objet
            en titre, la FICHE SOMMAIRE en puces dessous -- les champs d'identite
            que RIG v2 affiche au-dessus du treeview (PROC_VDOSSIER). UN SEUL
            FLUX : chaque champ VIDE disparait, libelle compris. */}
        <div className="rig-vged__identite">
          <div className="rig-vged__titre">
            <b className="rig-vged__numero">{d.numero}</b>{' '}<span>{d.objet}</span>
          </div>
          <div className="rig-vged__puces" aria-label="Fiche sommaire">
            {s.champs.map((c, i) => (
              <span key={i} className="rig-vged__puce">
                <span className="rig-vged__puce-libelle">{c.libelle}</span>{' '}
                <span className={'rig-vged__puce-valeur' + (c.mono ? ' rig-vged__puce-valeur--mono' : '')}>{c.valeur}</span>
              </span>
            ))}
            {s.mention.length > 0 && <span className="rig-vged__mention">{s.mention}</span>}
          </div>
        </div>
        {d.boutonDocument && (
          <button className="rig-bouton rig-bouton--plat" title="Reafficher le document a droite"
                  onClick={() => plan('afficherDocument')}>Document</button>
        )}
        {/* LES POINTS A CORRIGER, en pastille : visible des qu'un requis manque
            ou qu'une valeur est refusee. Elle deroule un lien par etape a
            corriger ; chaque lien ouvre l'etape (et la fiche). */}
        {d.bandeau && (
          <div className={'rig-vged__defauts' + (d.bandeau.erreur ? ' rig-vged__defauts--erreur' : '')} role="alert">
            <details ref={defauts}>
              <summary className="rig-vged__defauts-pastille" title="Voir les sections a corriger">
                <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 22 20H2z"></path><path d="M12 10v4"></path><path d="M12 17v.5"></path></svg>
                <b>{d.bandeau.nombre}</b>{' '}{d.bandeau.nombre === 1 ? 'point a corriger' : 'points a corriger'}
              </summary>
              <div className="rig-vged__defauts-liste">
                {d.bandeau.liens.map((l, i) => (
                  <button key={i} className={'rig-vged__defauts-lien' + (l.refuse ? ' rig-vged__defauts-lien--refuse' : '')}
                          title={l.titre}
                          onClick={() => {
                            if (defauts.current) defauts.current.open = false
                            plan('allerAuDefautBandeau', { etape: l.indexEtape, fiche: l.fiche })
                          }}>
                    {l.etape}{l.suffixe}{' '}
                    <span className="rig-vged__defauts-nb">{l.nombre}</span>
                  </button>
                ))}
              </div>
            </details>
          </div>
        )}
        <button className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Fermer l'affaire" aria-label="Fermer l'affaire" onClick={() => plan('fermerDossier')}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>
        </button>
      </div>

      <div className="rig-vged__corps" style={styleDepuisTexte(d.styleCorps)}>

        {/* ---- Arbre REPLIE : le chemin de l'element choisi, au-dessus des
            donnees et du document ; « Arbre » le rouvre. ---- */}
        {d.arbreReplie && (
          <nav className="rig-vged__chemin" aria-label="Chemin dans le dossier">
            <button type="button" className="rig-vged__chemin-arbre" title="Rouvrir l'arbre du dossier"
                    onClick={() => plan('arbreDeplier')}>
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 4v14a2 2 0 0 0 2 2h3"></path><path d="M5 9h5"></path><rect x="11" y="6.5" width="8" height="5" rx="1"></rect><rect x="11" y="16.5" width="8" height="5" rx="1"></rect></svg>
              Arbre
            </button>
            <ol className="rig-vged__chemin-etapes">
              {d.chemin.map((c, i) => (
                <li key={i} className={i === d.chemin.length - 1 ? 'rig-vged__chemin-courant' : undefined}>{c}</li>
              ))}
            </ol>
          </nav>
        )}

        {/* ---- Arbre documentaire, colonnes de TreeviewGed (v2) ---- */}
        {!d.arbreReplie && (
        <div className="rig-vged__arbre" ref={colonneArbre}>
          <div className="rig-arbre__entete">
            <span className="rig-arbre__libelle">Visualisation du dossier <span className="rig-arbre__pieces">{'· ' + d.pieces + (d.pieces === 1 ? ' piece' : ' pieces')}</span></span>{' '}
            <button className="rig-arbre__nouveau" title="Ajouter un dossier ou une piece dans l'arbre"
                    onClick={() => plan('ouvrirNouveau')}>+ Nouveau</button>{' '}
            <button type="button" className="rig-arbre__replier" title="Replier l'arbre" aria-label="Replier l'arbre"
                    onClick={() => plan('arbreReplier')}>
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M15 6l-6 6 6 6"></path></svg>
            </button>
          </div>
          {/* MODE TACHE : l'arbre est replie sur ce que la recette nomme. */}
          {d.tache && <BandeauTache tache={d.tache} plan={plan} />}
          {/* RECHERCHE : filtre les elements visibles de l'arbre. */}
          <div className="rig-arbre__recherche">
            <Saisie type="search" className="rig-arbre__filtre" placeholder="Filtrer l'arbre..."
                    aria-label="Filtrer les elements de l'arbre"
                    valeur={d.filtre} changer={(v) => plan('filtre', v)} />
            {d.filtre.length > 0 && (
              <button type="button" className="rig-arbre__filtre-effacer" title="Effacer le filtre"
                      onClick={() => plan('effacerFiltre')}>{'×'}</button>
            )}
          </div>{' '}
          {/* LE CLASSEUR (/draft du 19/11/2026, « Chronologie ») : une bascule par
              domaine, une carte par rubrique, la piece ouverte rappelee. */}
          <div className="rig-arbre">
            <ClasseurArbre arbre={d.arbre} chemin={d.chemin} filtre={d.filtre} tri={d.triClasseur}
                           trier={(tri) => plan('arbreTri', tri)}
                           activer={(id) => plan('arbreActiver', id)}
                           rubrique={(id) => plan('arbreRubrique', id)}
                           replier={(id) => plan('arbreReplierDossier', id)}
                           renommer={(r) => plan('arbreRenommer', r)}
                           menuContextuel={(souris) => plan('arbreMenu', souris)} />
          </div>
          {/* PROPAGATION -- EN PIED DE COLONNE (17/09/2026) : au-dessus de
              l'arbre, ouvrir la tache poussait tous les noeuds vers le bas. */}
          {d.tache && <ChecklistsTache tache={d.tache} plan={plan} />}
        </div>
        )}

        {/* ---- Separateur deplacable : regle la largeur de l'arbre ----
            preventDefault : sans lui, le navigateur demarre une SELECTION de
            texte des l'appui et toute la vue se couvre de surlignage bleu. */}
        {d.poignee && (
          <div className="rig-vged__poignee" title="Glisser pour ajuster l'arbre"
               onPointerDown={(e) => { e.preventDefault(); plan('redimDebut', { colonne: 1, x: e.clientX }) }}></div>
        )}

        {/* ---- ZONE 2 : LES DONNEES ---- la fiche d'informations quand une
            fiche est choisie dans l'arbre, sinon les 7 etapes de la saisie.
            L'historique des pieces, autrefois dessous, est passe dans l'onglet
            Suivi de la colonne de droite (proposition 3, 19/11/2026). */}
        {(d.fiche || d.etapes) && (
          <div className="rig-vged__milieu">
            {d.fiche && (
              <div key="fiche" className="rig-vged__guide rig-vged__donnees">
                <FicheInformationsDossier key={d.fiche.session} session={d.fiche.session} version={d.fiche.version}
                                          fermer={() => plan('ficheFermer')}
                                          toutAffiche={() => plan('toutAffiche')}
                                          champModifie={() => plan('champModifie')} />
              </div>
            )}
            {d.etapes && (
              <Etapes key="etapes" etapes={d.etapes} pagination={pagination} defilement={defilement}
                      defilementJoue={defilementJoue} plan={plan} />
            )}
          </div>
        )}
        {/* Seconde poignee : entre les donnees et le document. */}
        {d.poignee2 && (
          <div className="rig-vged__poignee" title="Glisser pour ajuster les donnees"
               onPointerDown={(e) => { e.preventDefault(); plan('redimDebut', { colonne: 2, x: e.clientX }) }}></div>
        )}

        {/* ---- ZONE 3 : LE DOCUMENT ---- */}
        {d.apercu && <Apercu colonne={d.apercu} plan={plan} />}

        {/* Troisieme poignee : devant la colonne de suivi, qu'elle elargit vers la
            gauche. Le C# ne connait pas la largeur rendue de la colonne souple : elle
            part avec la saisie. Repliee, la colonne n'a plus de poignee. */}
        {!d.suiviReplie && (
          <div className="rig-vged__poignee rig-vged__poignee--suivi" title="Glisser pour ajuster le suivi"
               onPointerDown={(e) => {
                 e.preventDefault()
                 const suivi = e.currentTarget.parentElement?.querySelector('.rig-vged__suivi')
                 plan('redimDebut', { colonne: 3, x: e.clientX, largeur: suivi?.getBoundingClientRect().width ?? 0 })
               }}></div>
        )}

        {/* ---- ZONE 4 : LE SUIVI ---- toujours a droite, onglets Suivi / Fil. */}
        <SuiviDossier bandeau={d.bandeau} historique={d.historique} fil={d.fil} replie={d.suiviReplie} plan={plan} />

        {d.voile && (
          <div className="rig-vged__voile"
               onPointerMove={(e) => plan('redim', { x: e.clientX, y: e.clientY })}
               onPointerUp={() => plan('redimFin')}
               onPointerLeave={() => plan('redimFin')}></div>
        )}
      </div>
    </div>
  )
}
