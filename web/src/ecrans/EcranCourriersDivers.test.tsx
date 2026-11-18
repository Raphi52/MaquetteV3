import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCourriersDivers } from './EcranCourriersDivers'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranCourriersDivers', () => {
  it('ouvre sur les quatre courriers actifs, le premier en modification', () => {
    const { container } = render(<EcranCourriersDivers />)
    expect(compte(container)).toBe('4 courrier(s)')
    expect(screen.getByText('Modification du courrier divers CD_CONV')).toBeTruthy()
    expect((screen.getByLabelText('Parametre edition (maquette)') as HTMLInputElement).value).toBe('MAQ_CONV_SIMPLE')
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Actif'])
  })

  it('« Actifs seulement » eteint montre aussi le courrier inactif', () => {
    const { container } = render(<EcranCourriersDivers />)
    fireEvent.click(screen.getByRole('button', { name: 'Actifs seulement' }))
    expect(compte(container)).toBe('5 courrier(s)')
    fireEvent.click(screen.getByText('CD_MESURE'))
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['Avec destinataire', 'Inactif'])
  })

  it('cherche dans le code et le libelle, et filtre par type d\'entite', () => {
    const { container } = render(<EcranCourriersDivers />)
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: 'relance' } })
    expect(compte(container)).toBe('1 courrier(s)')
    fireEvent.change(screen.getByLabelText('Code ou libelle'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE' } })
    expect(compte(container)).toBe('2 courrier(s)')
    fireEvent.change(screen.getByLabelText('Type d\'entite'), { target: { value: 'INSTANCE_PREVENTION' } })
    expect(screen.getByText('Aucun courrier divers ne repond a cette recherche.')).toBeTruthy()
  })
})
