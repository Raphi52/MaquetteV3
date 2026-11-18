import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranArchivage } from './EcranArchivage'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent!.trim()

describe('EcranArchivage', () => {
  it('retient les radiations de 2012 a 2016, bordereau non prepare', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    expect(compte(container)).toBe('4 retenue(s) sur 5 — bordereau non prepare')
    const premiere = container.querySelectorAll('tbody tr')[0]!
    expect([...premiere.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['1998B00214', 'SARL MENUISERIE COLLIN', 'PM', '12/03/2014', '04/11/2013', '18', 'A archiver'])
    expect(premiere.querySelector('.rig-etat')!.className).toBe('rig-etat rig-etat--attente')
  })

  it('« Preparer le bordereau » passe les formalites sur bordereau', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Preparer le bordereau' }))
    expect(compte(container)).toBe('4 retenue(s) sur 5 — bordereau prepare')
    expect([...container.querySelectorAll('.rig-etat')].every((e) => e.className === 'rig-etat rig-etat--ok'
      && e.textContent === 'Sur bordereau')).toBe(true)
  })

  it('filtre par type RCS, par dates, et borne au nombre de dossiers', () => {
    const { container } = render(<EcranArchivage ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: 'PP' } })
    expect(compte(container)).toBe('2 retenue(s) sur 5 — bordereau non prepare')
    fireEvent.change(screen.getByLabelText('Type RCS'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Date maxi.'), { target: { value: '' } })
    expect(compte(container)).toBe('5 retenue(s) sur 5 — bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Nombre de dossiers'), { target: { value: '2' } })
    expect(compte(container)).toBe('2 retenue(s) sur 5 — bordereau non prepare')

    fireEvent.change(screen.getByLabelText('Date mini.'), { target: { value: '2030-01-01' } })
    expect(screen.getByText('Aucune formalite dans cette plage de radiation.')).toBeTruthy()
  })

  it('une formalite cliquee remonte son dossier', () => {
    const ouvrirDossier = vi.fn()
    render(<EcranArchivage ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByText('DUPONT Jean'))
    expect(ouvrirDossier).toHaveBeenCalledWith('1995A00120')
  })
})
