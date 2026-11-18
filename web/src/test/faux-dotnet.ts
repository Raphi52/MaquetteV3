// Un faux DotNet (ce que blazor.webview.js installe dans la vraie fenetre) pour
// les tests : chaque appel est note, et la reponse vient d'une table
// <identifiant [JSInvokable]> -> fonction. Un appel non prevu echoue fort,
// plutot que de rendre undefined et laisser l'ecran vide sans le dire.

import { vi } from 'vitest'

type Reponse = (...args: unknown[]) => unknown

export interface FauxDotNet {
  invokeMethodAsync: ReturnType<typeof vi.fn<(assemblage: string, methode: string, ...args: unknown[]) => Promise<unknown>>>
}

export function installerFauxDotNet(reponses: Record<string, Reponse>): FauxDotNet {
  const invokeMethodAsync = vi.fn(async (assemblage: string, methode: string, ...args: unknown[]) => {
    if (assemblage !== 'RigV3Desktop') throw new Error(`Assemblage inattendu : ${assemblage}`)
    const reponse = reponses[methode]
    if (!reponse) throw new Error(`Methode C# non prevue par le test : ${methode}`)
    return reponse(...args)
  })
  const faux: FauxDotNet = { invokeMethodAsync }
  ;(globalThis as { DotNet?: unknown }).DotNet = faux
  return faux
}

export function retirerFauxDotNet(): void {
  delete (globalThis as { DotNet?: unknown }).DotNet
}
