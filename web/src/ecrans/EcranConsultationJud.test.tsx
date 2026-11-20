import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranConsultationJud } from './EcranConsultationJud'
import { contexteDossier } from '../test/dossier'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranConsultationJud', () => {
  it('liste les quatre enrolements, le rejete surligne', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    expect(compte(container)).toBe('4 affaire(s)')
    const rangees = container.querySelectorAll('tbody tr')
    expect([...rangees[2]!.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['2026J00140', 'EURL COIFFURE MARIE', 'Dematerialise', 'Rejete', 'Piece manquante', '02/06/2026', '03/06/2026', '3'])
    expect(rangees[2]!.className).toBe('rig-table__ligne--alerte')
    expect(rangees[0]!.className).toBe('')
    // Une affaire non traitee n'a pas de date de traitement.
    expect(rangees[1]!.querySelectorAll('td')[6]!.textContent).toBe('')
  })

  it('filtre par texte sans casse, par etat et par « Rejets uniquement »', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Affaire / dossier'), { target: { value: '  marne ' } })
    expect(compte(container)).toBe('1 affaire(s)')
    fireEvent.change(screen.getByLabelText('Affaire / dossier'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Etat de traitement'), { target: { value: 'En attente' } })
    expect(compte(container)).toBe('2 affaire(s)')
    fireEvent.change(screen.getByLabelText('Etat de traitement'), { target: { value: '' } })

    fireEvent.click(screen.getByRole('button', { name: 'Rejets uniquement' }))
    expect(compte(container)).toBe('1 affaire(s)')
  })

  it('« Traitement jusqu\'au » ecarte les affaires non traitees', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Traitement jusqu\'au'), { target: { value: '2026-12-31' } })
    expect(compte(container)).toBe('2 affaire(s)')
  })

  it('une ligne cliquee montre son detail ; la piece remonte le dossier, le XML se deplie', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranConsultationJud ouvrirDossier={ouvrirDossier} />)
    expect(screen.queryByText(/Detail de/)).toBeNull()

    fireEvent.click(screen.getByText('2026J00140'))
    expect(screen.getByText('Detail de 2026J00140')).toBeTruthy()
    expect((screen.getByLabelText('Anomalies') as HTMLTextAreaElement).value).toBe('Acte de saisine absent du depot')
    expect((screen.getByLabelText('Motif de rejet') as HTMLTextAreaElement).value).toBe('Dossier incomplet')

    fireEvent.click(screen.getByRole('button', { name: 'Visualiser la piece' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2023B01120')

    expect(container.querySelector('pre.rig-code')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Visualiser le XML' }))
    expect(container.querySelector('pre.rig-code')!.textContent).toBe('<enrolement num="2026J00140" />')
  })
})

// Ouvert depuis la page d'une affaire judiciaire : cherchee par son numero dans
// « Affaire / dossier » (plan « Vue du dossier », lot 2).
describe('EcranConsultationJud ouvert depuis une affaire', () => {
  it('ne montre qu\'elle, le critere deja rempli ; sans affaire, les quatre', () => {
    const avec = render(<EcranConsultationJud ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '2026J00134', type: 'JUD' })} />)
    expect(compte(avec.container)).toBe('1 affaire(s)')
    expect(screen.getByText('SAS TRANSPORTS MARNE')).toBeTruthy()
    expect((screen.getByLabelText('Affaire / dossier') as HTMLInputElement).value).toBe('2026J00134')
    avec.unmount()

    const sans = render(<EcranConsultationJud ouvrirDossier={vi.fn()} dossier={null} />)
    expect(compte(sans.container)).toBe('4 affaire(s)')
    expect((screen.getByLabelText('Affaire / dossier') as HTMLInputElement).value).toBe('')
  })
})
