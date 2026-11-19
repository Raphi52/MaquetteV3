// LA VISIONNEUSE D'AFFAIRE (esprit VGED, div.rig-vged) de
// Components/Pages/Home.razor, portee au lot 2 du plan « finir le passage a
// React » (19/11/2026). Trois zones cote a cote : l'ARBRE documentaire, les
// DONNEES (fiche d'informations ou saisie guidee), le DOCUMENT ; chacune se
// ferme, et les separateurs se glissent pour regler les largeurs.

import type { CSSProperties, MutableRefObject } from 'react'
import { ArbreGedNoeud } from '../composants/ArbreGedNoeud'
import { FicheInformationsDossier } from '../composants/FicheInformationsDossier'
import { Saisie } from '../session'
import { Apercu } from './Apercu'
import { Etapes } from './Etapes'
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
  return (
    <div className="rig-vged">
      <div className="rig-vged__entete">
        <button className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Retour a l'accueil" aria-label="Retour a l'accueil" onClick={() => plan('fermerDossier')}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5"></path><path d="M12 19l-7-7 7-7"></path></svg>
        </button>{' '}
        <span className={'rig-filet-v rig-filet-v--' + d.statut}></span>{' '}
        <b style={{ font: '13px var(--rig-font-mono)' }}>{d.numero}</b>{' '}
        <span>{d.objet}</span>{' '}
        <span style={{ color: 'var(--rig-texte-3)', fontSize: '12px' }}>{' · SIREN '}{d.siren}{' · '}{d.type}</span>{' '}
        <span style={{ marginLeft: 'auto' }}></span>
        {d.boutonDocument && (
          <button className="rig-bouton rig-bouton--plat" title="Reafficher le document a droite"
                  onClick={() => plan('afficherDocument')}>Document</button>
        )}
        <button className="rig-bouton rig-bouton--icone rig-bouton--plat" title="Fermer l'affaire" aria-label="Fermer l'affaire" onClick={() => plan('fermerDossier')}>
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12"></path><path d="M18 6L6 18"></path></svg>
        </button>
      </div>

      {/* FICHE SOMMAIRE (bandeau du haut de la vue arbre) : les champs
          d'identite que RIG v2 affiche au-dessus du treeview (PROC_VDOSSIER).
          UN SEUL FLUX : chaque champ VIDE disparait, libelle compris. */}
      <details className="rig-sommaire" open>
        <summary className="rig-sommaire__titre">
          {' Fiche sommaire '}
          <span className="rig-sommaire__resume">{d.numero}{' · '}{s.designation}</span>
        </summary>{' '}
        <div className="rig-sommaire__ligne">
          {s.champs.map((c, i) => (
            <span key={i} className="rig-sommaire__champ">
              <span className="rig-sommaire__libelle">{c.libelle}</span>{' '}
              <span className={'rig-sommaire__valeur ' + (c.mono ? 'rig-sommaire__valeur--mono' : '')}>{c.valeur}</span>
            </span>
          ))}
          {s.mention.length > 0 && <span className="rig-sommaire__mention">{s.mention}</span>}
        </div>
      </details>

      {/* Bandeau des defauts : visible des qu'un requis manque ou qu'une valeur
          est refusee. Chaque lien ouvre l'etape (et la fiche) ou corriger. */}
      {d.bandeau && (
        <div className={'rig-bandeau-defauts ' + (d.bandeau.erreur ? 'rig-bandeau-defauts--erreur' : '')} role="alert">
          <details className="rig-bandeau-defauts__replis">
            <summary className="rig-bandeau-defauts__resume">
              <span className="rig-bandeau-defauts__titre">
                <b>{d.bandeau.nombre}</b>{' '}{d.bandeau.nombre === 1 ? 'point a corriger' : 'points a corriger'}
              </span>{' '}
              <span className="rig-bandeau-defauts__voir">voir les sections</span>
            </summary>{' '}
            <div className="rig-bandeau-defauts__liste">
              {d.bandeau.liens.map((l, i) => (
                <button key={i} className={'rig-bandeau-defauts__lien ' + (l.refuse ? 'rig-bandeau-defauts__lien--refuse' : 'rig-bandeau-defauts__lien--manquant')}
                        title={l.titre}
                        onClick={() => plan('allerAuDefautBandeau', { etape: l.indexEtape, fiche: l.fiche })}>
                  {l.etape}{l.suffixe}{' '}
                  <span className="rig-bandeau-defauts__nb">{l.nombre}</span>
                </button>
              ))}
            </div>
          </details>
        </div>
      )}

      <div className="rig-vged__corps" style={styleDepuisTexte(d.styleCorps)}>

        {/* ---- Arbre documentaire, colonnes de TreeviewGed (v2) ---- */}
        <div className="rig-vged__arbre">
          <div className="rig-arbre__entete">
            <span className="rig-arbre__libelle">Visualisation du dossier</span>{' '}
            <button className="rig-arbre__nouveau" title="Ajouter un dossier ou une piece dans l'arbre"
                    onClick={() => plan('ouvrirNouveau')}>+ Nouveau</button>
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
          <div className="rig-arbre">
            <ArbreGedNoeud ligne={d.arbre}
                           activer={(id) => plan('arbreActiver', id)}
                           renommer={(r) => plan('arbreRenommer', r)}
                           menuContextuel={(souris) => plan('arbreMenu', souris)} />
          </div>
          {/* PROPAGATION -- EN PIED DE COLONNE (17/09/2026) : au-dessus de
              l'arbre, ouvrir la tache poussait tous les noeuds vers le bas. */}
          {d.tache && <ChecklistsTache tache={d.tache} plan={plan} />}
        </div>

        {/* ---- Separateur deplacable : regle la largeur de l'arbre ----
            preventDefault : sans lui, le navigateur demarre une SELECTION de
            texte des l'appui et toute la vue se couvre de surlignage bleu. */}
        {d.poignee && (
          <div className="rig-vged__poignee" title="Glisser pour ajuster l'arbre"
               onPointerDown={(e) => { e.preventDefault(); plan('redimDebut', { colonne: 1, x: e.clientX }) }}></div>
        )}

        {/* ---- ZONE 2 : LES DONNEES ---- la fiche d'informations quand une
            fiche est choisie dans l'arbre, sinon les 7 etapes de la saisie. */}
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
        {/* Seconde poignee : entre les donnees et le document. */}
        {d.poignee2 && (
          <div className="rig-vged__poignee" title="Glisser pour ajuster les donnees"
               onPointerDown={(e) => { e.preventDefault(); plan('redimDebut', { colonne: 2, x: e.clientX }) }}></div>
        )}

        {/* ---- ZONE 3 : LE DOCUMENT ---- */}
        {d.apercu && <Apercu colonne={d.apercu} plan={plan} />}

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
