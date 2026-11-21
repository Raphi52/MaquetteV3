// L'ECRAN DE CHOIX DU GREFFE A L'OUVERTURE DE RIG (lot 10 du plan de reconstruction, 2026-11-21).
//
// Le pendant de la fenetre de connexion de la V2 (RigFormLogin, RigFormLogin.cs:282-319) : quand
// plusieurs greffes de ce poste connaissent l'agent, il choisit celui qu'il ouvre. C'est une RACINE de
// fenetre, comme l'ecran de refus (EcranRefus.tsx) -- d'ou sa place dans composants/ : montee a la place
// de l'application (Pont/RacineChoixGreffe.cs), elle laisse la place a l'application (agent admis dans
// ce greffe) ou a l'ecran de refus, sans rechargement de la page.
// La V2 montre une liste deroulante, OK et Annuler ; ici une liste ou l'on choisit d'un clic, du clavier
// (fleches, Entree) ou d'un double clic, puis « Ouvrir le greffe » ; « Fermer RIG » est l'Annuler de la V2.
// Aucune saisie : ni identifiant ni mot de passe (SOC-001 CA08).
//
// Style de la maquette : la carte de l'ecran de refus, filet d'accent au lieu du filet d'erreur ;
// jetons --rig-* (themes clair et sombre). Le haut de la carte deplace la fenetre, qui n'a pas de
// barre de titre native (WindowStyle=None).

import { useState, type CSSProperties, type KeyboardEvent } from 'react'
import { ThemeRig } from './ThemeRig'

/** Un greffe propose (Pont/RacineChoixGreffe.cs, GreffePropose). */
export interface GreffePropose {
  code: string
  nom: string
}

/** Un greffe du poste qui n'est pas propose, et pourquoi (GreffeNonPropose). */
export interface GreffeNonPropose {
  code: string
  raison: string
}

const PAGE: CSSProperties = {
  position: 'fixed',
  inset: 0,
  display: 'grid',
  placeItems: 'center',
  background: 'var(--rig-fond)',
  color: 'var(--rig-texte)',
  fontFamily: 'var(--rig-font)',
}

const CARTE: CSSProperties = {
  width: 'min(560px, calc(100vw - 48px))',
  maxHeight: 'calc(100vh - 48px)',
  display: 'flex',
  flexDirection: 'column',
  background: 'var(--rig-surface)',
  border: '1px solid var(--rig-bord)',
  borderTop: '4px solid var(--rig-accent)',
  borderRadius: 14,
  boxShadow: 'var(--rig-ombre)',
  overflow: 'hidden',
}

const TETE: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '10px var(--rig-esp-5)',
  borderBottom: '1px solid var(--rig-bord-faible)',
  cursor: 'default',
  userSelect: 'none',
}

const CORPS: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'auto 1fr',
  gap: 'var(--rig-esp-4)',
  padding: 'var(--rig-esp-5)',
  minHeight: 0,
  overflow: 'auto',
}

const PASTILLE: CSSProperties = {
  width: 44,
  height: 44,
  borderRadius: '50%',
  display: 'grid',
  placeItems: 'center',
  color: 'var(--rig-accent)',
  background: 'color-mix(in srgb, var(--rig-accent) 14%, transparent)',
}

const COMPTE: CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  gap: 'var(--rig-esp-2)',
  marginTop: 'var(--rig-esp-3)',
  padding: '6px 12px',
  borderRadius: 8,
  background: 'var(--rig-fond-2)',
  border: '1px solid var(--rig-bord-faible)',
}

const LISTE: CSSProperties = {
  display: 'grid',
  gap: 6,
  marginTop: 'var(--rig-esp-4)',
  outline: 'none',
}

function styleLigne(choisie: boolean): CSSProperties {
  return {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr',
    alignItems: 'center',
    gap: 'var(--rig-esp-3)',
    width: '100%',
    padding: '10px 12px',
    textAlign: 'left',
    font: 'inherit',
    color: 'var(--rig-texte)',
    cursor: 'pointer',
    borderRadius: 10,
    border: `1px solid ${choisie ? 'var(--rig-accent)' : 'var(--rig-bord-faible)'}`,
    background: choisie ? 'color-mix(in srgb, var(--rig-accent) 14%, transparent)' : 'var(--rig-fond-2)',
  }
}

const CODE: CSSProperties = {
  fontFamily: 'var(--rig-font-mono)',
  fontWeight: 700,
  padding: '2px 8px',
  borderRadius: 6,
  border: '1px solid var(--rig-bord)',
  background: 'var(--rig-surface)',
}

const NON_PROPOSES: CSSProperties = {
  marginTop: 'var(--rig-esp-4)',
  paddingLeft: 12,
  borderLeft: '3px solid var(--rig-or)',
  color: 'var(--rig-texte-2)',
}

const PIED: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: 'var(--rig-esp-3)',
  padding: '12px var(--rig-esp-5)',
  borderTop: '1px solid var(--rig-bord-faible)',
}

export function ChoixGreffe({ compte, greffes, nonProposes, theme, choisir, fermer, deplacer }: {
  compte: string
  greffes: GreffePropose[]
  nonProposes: GreffeNonPropose[]
  theme: string
  /** Ouvre le greffe : la promesse ne se resout qu'une fois l'agent juge dans sa base. */
  choisir: (code: string) => Promise<unknown> | void
  fermer: () => void
  deplacer: () => void
}) {
  const [choisi, setChoisi] = useState<string | undefined>(greffes[0]?.code)
  const [enCours, setEnCours] = useState<string | null>(null)
  const [erreur, setErreur] = useState<string | null>(null)

  const ouvrir = (code: string | undefined): void => {
    if (!code || enCours) return
    setEnCours(code)
    setErreur(null)
    // Admis ou refuse, la racine change et cet ecran disparait ; seule une panne du pont revient ici.
    Promise.resolve(choisir(code)).catch((e: unknown) => {
      setEnCours(null)
      setErreur(e instanceof Error ? e.message : String(e))
    })
  }

  const clavier = (e: KeyboardEvent<HTMLDivElement>): void => {
    const rang = greffes.findIndex((g) => g.code === choisi)
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const pas = e.key === 'ArrowDown' ? 1 : -1
      const suivant = greffes[Math.min(greffes.length - 1, Math.max(0, rang + pas))]
      if (suivant) setChoisi(suivant.code)
    } else if (e.key === 'Enter') {
      e.preventDefault()
      ouvrir(choisi)
    }
  }

  const nomEnCours = greffes.find((g) => g.code === enCours)?.nom

  return (
    <div className="rig-choix-greffe" style={PAGE}>
      <ThemeRig theme={theme} />
      <section className="rig-choix-greffe__carte" style={CARTE} aria-labelledby="rig-choix-greffe-titre">
        <div className="rig-choix-greffe__tete" style={TETE} onMouseDown={(e) => { if (e.button === 0) deplacer() }}>
          <span className="rig-kicker">RIG · ouverture de session</span>
          <span className="rig-kicker">Session Windows</span>
        </div>
        <div style={CORPS}>
          <div style={PASTILLE} aria-hidden="true">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8"
                 strokeLinecap="round" strokeLinejoin="round">
              <path d="M3 10l9-6 9 6" />
              <path d="M5 10v8M9.5 10v8M14.5 10v8M19 10v8" />
              <path d="M3 20h18" />
            </svg>
          </div>
          <div style={{ minWidth: 0 }}>
            <h1 id="rig-choix-greffe-titre" className="rig-titre" style={{ margin: 0, fontWeight: 600 }}>
              Choisissez votre greffe
            </h1>
            <p style={{ margin: '6px 0 0', lineHeight: 1.45, color: 'var(--rig-texte-2)' }}>
              Votre compte est déclaré dans {greffes.length} greffes de ce poste.
            </p>
            <div className="rig-choix-greffe__compte" style={COMPTE}>
              <span className="rig-kicker">Compte Windows</span>
              <b style={{ fontFamily: 'var(--rig-font-mono)' }}>{compte}</b>
            </div>
            <div className="rig-choix-greffe__liste" role="listbox" aria-label="Greffes de ce poste"
                 aria-activedescendant={choisi ? `rig-greffe-${choisi}` : undefined}
                 tabIndex={0} autoFocus style={LISTE} onKeyDown={clavier}>
              {greffes.map((g) => (
                <div key={g.code} id={`rig-greffe-${g.code}`} role="option" aria-selected={g.code === choisi}
                     className={'rig-choix-greffe__greffe' + (g.code === choisi ? ' rig-choix-greffe__greffe--choisi' : '')}
                     style={styleLigne(g.code === choisi)}
                     onClick={() => setChoisi(g.code)} onDoubleClick={() => ouvrir(g.code)}>
                  <span style={CODE}>{g.code}</span>
                  <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{g.nom}</span>
                </div>
              ))}
            </div>
            {nonProposes.length > 0 && (
              <div className="rig-choix-greffe__non-proposes" style={NON_PROPOSES}>
                <span className="rig-kicker">Non proposés</span>
                {nonProposes.map((g) => (
                  <div key={g.code}><b style={{ fontFamily: 'var(--rig-font-mono)' }}>{g.code}</b>{' · '}{g.raison}</div>
                ))}
              </div>
            )}
            {(enCours || erreur) && (
              <p className="rig-choix-greffe__etat" role={erreur ? 'alert' : 'status'}
                 style={{ margin: 'var(--rig-esp-4) 0 0', color: erreur ? 'var(--rig-erreur)' : 'var(--rig-texte-2)' }}>
                {erreur ?? `Ouverture du greffe ${nomEnCours ?? enCours}…`}
              </p>
            )}
          </div>
        </div>
        <div style={PIED}>
          <button type="button" className="rig-bouton" onClick={fermer} disabled={enCours !== null}>
            Fermer RIG
          </button>
          <button type="button" className="rig-bouton rig-bouton--fort" onClick={() => ouvrir(choisi)}
                  disabled={!choisi || enCours !== null}>
            Ouvrir le greffe
          </button>
        </div>
      </section>
    </div>
  )
}
