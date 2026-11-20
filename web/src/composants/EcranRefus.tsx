// L'ECRAN DE REFUS D'OUVERTURE (SOC-001, CA03 a CA06), 2026-11-20.
//
// Porte de la boite d'erreur de PROC_ACCUEIL (CheckUser, RigConsoleAccueil.cs:113-133
// de RigClientAccueil et du module PROC_ACCUEIL). C'est une RACINE de fenetre, pas
// un ecran du plan de travail -- d'ou sa place dans composants/, a cote
// d'Application.tsx : il est monte A LA PLACE de l'application (Pont/EcranRefus.cs,
// RacineRefus) quand le serveur n'admet pas la session Windows de l'agent ;
// l'accueil n'est jamais ouvert, aucune donnee n'est montree. Il dit les trois
// choses de la boite de la V2 : le compte, la raison, vers qui se tourner -- puis,
// comme elle, il ne propose que de fermer RIG.
// Aucune saisie : ni identifiant ni mot de passe (CA08).
//
// Style de la maquette : une carte sur le fond de la page, ombre de la charte,
// jetons --rig-* (theme clair et sombre). Le haut de la carte deplace la
// fenetre, qui n'a pas de barre de titre native (WindowStyle=None).

import type { CSSProperties } from 'react'
import { ThemeRig } from './ThemeRig'

/** L'identite refusee (Contrats/Socle/IdentiteAgent.cs, en camelCase). */
export interface IdentiteRefusee {
  statut: string
  compte: string
  message: string | null
  assistance: string | null
}

/** Le titre de chaque refus : les quatre de la V2, dans leur ordre, puis la panne. */
const TITRES: Record<string, string> = {
  inconnu: 'Compte inconnu de RIG',
  'sans-droit': 'Aucune habilitation',
  desactive: 'Compte désactivé',
  'sans-service': 'Aucun service rattaché',
  'serveur-injoignable': 'Serveur de RIG injoignable',
}

export function titreRefus(statut: string): string {
  return TITRES[statut] ?? 'Ouverture refusée'
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
  width: 'min(520px, calc(100vw - 48px))',
  background: 'var(--rig-surface)',
  border: '1px solid var(--rig-bord)',
  borderTop: '4px solid var(--rig-erreur)',
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
}

const PASTILLE: CSSProperties = {
  width: 44,
  height: 44,
  borderRadius: '50%',
  display: 'grid',
  placeItems: 'center',
  color: 'var(--rig-erreur)',
  background: 'color-mix(in srgb, var(--rig-erreur) 14%, transparent)',
}

const COMPTE: CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  gap: 'var(--rig-esp-2)',
  marginTop: 'var(--rig-esp-4)',
  padding: '8px 12px',
  borderRadius: 8,
  background: 'var(--rig-fond-2)',
  border: '1px solid var(--rig-bord-faible)',
}

const ASSISTANCE: CSSProperties = {
  marginTop: 'var(--rig-esp-4)',
  paddingLeft: 12,
  borderLeft: '3px solid var(--rig-or)',
  color: 'var(--rig-texte-2)',
}

const PIED: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  padding: '12px var(--rig-esp-5)',
  borderTop: '1px solid var(--rig-bord-faible)',
}

export function EcranRefus({ refus, theme, fermer, deplacer }: {
  refus: IdentiteRefusee
  theme: string
  fermer: () => void
  deplacer: () => void
}) {
  return (
    <div className="rig-refus" style={PAGE}>
      <ThemeRig theme={theme} />
      <section className="rig-refus__carte" style={CARTE} aria-labelledby="rig-refus-titre">
        <div className="rig-refus__tete" style={TETE} onMouseDown={(e) => { if (e.button === 0) deplacer() }}>
          <span className="rig-kicker">RIG · ouverture de session</span>
          <span className="rig-kicker">Session Windows</span>
        </div>
        <div style={CORPS}>
          <div style={PASTILLE} aria-hidden="true">
            <svg viewBox="0 0 24 24" width="22" height="22">
              <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="2" />
              <path d="M5.6 5.6l12.8 12.8" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
          </div>
          <div>
            <h1 id="rig-refus-titre" className="rig-titre" style={{ margin: 0, fontWeight: 600 }}>
              {titreRefus(refus.statut)}
            </h1>
            <p className="rig-refus__message" role="alert" style={{ margin: '6px 0 0', lineHeight: 1.45 }}>
              {refus.message}
            </p>
            <div className="rig-refus__compte" style={COMPTE}>
              <span className="rig-kicker">Compte Windows</span>
              <b style={{ fontFamily: 'var(--rig-font-mono)' }}>{refus.compte}</b>
            </div>
            {refus.assistance && (
              <p className="rig-refus__assistance" style={ASSISTANCE}>{refus.assistance}</p>
            )}
          </div>
        </div>
        <div style={PIED}>
          <button type="button" className="rig-bouton rig-bouton--fort" onClick={fermer} autoFocus>
            Fermer RIG
          </button>
        </div>
      </section>
    </div>
  )
}
