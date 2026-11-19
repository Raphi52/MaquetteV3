// LA BARRE DU HAUT de la fenetre RIG : marque, version, reglages de la barre
// (le theme), rond de profil quand un ecran recouvre l'accueil, et les trois
// boutons de la fenetre. Un appui sur la barre DEPLACE la fenetre.
//
// Porte en React (lot 1 du plan « finir le passage a React », 19/11/2026)
// depuis le <header class="rig-barre"> de Components/Pages/Home.razor : meme
// balisage, memes classes, memes morceaux de texte (lus dans le code que Razor
// generait). L'etat (panneau ouvert, theme, login) vit dans SessionAccueil
// (Pont/Accueil.cs) ; chaque geste lui remonte par le rappel « geste », et les
// gestes de fenetre (deplacer, reduire...) y deviennent une DemandeAccueil que
// Home.razor execute par FenetreInterop.

import { Liste } from '../accueil/Liste'
import { RondProfil } from '../accueil/RondProfil'
import { arreter, type GesteAccueil, type GesteIle, type VueBarre } from '../accueil/types'

export interface ProprietesBarreRig {
  vue: VueBarre
  geste: (g: GesteIle) => void
}

export function BarreRig({ vue, geste }: ProprietesBarreRig) {
  const agir: GesteAccueil = (nom, valeur) => { geste({ nom, valeur: valeur ?? null }) }
  return (
    <header className="rig-barre" onMouseDown={() => agir('deplacer')}>
      <span className="rig-barre__marque">RIG <b>V3</b></span>
      {' '}
      <span className="rig-barre__version">{vue.version}</span>
      {' '}
      <div className="rig-barre__droite">
        {/* Les domaines se parametrent par login : le rond de profil reste
            atteignable depuis n'importe quel ecran ; un ecran ouvert recouvre
            le titre de l'accueil, le rond passe alors ICI. */}
        {vue.profil && <RondProfil profil={vue.profil} geste={agir} />}
        {/* Reglages de la BARRE : toujours atteignables, y compris quand un ecran
            de procedure occupe le plan de travail. Ne contient que le theme. */}
        <div className="rig-barre__reglages" onMouseDown={arreter}>
          <button className={'rig-barre__action ' + (vue.reglages ? 'rig-barre__action--ouvert' : '')}
                  title="Reglages" onClick={() => agir('panneauBarre')}>{'⚙'}</button>
          {vue.reglages && (
            <>
              {/* Voile transparent plein ecran : un clic AILLEURS ferme le panneau. */}
              <div className="rig-voile-clic" onClick={() => agir('fermerPanneauBarre')}
                   onMouseDown={arreter}></div>
              {' '}
              <div className="rig-tuiles-panneau">
                <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-2)' }}>Affichage</div>
                {' '}
                {/* Une liste et non plus la bascule « Mode sombre » : depuis le
                    18/11/2026 il y a plus de deux themes (ThemesRig.Tous). */}
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Theme</span>
                  {' '}
                  <Liste className="rig-saisie-champ__saisie" id="rig-choix-theme"
                         valeur={vue.theme} changer={(code) => agir('theme', code)}>
                    {vue.themes.map((t) => <option key={t.code} value={t.code}>{t.libelle}</option>)}
                  </Liste>
                </label>
              </div>
            </>
          )}
        </div>
        {' '}
        <div className="rig-fenetre">
          <button className="rig-fenetre__bouton" title="Reduire"
                  onClick={() => agir('reduire')} onMouseDown={arreter}>{'–'}</button>
          {' '}
          <button className="rig-fenetre__bouton" title="Agrandir"
                  onClick={() => agir('agrandir')} onMouseDown={arreter}>{'□'}</button>
          {' '}
          <button className="rig-fenetre__bouton rig-fenetre__bouton--fermer" title="Fermer"
                  onClick={() => agir('fermer')} onMouseDown={arreter}>{'✕'}</button>
        </div>
      </div>
    </header>
  )
}
