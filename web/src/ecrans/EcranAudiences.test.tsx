import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranAudiences } from './EcranAudiences'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de la capture de l'ecran Blazor (18/11/2026) :
// date proposee 16/09/2026 (JeuDemo.Aujourdhui + 7), 3 audiences programmees.
function ouvrir() {
  const pont = installerFauxPont({ 'EcranAudiences.DateProposee': () => '2026-09-16' })
  return { pont, ...render(<EcranAudiences />) }
}

const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

describe('EcranAudiences', () => {
  it('affiche les audiences programmees et la date proposee par le pont', async () => {
    const { container, pont } = ouvrir()
    expect(await screen.findByDisplayValue('2026-09-16')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.DateProposee')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('3 audience(s)')
    expect(lignes(container)[0]).toEqual(['15/09/2026', '09:00', 'Audience de procedure collective', '1re', 'A', 'oui', 'oui', '14'])
    expect(lignes(container)[1]).toEqual(['22/09/2026', '10:15', 'Audience du juge-commissaire', '1re', 'A', '', '', '6'])
    // Le @bind d'un TimeOnly ecrivait « HH:mm:ss ».
    expect((container.querySelector('input[type="time"]') as HTMLInputElement).value).toBe('09:00:00')
    expect(screen.getByText('Envoi de mail actif').className).toBe('rig-etat rig-etat--ok')
  })

  it('les bascules s\'allument et s\'eteignent : publique allumee, classement admis par defaut', () => {
    ouvrir()
    const publique = screen.getByRole('button', { name: 'Audience publique' })
    const web = screen.getByRole('button', { name: 'Publiee sur le web' })
    expect(publique.className).toBe('rig-bascule rig-bascule--actif')
    expect(web.className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Insuffisance d\'actif' }).className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.click(web)
    fireEvent.click(screen.getByRole('button', { name: 'Report' }))
    expect(web.className).toBe('rig-bascule rig-bascule--actif')
    expect(screen.getByRole('button', { name: 'Report' }).className).toBe('rig-bascule rig-bascule--actif')
  })

  it('« Creer l\'audience » ajoute la saisie, rangee par date, sans affaire', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-16')
    fireEvent.change(container.querySelector('input[type="time"]')!, { target: { value: '14:30' } })
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '3e' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))

    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('4 audience(s)')
    expect(lignes(container)[1]).toEqual(['16/09/2026', '14:30', 'Audience de procedure collective', '3e', 'A', 'oui', '', '0'])
  })

  it('sans date d\'audience, rien n\'est cree', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-16')
    fireEvent.change(container.querySelector('input[type="date"]')!, { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('3 audience(s)')
  })
})
