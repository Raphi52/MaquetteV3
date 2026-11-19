import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranFicheClient } from './EcranFicheClient'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranFicheClient.razor), fiche LEROY au depart.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

describe('EcranFicheClient', () => {
  it('affiche la fiche LEROY, son compte et son solde', () => {
    const { container } = render(<EcranFicheClient />)
    expect(valeur('Raison sociale')).toBe('SELARL LEROY & ASSOCIES')
    expect(valeur('Adresse')).toBe('12 rue du Palais\n51100 Reims')
    // ToString("N2") en fr-FR : milliers separes par U+202F, comme .NET.
    expect(valeur('Montant de provision')).toBe('1 500,00')
    expect(valeur('Nb factures')).toBe('412')
    expect(screen.getByText('Releve de facture actif').className).toBe('rig-etat rig-etat--ok')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('Solde actuel 812,40 EUR')
    expect(lignes(container)).toEqual([
      ['04/07/2026', 'Facture FA-44120', '194,85', '', '194,85'],
      ['28/07/2026', 'Encaissement CH-4471', '', '194,85', '0,00'],
      ['19/08/2026', 'Facture FA-45120', '812,40', '', '812,40'],
    ])
  })

  it('changer de client change la fiche ; l\'onglet Contacts liste ses contacts', () => {
    const { container } = render(<EcranFicheClient />)
    fireEvent.change(screen.getByLabelText('Choisir un client'), { target: { value: 'DUPONT' } })
    expect(valeur('Etat du compte')).toBe('Bloque')
    expect(valeur('Montant de provision')).toBe('0,00')
    expect(screen.getByText('Pas de releve de facture').className).toBe('rig-etat rig-etat--attente')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('Solde actuel 145,00 EUR')

    fireEvent.change(screen.getByLabelText('Choisir un client'), { target: { value: 'LEROY' } })
    fireEvent.click(screen.getByRole('button', { name: 'Contacts' }))
    expect(screen.getByRole('button', { name: 'Contacts' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(lignes(container)).toEqual([
      ['Antoine LEROY', 'Mandataire', '03 26 40 11 22', 'a.leroy@exemple.fr', 'oui'],
      ['Sarah HUET', 'Assistante', '03 26 40 11 25', 's.huet@exemple.fr', ''],
    ])
  })
})
