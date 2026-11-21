// L'ECRAN DE PROCEDURE (div.rig-proc) de Components/Pages/Home.razor, porte au
// lot 2 du plan « finir le passage a React » (19/11/2026). Le cadre est
// commun ; chaque procedure a son composant dans src/ecrans/, monte par
// l'aiguillage (Aiguillage.tsx). Les procedures sans ecran dedie tombent sur
// EcranNonMaquette, qui se nomme.
//
// Le cadre ne porte plus rien au-dessus de l'ecran (19/11/2026, conv-2010, a la
// demande de l'utilisateur : « ca prend de la place pour rien ») : ni la ligne
// du titre (etoile, Fermer), ni le bandeau des ecrans voisins du meme sujet.
// L'onglet porte le titre et sa croix ferme l'ecran ; l'etoile reste dans le
// menu de gauche.
//
// `cachee` : l'onglet de cet ecran est ouvert mais ne se montre pas. L'ecran
// reste monte (son etat survit au changement d'onglet, Application.tsx) ; seul
// son cadre sort de la page.
//
// Exception, une ligne fine : l'ecran ouvert DEPUIS UNE AFFAIRE (barre d'actions
// du dossier, 20/11/2026) montre la puce « ← Dossier 004176 SCI DU PARC », qui
// y ramene (geste « retourDossier ») ; fermer son onglet y ramene aussi (C#).

import { AideTouchesEcran } from '../ecrans/EcranCorrespondances'
import { Aiguillage } from './Aiguillage'
import type { GestePlan, ProcVue } from './types'
import { EcranVisible } from './visibilite'

export interface ProprietesProcedure {
  proc: ProcVue
  plan: GestePlan
  cachee?: boolean
}

export function Procedure({ proc, plan, cachee = false }: ProprietesProcedure) {
  return (
    // display:none en ligne : .rig-proc pose display:flex, qui l'emporterait sur l'attribut hidden.
    <div className="rig-proc" {...(cachee ? { style: { display: 'none' }, 'aria-hidden': true } : {})}>
      {proc.ecran.dossier && (
        <div className="rig-proc__dossier">
          <button type="button" className="rig-proc__dossier-retour" title="Revenir a l'affaire"
                  onClick={() => plan('retourDossier')}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5"></path><path d="M12 19l-7-7 7-7"></path></svg>
            {'Dossier '}<b>{proc.ecran.dossier.numero}</b>{' '}<span>{proc.ecran.dossier.nom}</span>
          </button>
        </div>
      )}
      <EcranVisible.Provider value={!cachee}>
        <Aiguillage proc={proc} plan={plan} />
      </EcranVisible.Provider>
      {/* SOC-027 CA03 : « ? » montre les touches de l'ecran visible, l'ancienne touche de la V2 a cote. */}
      <AideTouchesEcran code={proc.code} actif={!cachee} />
    </div>
  )
}
