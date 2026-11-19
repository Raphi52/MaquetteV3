// LA RACINE DE LA PAGE : ce que la fenetre WPF fait monter dans #app, depuis
// le retrait de Blazor (lot 3 du plan « finir le passage a React », 19/11/2026).
// Elle remplace Main.razor, Home.razor, DocumentDetacheVue.razor et
// Components/IleReact.razor.
//
// Au chargement, la page demande sa racine au C# (« Racine.Ouvrir »,
// Pont/Racines.cs) : le composant a monter (registre de iles.ts), ses
// proprietes, les noms de ses gestes et les bandes de bord. Ensuite le C#
// renvoie la racine de lui-meme a chaque changement (evenement « racine ») :
// React re-rend avec les nouvelles proprietes, son etat est garde.
//
// Meme arbre que sous Blazor, pour un rendu identique au pixel : le composant
// est dans un div[data-ile] en display:contents (aucune boite), suivi des
// bandes de bord.

import { useMemo } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { Bords } from './fenetre/Bords'
import { composantMontable } from './iles'
import { appeler, ecouter, rappel } from './pont'

/** Ce que le C# envoie (EtatRacine, Pont/Repartiteur.cs). */
export interface EtatRacine {
  composant: string
  rappels: string[]
  bords: boolean
  proprietes: Record<string, unknown>
}

export function Racine({ etat }: { etat: EtatRacine }) {
  const Composant = composantMontable(etat.composant)
  const cleRappels = etat.rappels.join('|')
  // Les fonctions de geste ne changent pas d'un rendu a l'autre : un composant
  // qui les garde (useCallback, useRef) n'est pas rebranche a chaque envoi.
  const rappels = useMemo(
    () => Object.fromEntries(cleRappels.split('|').filter(Boolean).map((nom) => [nom, rappel(nom)])),
    [cleRappels],
  )
  const redimensionner = useMemo(() => rappel('bord'), [])
  return (
    <>
      <div data-ile={etat.composant} style={{ display: 'contents' }}>
        <Composant {...etat.proprietes} {...rappels} />
      </div>
      {etat.bords && <Bords redimensionner={redimensionner} />}
    </>
  )
}

/** La page montee : `arreter` cesse d'ecouter le C# et demonte React. */
export interface PageMontee {
  arreter(): void
}

/**
 * Monte la racine de la fenetre dans `app`. Une erreur (pont absent, composant
 * inconnu, C# en echec, rendu qui casse) s'ecrit dans `app` et arrete la page :
 * elle ne reste jamais blanche ni bloquee sur « Chargement... » sans dire
 * pourquoi.
 */
export async function demarrer(app: HTMLElement): Promise<PageMontee> {
  let racine: Root | undefined
  let cesser: (() => void) | undefined
  const arreter = (): void => {
    cesser?.()
    cesser = undefined
    racine?.unmount()
    racine = undefined
  }
  const echouer = (erreur: unknown): void => {
    console.error('[racine]', erreur)
    arreter()
    app.textContent = 'RIG V3 n\'a pas pu afficher sa page : '
      + (erreur instanceof Error ? erreur.message : String(erreur))
  }
  const rendre = (etat: EtatRacine): void => {
    // Un nom inconnu echoue ICI, avant le rendu : l'erreur est la notre.
    composantMontable(etat.composant)
    if (!racine) {
      app.textContent = ''
      // Une erreur pendant le rendu : React a deja retire l'arbre ; on le dit
      // une fois son traitement fini.
      racine = createRoot(app, { onUncaughtError: (erreur) => queueMicrotask(() => echouer(erreur)) })
    }
    racine.render(<Racine etat={etat} />)
  }
  try {
    // L'ecoute d'abord : un changement envoye juste apres l'ouverture arrive.
    cesser = ecouter('racine', (valeur) => {
      try { rendre(valeur as EtatRacine) } catch (erreur) { echouer(erreur) }
    })
    rendre(await appeler<EtatRacine>('Racine.Ouvrir'))
  } catch (erreur) {
    echouer(erreur)
  }
  return { arreter }
}
