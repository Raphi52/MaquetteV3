// SIGNALER UN DEFAUT A AMITEL, EN UN GESTE (SOC-022 CA06, T03 ; lot 17 du plan « Reconstruction de RIG — phase 2 »,
// 21/11/2026). Sans origine RIG v2 : l'ancien RIG n'a pas de signalement, un agent appelle l'assistance.
//
// Le geste : le bouton « Signaler un défaut » de l'en-tete de l'accueil (web/src/accueil/Entete.tsx, qui envoie
// EVENEMENT_SIGNALER), ou Ctrl Maj D de partout -- meme dans un champ (la touche est dans la fiche « Ancienne touche,
// nouvelle touche », Pont/EcranCorrespondances.cs). La boite montre ce qui part SANS SAISIE -- l'ecran ouvert, le greffe,
// l'agent, la version, l'heure, la derniere erreur suivie (Pont/EcranSignalement.cs) -- et l'agent n'ecrit qu'une phrase.
// Envoyee, elle montre l'identifiant de suivi donne par le serveur (le mecanisme des erreurs, X-Rig-Suivi) ; Amitel le
// retrouve dans l'ecran Supervision. Sans serveur, rien ne part et la boite le dit : aucun identifiant invente.
// Montee une fois par fenetre, par Application.tsx, qui lui donne l'ecran ouvert (onglet, page du dossier ou accueil).

import { useCallback, useEffect, useRef, useState } from 'react'
import type { MouseEvent } from 'react'
import { appeler } from '../pont'
import { ecouterEchapDabord } from '../plan/clavier'

export interface ContexteSignalement {
  mode: 'serveur' | 'demo'
  ecran: string
  codeEcran: string | null
  codeGreffe: string | null
  greffe: string | null
  agent: string
  version: string
  heure: string
  derniereErreur: string | null
}

export interface ResultatSignalement {
  envoye: boolean
  suivi: string | null
  message: string
  contexte: ContexteSignalement
}

/** L'ecran d'ou l'agent signale : son nom tel qu'il le voit, et son code dans la V3 (null pour l'accueil). */
export interface EcranOuvert {
  nom: string
  code: string | null
}

/** L'evenement qui ouvre la boite (le bouton de l'en-tete l'envoie). */
export const EVENEMENT_SIGNALER = 'rig-signaler-defaut'

/** La plus longue phrase que le serveur accepte (RoutesSupervision.PhraseAuPlus). */
export const PHRASE_AU_PLUS = 1000

/** Ouvre la boite du signalement sur l'ecran ouvert (le bouton « Signaler un défaut » de l'en-tete). */
export function demanderSignalement(cible: EventTarget = window): void {
  cible.dispatchEvent(new Event(EVENEMENT_SIGNALER))
}

/** Ctrl Maj D (« Ctrl Maj D » de la fiche des touches) : de partout, meme dans un champ ; une touche tenue ne rouvre pas. */
export function estToucheSignalement(e: Pick<KeyboardEvent, 'ctrlKey' | 'metaKey' | 'shiftKey' | 'altKey' | 'key' | 'repeat'>): boolean {
  return (e.ctrlKey || e.metaKey) && e.shiftKey && !e.altKey && !e.repeat && e.key.toLowerCase() === 'd'
}

function retenir(e: MouseEvent): void { e.stopPropagation() }

const Ligne = { display: 'grid', gridTemplateColumns: '120px minmax(0, 1fr)', gap: 'var(--rig-esp-2)', margin: 0 } as const
const Libelle = { color: 'var(--rig-texte-3)', fontSize: 'var(--rig-fs-dense)' } as const
const Valeur = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const Aide = { margin: 0, fontSize: 'var(--rig-fs-dense)', color: 'var(--rig-texte-2)' } as const
const Suivi = { fontFamily: 'var(--rig-font-mono)', fontSize: '22px', fontWeight: 700, letterSpacing: '2px' } as const

export function SignalementDefaut({ ecran }: { ecran: EcranOuvert }) {
  const [contexte, setContexte] = useState<ContexteSignalement | null>(null)
  const [phrase, setPhrase] = useState('')
  const [resultat, setResultat] = useState<ResultatSignalement | null>(null)
  const [occupe, setOccupe] = useState(false)
  const ecranCourant = useRef(ecran)
  ecranCourant.current = ecran

  const ouvrir = useCallback(() => {
    const e = ecranCourant.current
    setResultat(null)
    setPhrase('')
    void appeler<ContexteSignalement>('EcranSignalement.Preparer', e.nom, e.code).then(setContexte)
  }, [])
  const fermer = useCallback(() => { setContexte(null); setResultat(null) }, [])

  useEffect(() => {
    const surDemande = () => ouvrir()
    const surTouche = (e: KeyboardEvent) => {
      if (e.defaultPrevented || !estToucheSignalement(e)) return
      e.preventDefault()
      ouvrir()
    }
    window.addEventListener(EVENEMENT_SIGNALER, surDemande)
    window.addEventListener('keydown', surTouche)
    return () => {
      window.removeEventListener(EVENEMENT_SIGNALER, surDemande)
      window.removeEventListener('keydown', surTouche)
    }
  }, [ouvrir])

  // Echap ferme la boite seule : l'ecran ou l'affaire dessous restent ouverts.
  const ouverte = contexte !== null
  useEffect(() => (ouverte ? ecouterEchapDabord(fermer) : undefined), [ouverte, fermer])

  if (!contexte) return null
  const c = resultat?.contexte ?? contexte
  const envoyer = async () => {
    setOccupe(true)
    try {
      setResultat(await appeler<ResultatSignalement>('EcranSignalement.Envoyer', { phrase, ecran: c.ecran, codeEcran: c.codeEcran }))
    } finally { setOccupe(false) }
  }
  const part: [string, string][] = [
    ['Écran', c.ecran],
    ['Greffe', c.greffe ?? 'aucun greffe ouvert'],
    ['Agent', c.agent],
    ['Version', c.version],
    ['Heure', c.heure],
    ['Dernière erreur', c.derniereErreur ?? 'aucune dans cette session'],
  ]

  return (
    <div className="rig-voile rig-voile--haut" onClick={fermer}>
      <div className="rig-impression rig-impression--large" role="dialog" aria-label="Signaler un défaut" onClick={retenir}>
        <div className="rig-kicker">Signaler un défaut à Amitel</div>

        <div aria-label="Ce qui part avec votre phrase" style={{ display: 'grid', gap: '2px' }}>
          {part.map(([libelle, valeur]) => (
            <p key={libelle} style={Ligne}>
              <span style={Libelle}>{libelle}</span>
              <span style={Valeur} title={valeur} className={libelle === 'Dernière erreur' && c.derniereErreur ? 'rig-table__mono' : undefined}>{valeur}</span>
            </p>
          ))}
        </div>

        {resultat?.envoye ? (
          <>
            <p style={{ margin: 0 }} role="status">
              <span className="rig-etat rig-etat--ok">Défaut signalé</span>
            </p>
            <p style={{ margin: 0 }}>
              <span style={Libelle}>{'Identifiant de suivi '}</span>
              <span style={Suivi} aria-label="Identifiant de suivi">{resultat.suivi}</span>
            </p>
            <p style={Aide}>{resultat.message}</p>
            <div className="rig-impression__pied">
              <button className="rig-bouton rig-bouton--fort" onClick={fermer}>Fermer</button>
            </div>
          </>
        ) : (
          <>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Ce que vous avez constaté, en une phrase</span>
              <textarea className="rig-saisie-champ__saisie" rows={3} maxLength={PHRASE_AU_PLUS} value={phrase} autoFocus
                        onChange={(e) => setPhrase(e.target.value)} />
            </label>
            {c.mode === 'demo' && (
              <p style={Aide}>
                <span className="rig-etat rig-etat--attente">Démonstration</span>
                {' '}Sans serveur de RIG, le défaut ne part pas chez Amitel.
              </p>
            )}
            {resultat && !resultat.envoye && (
              <p style={Aide} role="alert">
                <span className="rig-etat rig-etat--alerte">Pas envoyé</span>{' '}{resultat.message}
              </p>
            )}
            <div className="rig-impression__pied">
              <button className="rig-bouton rig-bouton--fort" disabled={occupe || phrase.trim().length === 0}
                      onClick={() => void envoyer()}>Envoyer à Amitel</button>{' '}
              <button className="rig-bouton" onClick={fermer}>Annuler</button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
