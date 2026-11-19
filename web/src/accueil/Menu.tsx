// LE MENU DE GAUCHE : les METIERS du greffe (catalogue Pont/MenuAccueil.cs).
//
// Le panneau suit l'ORGANISATION du greffe : le RCS et le JUDICIAIRE sont deux
// mondes qu'un agent pratique rarement en meme temps. Chaque monde porte SA
// couleur : on reconnait ou on est avant meme d'avoir lu. Le menu reste
// TOUJOURS visible : reduit, il laisse un rail ou les pastilles de couleur
// restent cliquables ; il ne disparait jamais.
//
// Porte en React (lot 1 du plan « finir le passage a React ») depuis le
// <nav class="rig-menu"> de Home.razor ; la vue (metiers visibles pour le
// login, verbes ouverts, favoris) est calculee par SessionAccueil.

import { Fragment } from 'react'
import type { GesteAccueil, MenuVue } from './types'

export function Menu({ menu, reduit, geste }: { menu: MenuVue, reduit: boolean, geste: GesteAccueil }) {
  return (
    <nav className="rig-menu">
      <div className="rig-menu__entete">
        <button className="rig-menu__replier" onClick={() => geste('menu')}
                title={reduit ? 'Deployer le menu' : 'Reduire le menu'}>
          {reduit ? '»' : '«'}
        </button>
      </div>

      {/* FAVORIS : les ecrans etoiles, en haut du menu. Ce groupe ne vient PAS
          du catalogue : son contenu est choisi par l'utilisateur et vit dans
          accueil.json. Tant qu'aucune etoile n'est allumee, il ne prend pas de
          place. L'etoile est un bouton FRERE du lien, jamais imbrique : un
          bouton dans un bouton declencherait les deux gestes. */}
      {menu.favoris.length > 0 && (
        <div className={'rig-menu__groupe rig-menu__groupe--fav ' + (menu.favorisOuverts ? 'rig-menu__groupe--ouvert' : '')}>
          <button className="rig-menu__tete" title="Favoris" onClick={() => geste('favorisOuverts')}>
            <span className="rig-menu__chevron">{'▸'}</span>
            {' '}
            <span className="rig-menu__pastille">{'★'}</span>
            {' '}
            <span className="rig-menu__titre">Favoris</span>
            {' '}
            <span className="rig-menu__compte">{menu.favoris.length}</span>
          </button>
          {menu.favorisOuverts && (
            <ul className="rig-menu__liste">
              {menu.favoris.map((e) => (
                <li key={e.code} className="rig-menu__ligne">
                  <button className={'rig-menu__lien rig-menu__lien--metier ' + (e.actif ? 'rig-menu__lien--actif' : '')}
                          title={e.detail}
                          onClick={() => geste('ouvrir', e.code)}>
                    <span className="rig-menu__quoi">{e.libelle}</span>
                  </button>
                  {' '}
                  <button className="rig-menu__etoile rig-menu__etoile--allumee"
                          title="Retirer des favoris"
                          onClick={() => geste('favori', e.code)}>{'★'}</button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {menu.moments.map((moment) => (
        <div key={moment.titre}
             className={'rig-menu__groupe rig-menu__groupe--' + moment.teinte + ' ' + (moment.ouvert ? 'rig-menu__groupe--ouvert' : '')}>
          <button className="rig-menu__tete" title={moment.titre}
                  onClick={() => geste('moment', moment.titre)}>
            <span className="rig-menu__chevron">{'▸'}</span>
            {' '}
            <span className="rig-menu__pastille">{moment.signe}</span>
            {' '}
            <span className="rig-menu__titre">{moment.titre}</span>
            {' '}
            <span className="rig-menu__compte">{moment.compte}</span>
          </button>
          {/* Troisieme niveau : le metier se lit par VERBES. Un verbe ouvert
              montre ses ecrans ; les autres restent des titres d'une ligne. */}
          {moment.sous.map((sous, rang) => (
            <Fragment key={rang + '/' + sous.titre}>
              {sous.titre.length > 0 && (
                <button className={'rig-menu__sous ' + (sous.ouvert ? 'rig-menu__sous--ouvert' : '')}
                        title={sous.titre}
                        onClick={() => geste('sous', { moment: moment.titre, sous: sous.titre })}>
                  <span className="rig-menu__chevron">{'▸'}</span>
                  {' '}
                  <span className="rig-menu__sous-titre">{sous.titre}</span>
                  {' '}
                  <span className="rig-menu__compte">{sous.compte}</span>
                </button>
              )}
              {sous.ouvert && (
                <ul className="rig-menu__liste">
                  {sous.entrees.map((e) => (
                    <li key={e.code} className="rig-menu__ligne">
                      {/* Le code technique n'est JAMAIS affiche : il reste
                          l'identifiant interne, l'utilisateur lit le besoin. */}
                      <button className={'rig-menu__lien rig-menu__lien--metier ' + (e.actif ? 'rig-menu__lien--actif' : '')}
                              title={e.detail}
                              onClick={() => geste('ouvrir', e.code)}>
                        <span className="rig-menu__quoi">{e.libelle}</span>
                      </button>
                      {' '}
                      <button className={'rig-menu__etoile ' + (e.favori ? 'rig-menu__etoile--allumee' : '')}
                              title={e.favori ? 'Retirer des favoris' : 'Mettre en favori'}
                              onClick={() => geste('favori', e.code)}>{e.favori ? '★' : '☆'}</button>
                    </li>
                  ))}
                </ul>
              )}
            </Fragment>
          ))}
        </div>
      ))}
    </nav>
  )
}
