// Le rond de profil (« Commandes rondes », 18/11/2026) : les initiales du
// login, cercle d'or ; un clic deroule les comptes, un compte choisi change de
// login. Sur l'accueil il est a droite du titre (Entete.tsx) ; un ecran ouvert
// recouvre ce titre, il est alors rendu dans la barre du haut (BarreRig.tsx) :
// un seul des deux existe a la fois, la vue ne remplit que l'un des deux.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis le
// RenderFragment RondProfil de Home.razor : meme balisage, memes morceaux de
// texte (lus dans le code que Razor generait).

import { arreter, type GesteAccueil, type ProfilVue } from './types'

export function RondProfil({ profil, geste }: { profil: ProfilVue, geste: GesteAccueil }) {
  return (
    <div className={'rig-profil-ancre ' + (profil.dansLaBarre ? 'rig-profil-ancre--barre' : '')}
         onMouseDown={arreter}>
      <button className={'rig-profil ' + (profil.ouvert ? 'rig-profil--ouvert' : '')}
              title={profil.titre}
              aria-label={'Profil ' + profil.login + ' : changer de login'}
              aria-expanded={profil.ouvert ? 'true' : 'false'}
              onClick={() => geste('profil')}>{profil.initiales}</button>
      {profil.ouvert && (
        <>
          {/* Voile transparent plein ecran : un clic AILLEURS ferme le panneau. */}
          <div className="rig-voile-clic" onClick={() => geste('fermerProfil')}></div>
          {' '}
          <div className="rig-tuiles-panneau rig-profil-panneau">
            <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-2)' }}>Login connecte</div>
            {profil.comptes.map((compte) => (
              <button key={compte.login}
                      className={'rig-compte ' + (compte.actif ? 'rig-compte--actif' : '')}
                      aria-pressed={compte.actif ? 'true' : 'false'}
                      onClick={() => geste('login', compte.login)}>
                <span className="rig-compte__rond">{compte.initiales}</span>
                {' '}
                <span className="rig-compte__texte">
                  <span className="rig-compte__nom">{compte.login}{' · '}{compte.profil}</span>
                  {' '}
                  <span className="rig-compte__domaines">{compte.domaines}</span>
                </span>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
