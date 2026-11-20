import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { PourLeDossier } from './PourLeDossier'
import { contexteDossier } from '../test/dossier'

afterEach(cleanup)

const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement)

describe('PourLeDossier', () => {
  it('montre l\'affaire choisie, en lecture seule', () => {
    render(<PourLeDossier dossier={contexteDossier()} />)
    expect(screen.getByText('Pour le dossier')).toBeTruthy()
    expect(valeur('N de dossier').value).toBe('004176')
    expect(valeur('Siren').value).toBe('812 445 901')
    expect(valeur('Denomination').value).toBe('SCI DU PARC')
    for (const libelle of ['N de dossier', 'Siren', 'Denomination']) expect(valeur(libelle).readOnly, libelle).toBe(true)
  })

  it('une affaire purement judiciaire, sans SIREN, n\'a pas de champ Siren vide', () => {
    render(<PourLeDossier dossier={contexteDossier({ numero: '2026J00087', siren: '', type: 'JUD', nom: 'DUPONT Jean' })} />)
    expect(valeur('N de dossier').value).toBe('2026J00087')
    expect(screen.queryByLabelText('Siren')).toBeNull()
    expect(valeur('Denomination').value).toBe('DUPONT Jean')
  })
})
