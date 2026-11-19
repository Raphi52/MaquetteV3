// LE PONT : le SEUL fichier de web/src qui parle au C#.
//
// Transport : la messagerie de WebView2 (window.chrome.webview), depuis le
// retrait de Blazor (lot 3 du plan « finir le passage a React », 19/11/2026).
// Le cote C# est Pont/Repartiteur.cs :
//   page -> C#  { appel: n, nom: '<NomEcran>.<Methode>', args: [...] }
//   C# -> page  { reponse: n, valeur } ou { reponse: n, erreur }
//   C# -> page  { evenement: 'racine', valeur }   (sans demande : la racine a change)
//
// Trois usages, un seul endroit :
//  - React -> C# : appeler<T>(nom, ...args) joint une methode statique
//    [JSInvokable("<NomEcran>.<Methode>")] de Pont/<NomEcran>.cs, qui delegue
//    a Metier/ ;
//  - React -> racine de la fenetre : rappel(nom) fabrique la fonction qui
//    remonte un geste (ex. « ouvrir ce dossier ») a la racine C# de la fenetre
//    (Pont/Racines.cs) ;
//  - C# -> React : ecouter(evenement, ...) recoit ce que le C# envoie de
//    lui-meme (la racine redessinee apres un minuteur, une autre fenetre).
//
// Garde-fou : src/regles.test.ts echoue si un autre fichier touche a
// chrome.webview. Generique par construction : aucune liste d'ecrans ici. Un
// ecran porte ajoute sa methode dans SON Pont/<NomEcran>.cs, jamais ici.

/** Ce que WebView2 installe dans la page (window.chrome.webview). */
export interface MessagerieWebView {
  postMessage(message: unknown): void
  addEventListener(type: 'message', ecouteur: (e: { data: unknown }) => void): void
  removeEventListener(type: 'message', ecouteur: (e: { data: unknown }) => void): void
}

interface MessageDuCSharp {
  reponse?: number
  valeur?: unknown
  erreur?: string
  evenement?: string
}

interface AppelEnAttente {
  nom: string
  resoudre: (valeur: unknown) => void
  rejeter: (erreur: Error) => void
}

let branchee: MessagerieWebView | undefined
let numero = 0
const enAttente = new Map<number, AppelEnAttente>()
const ecouteurs = new Map<string, Set<(valeur: unknown) => void>>()

function recevoir(e: { data: unknown }): void {
  const message = e.data as MessageDuCSharp | null
  if (!message || typeof message !== 'object') return
  if (typeof message.reponse === 'number') {
    const appel = enAttente.get(message.reponse)
    if (!appel) return
    enAttente.delete(message.reponse)
    if (typeof message.erreur === 'string') appel.rejeter(new Error(`${appel.nom} : ${message.erreur}`))
    else appel.resoudre(message.valeur)
    return
  }
  if (typeof message.evenement === 'string') {
    for (const ecouteur of ecouteurs.get(message.evenement) ?? []) ecouteur(message.valeur)
  }
}

/**
 * La messagerie de la fenetre, branchee a sa premiere utilisation. Une
 * messagerie remplacee (tests) est rebranchee : les reponses arrivent toujours
 * par celle qui a porte l'appel.
 */
function messagerie(): MessagerieWebView {
  const webview = (globalThis as { chrome?: { webview?: MessagerieWebView } }).chrome?.webview
  if (!webview) {
    throw new Error('Pont indisponible : chrome.webview est absent (la page n\'est pas servie par une fenetre RIG).')
  }
  if (webview !== branchee) {
    branchee?.removeEventListener('message', recevoir)
    webview.addEventListener('message', recevoir)
    branchee = webview
  }
  return webview
}

/**
 * Appelle la methode C# `nom` (identifiant [JSInvokable] « <NomEcran>.<Methode> »)
 * et rend sa reponse, deja convertie depuis le JSON (proprietes en camelCase).
 * Une valeur `undefined` part en null, comme JSON.stringify l'ecrit.
 */
export function appeler<T>(nom: string, ...args: unknown[]): Promise<T> {
  try {
    const webview = messagerie()
    const appel = ++numero
    return new Promise<T>((resoudre, rejeter) => {
      enAttente.set(appel, { nom, resoudre: resoudre as (valeur: unknown) => void, rejeter })
      try {
        webview.postMessage({ appel, nom, args: args.map((a) => a ?? null) })
      } catch (erreur) {
        enAttente.delete(appel)
        rejeter(erreur instanceof Error ? erreur : new Error(String(erreur)))
      }
    })
  } catch (erreur) {
    return Promise.reject(erreur)
  }
}

/**
 * Recoit ce que le C# envoie sans demande (`evenement`). Rend la fonction qui
 * cesse l'ecoute.
 */
export function ecouter(evenement: string, ecouteur: (valeur: unknown) => void): () => void {
  messagerie()
  let liste = ecouteurs.get(evenement)
  if (!liste) ecouteurs.set(evenement, (liste = new Set()))
  liste.add(ecouteur)
  return () => { liste.delete(ecouteur) }
}

/**
 * La fonction qui remonte le geste `nom` a la racine C# de la fenetre
 * (RacineWeb.Rappeler). La valeur absente part en null : le C# la lit comme
 * un JSON null, jamais comme un argument manquant.
 */
export function rappel(nom: string): (valeur?: unknown) => Promise<unknown> {
  return (valeur?: unknown) => appeler('Racine.Rappeler', nom, valeur ?? null)
}
