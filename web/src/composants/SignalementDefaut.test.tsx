import { afterEach, describe, expect, it } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { demanderSignalement, estToucheSignalement, SignalementDefaut, type ContexteSignalement, type ResultatSignalement } from './SignalementDefaut'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Ce qui part sans saisie (ecran, greffe, agent, version, heure, derniere erreur) est pris par Pont/EcranSignalement.cs ;
// l'identifiant de suivi est donne par le serveur (tests/SocleBetaCheck, SOC-022 CA06). Ici : le geste et ce que voit l'agent.
function contexte(partiel: Partial<ContexteSignalement> = {}): ContexteSignalement {
  return {
    mode: 'serveur', ecran: 'Compteurs de numéros', codeEcran: 'PROC_COMPTEURS_NUM', codeGreffe: '4302', greffe: 'Le Puy (4302)',
    agent: 'Jeanne Dupont', version: '2026.1.0', heure: '21/11/2026 14:32', derniereErreur: 'Q3VZ-81HD', ...partiel,
  }
}

function resultat(partiel: Partial<ResultatSignalement> = {}): ResultatSignalement {
  return {
    envoye: true, suivi: 'H8RZ-4K2Q', contexte: contexte(),
    message: 'Défaut signalé à Amitel. Identifiant de suivi H8RZ-4K2Q : donnez-le si l\'assistance d\'Amitel vous rappelle.', ...partiel,
  }
}

describe('SignalementDefaut', () => {
  it('SOC-022 CA06 : le bouton de l\'en-tete ouvre la boite sur l\'ecran ouvert, avec ce qui part sans saisie', async () => {
    const pont = installerFauxPont({ 'EcranSignalement.Preparer': () => contexte() })
    render(<SignalementDefaut ecran={{ nom: 'Compteurs de numéros', code: 'PROC_COMPTEURS_NUM' }} />)
    expect(screen.queryByRole('dialog')).toBeNull()
    act(() => demanderSignalement())
    const boite = await screen.findByRole('dialog', { name: 'Signaler un défaut' })
    expect(pont.appels).toHaveBeenCalledWith('EcranSignalement.Preparer', 'Compteurs de numéros', 'PROC_COMPTEURS_NUM')
    const part = screen.getByLabelText('Ce qui part avec votre phrase').textContent!
    for (const t of ['Compteurs de numéros', 'Le Puy (4302)', 'Jeanne Dupont', '2026.1.0', '21/11/2026 14:32', 'Q3VZ-81HD']) expect(part).toContain(t)
    expect((screen.getByRole('button', { name: 'Envoyer à Amitel' }) as HTMLButtonElement).disabled).toBe(true)
    expect(boite).toBeTruthy()
  })

  it('SOC-022 CA06 : l\'agent ecrit une phrase, envoie, et lit l\'identifiant de suivi', async () => {
    const pont = installerFauxPont({ 'EcranSignalement.Preparer': () => contexte(), 'EcranSignalement.Envoyer': () => resultat() })
    render(<SignalementDefaut ecran={{ nom: 'Compteurs de numéros', code: 'PROC_COMPTEURS_NUM' }} />)
    act(() => demanderSignalement())
    fireEvent.change(await screen.findByLabelText('Ce que vous avez constaté, en une phrase'),
      { target: { value: 'Le numéro rendu ne réapparaît pas.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Envoyer à Amitel' }))
    expect((await screen.findByLabelText('Identifiant de suivi')).textContent).toBe('H8RZ-4K2Q')
    expect(screen.getByRole('status').textContent).toBe('Défaut signalé')
    expect(pont.appels).toHaveBeenCalledWith('EcranSignalement.Envoyer',
      { phrase: 'Le numéro rendu ne réapparaît pas.', ecran: 'Compteurs de numéros', codeEcran: 'PROC_COMPTEURS_NUM' })
    fireEvent.click(screen.getByRole('button', { name: 'Fermer' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('SOC-022 CA06 : Ctrl Maj D ouvre la boite de partout, meme dans un champ ; Echap la ferme seule', async () => {
    installerFauxPont({ 'EcranSignalement.Preparer': () => contexte({ ecran: 'Accueil', codeEcran: null, derniereErreur: null }) })
    render(<><input aria-label="un champ" /><SignalementDefaut ecran={{ nom: 'Accueil', code: null }} /></>)
    fireEvent.keyDown(screen.getByLabelText('un champ'), { key: 'D', ctrlKey: true, shiftKey: true })
    expect(await screen.findByRole('dialog')).toBeTruthy()
    expect(screen.getByLabelText('Ce qui part avec votre phrase').textContent).toContain('aucune dans cette session')
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('sans serveur, rien ne part : la boite le dit et ne montre aucun identifiant', async () => {
    installerFauxPont({
      'EcranSignalement.Preparer': () => contexte({ mode: 'demo' }),
      'EcranSignalement.Envoyer': () => resultat({ envoye: false, suivi: null, contexte: contexte({ mode: 'demo' }),
        message: 'Démonstration : sans serveur de RIG, le défaut ne part pas chez Amitel (aucun identifiant de suivi).' }),
    })
    render(<SignalementDefaut ecran={{ nom: 'Accueil', code: null }} />)
    act(() => demanderSignalement())
    fireEvent.change(await screen.findByLabelText('Ce que vous avez constaté, en une phrase'), { target: { value: 'essai' } })
    fireEvent.click(screen.getByRole('button', { name: 'Envoyer à Amitel' }))
    expect((await screen.findByRole('alert')).textContent).toContain('ne part pas chez Amitel')
    expect(screen.queryByLabelText('Identifiant de suivi')).toBeNull()
  })

  it('la touche : Ctrl (ou Cmd) + Maj + D, sans Alt (AltGr), pas une touche tenue', () => {
    const t = (p: Partial<KeyboardEvent>) => estToucheSignalement({ ctrlKey: false, metaKey: false, shiftKey: false, altKey: false, key: '', repeat: false, ...p })
    expect(t({ ctrlKey: true, shiftKey: true, key: 'D' })).toBe(true)
    expect(t({ metaKey: true, shiftKey: true, key: 'd' })).toBe(true)
    expect(t({ ctrlKey: true, key: 'd' })).toBe(false)
    expect(t({ ctrlKey: true, shiftKey: true, altKey: true, key: 'D' })).toBe(false)
    expect(t({ ctrlKey: true, shiftKey: true, key: 'D', repeat: true })).toBe(false)
  })
})
