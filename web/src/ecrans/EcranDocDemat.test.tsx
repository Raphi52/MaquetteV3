import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranDocDemat } from './EcranDocDemat'

afterEach(cleanup)

const liens = (c: HTMLElement) => [...c.querySelectorAll('.rig-menu__lien')].map((b) => b.textContent)

describe('EcranDocDemat', () => {
  it('ouvre le premier dossier deplie, sa premiere piece selectionnee', () => {
    const { container } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    expect([...container.querySelectorAll('.rig-menu__tete')].map((b) => b.textContent))
      .toEqual(['▸2026B008113', '▸2019B002332', '▸2011B005121'])
    expect(liens(container)).toEqual(['STATStatuts constitutifs', 'CNICarte d\'identite du gerant', 'DOMJustificatif de domiciliation'])
    expect(container.querySelector('.rig-menu__lien--actif')!.textContent).toBe('STATStatuts constitutifs')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('Statuts constitutifs')
    expect((screen.getByLabelText('Date de la piece') as HTMLInputElement).value).toBe('28/08/2026')
  })

  it('un dossier se deplie et se replie ; la recherche filtre les pieces sans casse', () => {
    const { container } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /2019B00233/ }))
    expect(liens(container)).toHaveLength(5)
    expect(container.querySelectorAll('.rig-menu__groupe--ouvert')).toHaveLength(2)

    fireEvent.change(screen.getByLabelText('Rechercher'), { target: { value: 'stat' } })
    expect(liens(container)).toEqual(['STATStatuts constitutifs', 'STATStatuts mis a jour'])

    fireEvent.click(screen.getByRole('button', { name: /2026B00811/ }))
    expect(liens(container)).toEqual(['STATStatuts mis a jour'])
  })

  it('une piece choisie s\'affiche ; « Voir » remonte son dossier, les actions s\'annoncent', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranDocDemat ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByRole('button', { name: 'CNICarte d\'identite du gerant' }))
    expect((screen.getByLabelText('Controle de la piece') as HTMLInputElement).value).toBe('A verifier')

    fireEvent.click(screen.getByRole('button', { name: 'Verifier la carte d\'identite' }))
    expect(container.querySelector('p.rig-nonmaq__texte')!.textContent).toBe('Conformite de la carte d identite verifiee')

    fireEvent.click(screen.getByRole('button', { name: 'Voir' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2026B00811')
  })
})
