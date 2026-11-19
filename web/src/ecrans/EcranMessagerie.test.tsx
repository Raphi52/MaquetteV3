import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranMessagerie } from './EcranMessagerie'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranMessagerie.razor) : 4 modeles, ACC_DEM choisi.
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

describe('EcranMessagerie', () => {
  it('affiche les modeles et l\'apercu du premier', () => {
    const { container } = render(<EcranMessagerie />)
    expect(screen.getByText('Resultats (4)')).toBeTruthy()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('ACC_DEM')
    expect(valeur('Sujet')).toBe('Votre demande {NUM_DEMANDE} a bien ete recue')
    expect(valeur('Corps du message')).toMatch(/^Bonjour \{CIVILITE\} \{NOM\},\n\nNous avons bien recu/)
    expect(screen.getByText('Envoi automatique actif').className).toBe('rig-etat rig-etat--ok')
    // Les domaines du modele s'affichent deux fois, comme dans le .razor.
    expect([...container.querySelectorAll('.rig-chip')].map((p) => p.textContent)).toEqual(['RCS', 'RCS'])
  })

  it('filtre par domaine ; l\'onglet des listes de distribution', () => {
    const { container } = render(<EcranMessagerie />)
    fireEvent.click(screen.getByRole('button', { name: 'Judiciaire' }))
    expect(screen.getByText('Resultats (1)')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'CONV_AUD — Convocation a une audience' }))
    expect(screen.getByText('Envoi manuel').className).toBe('rig-etat rig-etat--attente')
    expect(valeur('Copie (CC)')).toBe('greffe@exemple.fr')

    fireEvent.click(screen.getByRole('button', { name: 'Listes de distribution' }))
    const lignes = [...container.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
    expect(lignes).toHaveLength(3)
    expect(lignes[0]).toEqual(['LD_MAND', 'Mandataires judiciaires', 'Judiciaire', '2', 'a.leroy@exemple.fr ; s.blanc@exemple.fr'])
  })
})
