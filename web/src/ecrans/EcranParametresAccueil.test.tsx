import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranParametresAccueil, SansEnregistrement, type VueParametresAccueil } from './EcranParametresAccueil'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de l'ecran Blazor (EcranParametresAccueil.razor) : 3 imprimantes
// cochees sur 4, une ligne sur deux coloriee, tri « Date de creation (Descendant) ».
const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

const Vue = (v: Partial<VueParametresAccueil>): VueParametresAccueil =>
  ({ mode: 'demo', message: null, valeurs: {}, estAdministrateur: false, estAmitel: false, agentV2: false, ...v })

/** L'ecran une fois le pont repondu (sans serveur par defaut : les reglages d'exemple, marques). */
async function ouvrir(v: VueParametresAccueil = Vue({})) {
  const pont = installerFauxPont({ 'EcranParametresAccueil.Reglages': () => v })
  const r = render(<EcranParametresAccueil />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranParametresAccueil.Reglages'))
  await vi.waitFor(() => expect(r.container.querySelector('.rig-eg-note')).not.toBeNull())
  return r
}

describe('EcranParametresAccueil', () => {
  it('affiche les reglages de depart et l\'apercu, marques « exemple » sans serveur', async () => {
    const { container } = await ouvrir()
    expect(container.querySelector('.rig-tcw__exemple')!.textContent).toBe('exemple')
    expect(compte(container)).toBe('3 imprimante(s) cochee(s)')
    expect(screen.getByRole('button', { name: 'Animations' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(screen.getByRole('button', { name: 'Ouvrir en plein ecran' }).className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Greffe - Accueil (recommande)' })).toBeTruthy()
    expect((screen.getByLabelText('Interval de defilement (secondes)') as HTMLInputElement).value).toBe('8')
    expect(container.querySelector('thead th')!.textContent).toBe('Date de creation (Descendant)')
    const lignes = [...container.querySelectorAll('tbody tr')] as HTMLElement[]
    expect(lignes).toHaveLength(4)
    expect(lignes.map((l) => l.style.background !== '')).toEqual([false, true, false, true])
  })

  it('cocher une imprimante, changer le tri, eteindre le coloriage', async () => {
    const { container } = await ouvrir()
    fireEvent.click(screen.getByRole('button', { name: /^Greffe - Archives/ }))
    expect(compte(container)).toBe('4 imprimante(s) cochee(s)')
    fireEvent.change(screen.getByLabelText('Sens du tri'), { target: { value: 'Ascendant' } })
    fireEvent.change(screen.getByLabelText('Colonne de tri des alertes'), { target: { value: 'Client' } })
    expect(container.querySelector('thead th')!.textContent).toBe('Client (Ascendant)')
    fireEvent.click(screen.getByRole('button', { name: 'Colorier une ligne sur deux' }))
    expect(([...container.querySelectorAll('tbody tr')] as HTMLElement[]).every((l) => l.style.background === '')).toBe(true)
  })
})

// Branche sur la base (plan « RIG v3 100 % fonctionnel », lot 20) : les reglages RIG v2 de l'agent, ses gardes de profil,
// « non enregistre » dit ; les imprimantes PaperCut non lues ; en panne, le motif.
describe('EcranParametresAccueil branche sur la base', () => {
  it('reprend les reglages RIG v2 lus (intervalle en ms), cache ce que le profil RIG v2 cache, dit qu il n enregistre pas', async () => {
    const { container } = await ouvrir(Vue({
      mode: 'serveur', agentV2: true, estAdministrateur: false, estAmitel: false,
      valeurs: { ConsoleAccueil_Colorier1LigneSur2: 'False', ConsoleAccueil_AutofocusOnClickIconEtpOpe: 'False',
                 ConsoleAccueil_IntervalSlideshowAlertes: '5000', ConsoleAccueil_Form_MaxWidth: '5000' },
    }))
    expect(container.querySelector('.rig-tcw__exemple')).toBeNull()
    expect(screen.getByText(/Vos réglages RIG v2 \(4 enregistré\(s\)/).textContent).toContain(SansEnregistrement)
    // Animations : administrateur seul ; molette et date libre : Amitel seul (FORM_SKIN_ACCUEIL_PARAM.cs:552-575).
    expect(screen.queryByRole('button', { name: 'Animations' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Date en saisie libre' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Focus automatique au clic sur un bouton' }).className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Colorier une ligne sur deux' }).className).toBe('rig-bascule')
    expect((screen.getByLabelText('Interval de defilement (secondes)') as HTMLInputElement).value).toBe('5')
    expect((screen.getByLabelText('Largeur max.') as HTMLInputElement).value).toBe('5000')
    expect(compte(container)).toBe('Imprimantes non lues')
    expect(screen.queryByRole('button', { name: /^Greffe - Accueil/ })).toBeNull()
  })

  it('un administrateur Amitel voit les trois reglages gardes ; sans compte RIG v2, rien a relire', async () => {
    await ouvrir(Vue({ mode: 'serveur', agentV2: false, estAdministrateur: true, estAmitel: true }))
    expect(screen.getByRole('button', { name: 'Animations' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Date en saisie libre' })).toBeTruthy()
    expect(screen.getByText(/Vous n'avez pas de compte RIG v2/)).toBeTruthy()
  })

  it('en panne : le motif', async () => {
    await ouvrir(Vue({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }))
    expect(screen.getByText(/La base du greffe 3801 ne répond pas/)).toBeTruthy()
  })
})
