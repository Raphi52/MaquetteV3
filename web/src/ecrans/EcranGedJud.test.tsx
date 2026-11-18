import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranGedJud } from './EcranGedJud'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranGedJud', () => {
  it('liste les quatre documents, sans date de decision pour le brouillon', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    expect(compte(container)).toBe('4 document(s)')
    const brouillon = container.querySelectorAll('tbody tr')[2]!
    expect([...brouillon.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['Jugement de cloture', '', '2025J00088', 'Jugement', '', '20/09/2026', '0', 'Brouillon'])
    expect(brouillon.querySelector('.rig-etat')!.className).toBe('rig-etat rig-etat--attente')
  })

  it('filtre par nature, par etat et par « Non lies a une decision »', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    expect([...(screen.getByLabelText('Nature du document') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Toutes', 'Copie', 'Jugement', 'Ordonnance'])
    fireEvent.change(screen.getByLabelText('Nature du document'), { target: { value: 'Jugement' } })
    expect(compte(container)).toBe('2 document(s)')
    fireEvent.change(screen.getByLabelText('Etat'), { target: { value: 'Notifie' } })
    expect(compte(container)).toBe('1 document(s)')
    fireEvent.change(screen.getByLabelText('Nature du document'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Etat'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Non lies a une decision' }))
    expect(compte(container)).toBe('1 document(s)')
  })

  it('une action s\'inscrit a cote du compte', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Lier a une decision' }))
    expect(compte(container)).toBe('4 document(s) — Lier a une decision judiciaire')
  })

  it('un document clique ouvre ses actions et son historique ; « Afficher » remonte le dossier', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranGedJud ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByText('2026/0412'))
    expect(screen.getByText('Jugement d\'ouverture — minute 2026/0412')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Historique' }))
    expect([...container.querySelectorAll('.rig-historique__liste li')].map((li) => li.textContent))
      .toEqual(['06/03/2026 — signe par le president', '07/03/2026 — notifie au mandataire'])

    fireEvent.click(screen.getByRole('button', { name: 'Imprimer' }))
    expect(compte(container)).toBe('4 document(s) — Imprimer')

    fireEvent.click(screen.getByRole('button', { name: 'Afficher le document' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00318')
  })
})
