// Un faux pont pour les tests : la messagerie de WebView2 (window.chrome.webview)
// que la vraie fenetre installe, avec le C# (Pont/Repartiteur.cs) simule
// derriere. Chaque appel est note, et la reponse vient d'une table
// <identifiant [JSInvokable]> -> fonction. Un appel non prevu echoue fort,
// plutot que de rendre undefined et laisser l'ecran vide sans le dire.
//
// Le VRAI transport de src/pont.ts est joue : message poste, aller-retour en
// JSON (comme WebView2), reponse rendue par numero d'appel, evenements sans
// demande (pousser).

import { vi } from 'vitest'

type Reponse = (...args: unknown[]) => unknown
type Ecouteur = (e: { data: unknown }) => void

export interface FauxPont {
  /** Chaque appel recu par le « C# », (identifiant, ...arguments), dans l'ordre. */
  appels: ReturnType<typeof vi.fn<(nom: string, ...args: unknown[]) => unknown>>
  /** Envoie a la page un message sans demande, comme Repartiteur.Pousser. */
  pousser(evenement: string, valeur: unknown): void
}

/** Ce que JSON.stringify puis JSON.parse rendent : ce que traverse la messagerie. */
function enJson<T>(valeur: T): T {
  return valeur === undefined ? (null as T) : (JSON.parse(JSON.stringify(valeur)) as T)
}

export function installerFauxPont(reponses: Record<string, Reponse>): FauxPont {
  const ecouteurs = new Set<Ecouteur>()
  const envoyer = (data: unknown): void => { for (const e of [...ecouteurs]) e({ data }) }

  const appels = vi.fn((nom: string, ...args: unknown[]): unknown => {
    const reponse = reponses[nom]
    if (!reponse) throw new Error(`Methode C# non prevue par le test : ${nom}`)
    return reponse(...args)
  })

  const webview = {
    postMessage(message: unknown): void {
      const { appel, nom, args } = enJson(message) as { appel: number; nom: string; args: unknown[] }
      let resultat: Promise<unknown>
      try {
        resultat = Promise.resolve(appels(nom, ...args))
      } catch (erreur) {
        resultat = Promise.reject(erreur)
      }
      void resultat.then(
        (valeur) => envoyer({ reponse: appel, valeur: enJson(valeur) }),
        (erreur: unknown) => envoyer({ reponse: appel, erreur: erreur instanceof Error ? erreur.message : String(erreur) }),
      )
    },
    addEventListener(_type: 'message', ecouteur: Ecouteur): void { ecouteurs.add(ecouteur) },
    removeEventListener(_type: 'message', ecouteur: Ecouteur): void { ecouteurs.delete(ecouteur) },
  }
  ;(globalThis as { chrome?: unknown }).chrome = { webview }

  return {
    appels,
    pousser: (evenement, valeur) => envoyer({ evenement, valeur: enJson(valeur) }),
  }
}

export function retirerFauxPont(): void {
  delete (globalThis as { chrome?: unknown }).chrome
}
