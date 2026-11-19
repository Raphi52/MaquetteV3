// L'ECRAN DE PROCEDURE (div.rig-proc) de Components/Pages/Home.razor, porte au
// lot 2 du plan « finir le passage a React » (19/11/2026). Le cadre est
// commun ; chaque procedure a son composant dans src/ecrans/, monte par
// l'aiguillage (Aiguillage.tsx). Les procedures sans ecran dedie tombent sur
// EcranNonMaquette, qui se nomme.

import { Aiguillage } from './Aiguillage'
import type { GestePlan, ProcVue } from './types'

export interface ProprietesProcedure {
  proc: ProcVue
  plan: GestePlan
}

export function Procedure({ proc, plan }: ProprietesProcedure) {
  const titreEtoile = proc.favori ? 'Retirer des favoris' : 'Mettre en favori'
  return (
    <div className="rig-proc">
      <div className="rig-vged__entete">
        <span className="rig-filet-v rig-filet-v--action"></span>{' '}
        {/* Le code technique (PROC_xxx) n'est JAMAIS affiche : l'agent lit son
            BESOIN, pas le nom interne de la procedure. Meme regle que dans le
            menu de gauche. */}
        <b>{proc.libelle}</b>{' '}
        <span style={{ marginLeft: 'auto' }}></span>{' '}
        {/* Etoile de la vue : meme geste et meme etat que l'etoile du menu de
            gauche (BasculerFavori sur le CODE), pour que la vue ouverte puisse
            etre mise en favori sans retourner dans le menu. */}
        <button className={'rig-vue__etoile ' + (proc.favori ? 'rig-vue__etoile--allumee' : '')}
                title={titreEtoile}
                aria-label={titreEtoile}
                aria-pressed={proc.favori ? 'true' : 'false'}
                onClick={() => plan('favori', proc.code)}>{proc.favori ? '★' : '☆'}</button>
        {/* Dans une fenetre d'onglet sorti, la croix de la fenetre ferme deja la
            vue : un second bouton « Fermer » faisait doublon (18/09/2026). */}
        {proc.boutonFermer && <button className="rig-bouton" onClick={() => plan('fermerProc')}>Fermer</button>}
      </div>

      {/* Bandeau du SUJET : tous les ecrans qui manipulent le meme objet metier
          sont ici, dans une seule vue. Repli : la procedure n'appartient a aucun
          sujet d'OBJET, on montre alors ses voisines de GESTE -- les ecrans du
          meme sous-menu. Aucune procedure ouverte ne reste donc isolee. */}
      {proc.sujet && (
        <div className="rig-sujet">
          <span className="rig-kicker">{proc.sujet.titre}</span>{' '}
          <div className="rig-onglets">
            {proc.sujet.onglets.map((o) => (
              <button key={o.code} className={'rig-onglet ' + (o.actif ? 'rig-onglet--actif' : '')}
                      title={o.detail ?? undefined}
                      onClick={() => plan('ouvrirCode', o.code)}>{o.libelle}</button>
            ))}
          </div>
        </div>
      )}

      <Aiguillage proc={proc} plan={plan} />
    </div>
  )
}
