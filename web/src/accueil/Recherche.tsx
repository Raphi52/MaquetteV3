// LA RECHERCHE DE L'ACCUEIL : centree, toujours a la meme place, jamais
// deplacable. Elle trouve les TRAITEMENTS (« facture » propose l'ecran des
// factures), les AFFAIRES (numero, objet, SIREN) et les ELEMENTS d'un arbre
// (un numero de depot, de formalite ou de liasse ouvre l'affaire deja depliee
// sur cet element).
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis le
// <div class="rig-recherche"> de Home.razor. Le texte tape vit dans
// SessionAccueil ; les resultats sont calcules par le plan de travail (le
// fonds d'affaires, l'arbre ouvert) et arrivent dans la vue. Un clic remonte
// le RANG du resultat : Home ouvre celui qu'il a calcule a ce rendu.

import { Saisie } from '../session'
import type { GesteAccueil, RechercheVue } from './types'

const PETIT = { fontSize: '11px', color: 'var(--rig-texte-3)' }

export function Recherche({ recherche, geste }: { recherche: RechercheVue, geste: GesteAccueil }) {
  const r = recherche
  return (
    <div className="rig-recherche rig-recherche--fixe">
      <span style={{ font: '13px var(--rig-font-mono)', color: 'var(--rig-focus)' }}>{'⌕'}</span>
      {' '}
      <Saisie className="rig-recherche__saisie"
              placeholder="Rechercher un SIREN, une societe, un dirigeant, un n° de demande…"
              valeur={r.texte} quand="input" changer={(texte) => geste('recherche', texte)} />
      {' '}
      <span className="rig-touche">Ctrl K</span>
      {(r.affaires.length > 0 || r.traitements.length > 0 || r.elements.length > 0) && (
        <div className="rig-resultats">
          {r.traitements.map((t, rang) => (
            <button key={'t' + rang} className="rig-resultat" onClick={() => geste('traitement', rang)}>
              <span className="rig-filet-v rig-filet-v--action"></span>
              {' '}
              <span className="rig-resultat__id">{'→'}</span>
              {' '}
              <span>{t.libelle}<br />{' '}<span style={PETIT}>Ouvrir le traitement</span></span>
              {' '}
              <span className="rig-resultat__type">Action</span>
            </button>
          ))}
          {r.affaires.map((a, rang) => (
            <button key={'a' + rang} className="rig-resultat" onClick={() => geste('affaire', rang)}>
              <span className={'rig-filet-v rig-filet-v--' + a.statut}></span>
              {' '}
              <span className="rig-resultat__id">{a.numero}</span>
              {' '}
              <span>{a.objet}<br />{' '}<span style={PETIT}>{'SIREN '}{a.siren}</span></span>
              {' '}
              <span className="rig-resultat__type">{a.type}</span>
            </button>
          ))}
          {r.elements.map((e, rang) => (
            <button key={'e' + rang} className="rig-resultat" onClick={() => geste('element', rang)}>
              <span className={'rig-filet-v rig-filet-v--' + e.statut}></span>
              {' '}
              <span className="rig-resultat__id">{'\u{1F4C1}'}</span>
              {' '}
              <span>{e.nom}<br />{' '}<span style={PETIT}>{e.numero}{' · '}{e.chemin}</span></span>
              {' '}
              <span className="rig-resultat__type">Element</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
