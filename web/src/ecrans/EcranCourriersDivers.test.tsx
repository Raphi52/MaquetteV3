import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCourriersDivers } from './EcranCourriersDivers'
import { contexteDossier } from '../test/dossier'

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

// Ouvert depuis la page d'une affaire : le courrier est pour elle, deja choisie
// (bloc « Pour le dossier ») ; le catalogue ne change pas (plan « Vue du dossier », lot 2).
describe('EcranCourriersDivers ouvert depuis une affaire', () => {
  it('montre l\'affaire choisie en tete ; sans affaire, l\'ecran d\'avant', () => {
    const avec = render(<EcranCourriersDivers dossier={contexteDossier()} />)
    expect(screen.getByText('Pour le dossier')).toBeTruthy()
    expect((screen.getByLabelText('N de dossier') as HTMLInputElement).value).toBe('004176')
    expect((screen.getByLabelText('Denomination') as HTMLInputElement).value).toBe('SCI DU PARC')
    expect(compte(avec.container)).toBe('4 courrier(s)')
    avec.unmount()

    const sans = render(<EcranCourriersDivers dossier={null} />)
    expect(screen.queryByText('Pour le dossier')).toBeNull()
    expect(sans.container.querySelector('.rig-criteres > .rig-kicker')!.textContent).toBe('Courriers divers')
  })
})
