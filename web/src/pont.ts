// LE PONT : le SEUL fichier de web/src qui parle au C#.
//
// Deux sens, un seul endroit :
//  - React -> C# : appeler<T>(nom, ...args) joint une methode statique
//    [JSInvokable("<NomEcran>.<Methode>")] de Pont/<NomEcran>.cs, qui delegue
//    a Metier/ ;
//  - React -> Blazor : rappelBlazor(reference, nom) fabrique la fonction qui
//    remonte un geste (ex. « ouvrir ce dossier ») a la coquille .razor, par
//    la reference que Components/IleReact.razor a passee au montage.
//
// Pourquoi un seul fichier : au lot 9, Blazor part et la WebView est pilotee
// directement. Ce fichier est alors le SEUL a changer cote React (le transport
// devient chrome.webview.postMessage), les ecrans n'en savent rien.
// Garde-fou : src/regles.test.ts echoue si un autre fichier appelle DotNet.
//
// Generique par construction : aucune liste d'ecrans ici. Un ecran porte
// ajoute sa methode dans SON Pont/<NomEcran>.cs, jamais dans ce fichier.

/** Le nom de l'assemblage .NET qui porte les methodes [JSInvokable]. */
export const ASSEMBLAGE = 'RigV3Desktop'

/** Ce que blazor.webview.js installe en global (DotNet). */
interface DotNetGlobal {
  invokeMethodAsync<T>(assemblage: string, methode: string, ...args: unknown[]): Promise<T>
}

/**
 * Une DotNetObjectReference recue du C# (ici : l'ile qui a monte l'ecran).
 * Un rappel ne rend rien d'utile a React : la reponse reste `unknown`.
 */
export interface ReferenceBlazor {
  invokeMethodAsync(methode: string, ...args: unknown[]): Promise<unknown>
}

function dotnet(): DotNetGlobal {
  const d = (globalThis as { DotNet?: DotNetGlobal }).DotNet
  if (!d) {
    throw new Error('Pont indisponible : DotNet est absent (la page n\'est pas servie par la fenetre RIG).')
  }
  return d
}

/**
 * Appelle la methode C# `nom` (identifiant [JSInvokable] « <NomEcran>.<Methode> »)
 * et rend sa reponse, deja convertie depuis le JSON (proprietes en camelCase).
 */
export function appeler<T>(nom: string, ...args: unknown[]): Promise<T> {
  try {
    return dotnet().invokeMethodAsync<T>(ASSEMBLAGE, nom, ...args)
  } catch (erreur) {
    return Promise.reject(erreur)
  }
}

/**
 * La fonction qui remonte le rappel `nom` a la coquille Blazor
 * (IleReact.Rappeler). La valeur absente part en null : le C# la lit comme
 * un JSON null, jamais comme un argument manquant.
 */
export function rappelBlazor(reference: ReferenceBlazor, nom: string): (valeur?: unknown) => Promise<unknown> {
  return (valeur?: unknown) => reference.invokeMethodAsync('Rappeler', nom, valeur ?? null)
}
