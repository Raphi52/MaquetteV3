// ECRANS A SESSION : l'etat de l'ecran vit dans son pont C#, React l'affiche.
//
// Pourquoi (lot 2, 18/11/2026, Requisitions et Commandes web) : l'etat de ces
// ecrans est fait d'objets de Metier/ que Metier/ modifie lui-meme
// (TraitementCommandes.Appliquer valide une CommandeRecue, CommandeRequi
// calcule sa fraicheur, CommandeWeb passe « Traitee »), et presque chaque
// valeur affichee sort d'une regle de Metier/. Recopier cet etat dans React
// aurait recopie les regles. Le @code de l'ecran Blazor est donc deplace TEL
// QUEL dans une classe de session de Pont/<Ecran>.cs ; React ne tient que la
// VUE que la session lui rend, et lui remonte chaque geste.
//
// Le contrat d'un pont a session (trois methodes [JSInvokable]) :
//   <Ecran>.Ouvrir()                     -> la vue, avec l'identifiant de session
//   <Ecran>.Agir(session, geste, valeur) -> la vue apres le geste
//   <Ecran>.Fermer(session)              -> la session est oubliee
//
// Ce module ne parle pas au C# lui-meme : chaque ecran lui passe ses propres
// appels a pont.ts (regles.test.ts verifie qu'un ecran n'appelle que SON pont).

import { useCallback, useEffect, useRef, useState, type InputHTMLAttributes } from 'react'

/** Toute vue rendue par un pont a session porte l'identifiant de sa session. */
export interface VueDeSession {
  session: string
}

/** Les trois appels du pont d'un ecran a session (voir l'en-tete). */
export interface PontDeSession<V extends VueDeSession> {
  ouvrir: () => Promise<V>
  agir: (session: string, geste: string, valeur: unknown) => Promise<V>
  fermer: (session: string) => Promise<unknown>
}

/** Remonte un geste a la session : le nom du geste et sa valeur (absente = null). */
export type Geste = (geste: string, valeur?: unknown) => void

function signaler(erreur: unknown): void {
  console.error('[pont] appel en echec', erreur)
}

/**
 * Ouvre une session au montage, la ferme au demontage, et rend [vue, geste].
 * `vue` reste null tant que le pont n'a pas repondu : l'ecran n'affiche alors
 * rien plutot qu'un etat faux.
 *
 * Seule la reponse du DERNIER geste est appliquee : les appels partent et sont
 * traites dans l'ordre, la derniere vue contient donc l'effet des precedents.
 * Une frappe rapide (« ab ») ne fait pas reapparaitre la vue de « a ».
 * `pont` doit etre une constante du module (sinon la session se rouvre a chaque rendu).
 */
export function useSession<V extends VueDeSession>(pont: PontDeSession<V>): [V | null, Geste] {
  const [vue, setVue] = useState<V | null>(null)
  const session = useRef<string | null>(null)
  const dernier = useRef(0)

  useEffect(() => {
    let demonte = false
    const numero = ++dernier.current
    pont.ouvrir().then((v) => {
      if (demonte) { pont.fermer(v.session).catch(signaler); return }
      session.current = v.session
      if (numero === dernier.current) setVue(v)
    }, signaler)
    return () => {
      demonte = true
      const s = session.current
      session.current = null
      if (s) pont.fermer(s).catch(signaler)
    }
  }, [pont])

  const geste = useCallback<Geste>((nom, valeur) => {
    const s = session.current
    if (!s) return
    const numero = ++dernier.current
    pont.agir(s, nom, valeur ?? null).then((v) => {
      if (numero === dernier.current && session.current === s) setVue(v)
    }, signaler)
  }, [pont])

  return [vue, geste]
}

type AttributsSaisie = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'defaultValue' | 'onChange' | 'onInput'>

export interface ProprietesSaisie extends AttributsSaisie {
  /** La valeur tenue par la session (texte, ou nombre / date deja mis en forme). */
  valeur: string
  /**
   * Quand la saisie remonte : 'input' a chaque frappe (le `@bind:event="oninput"`
   * de Blazor), 'change' a la sortie du champ ou sur Entree (le `@bind` seul).
   */
  quand?: 'input' | 'change'
  changer: (valeur: string) => void
}

/**
 * Un <input> dont la valeur vit dans la session, avec la regle de Blazor :
 * le champ n'est reecrit QUE quand la valeur de la session CHANGE. Une saisie
 * que la session refuse (un nombre illisible) reste donc affichee telle quelle,
 * et la frappe n'est jamais ecrasee par une reponse en retard.
 */
export function Saisie({ valeur, quand = 'input', changer, ...attributs }: ProprietesSaisie) {
  const champ = useRef<HTMLInputElement>(null)
  const changerCourant = useRef(changer)

  useEffect(() => { changerCourant.current = changer }, [changer])

  useEffect(() => {
    const el = champ.current
    if (el && el.value !== valeur) el.value = valeur
  }, [valeur])

  // L'evenement « change » du navigateur : React n'en donne pas (son onChange
  // suit chaque frappe). C'est celui que le `@bind` de Blazor ecoutait.
  useEffect(() => {
    const el = champ.current
    if (!el || quand !== 'change') return
    const surChange = () => changerCourant.current(el.value)
    el.addEventListener('change', surChange)
    return () => el.removeEventListener('change', surChange)
  }, [quand])

  return (
    <input ref={champ} defaultValue={valeur} {...attributs}
           onInput={quand === 'input' ? (e) => changer(e.currentTarget.value) : undefined} />
  )
}
