import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranFicheClient } from './EcranFicheClient'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranFicheClient.razor), fiche LEROY au depart.
// Milliers : montant() (ListeModerne.tsx) les separe par l'espace insecable U+00A0.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
// Les onglets de la fiche, en puces comptees.
const puce = (nom: RegExp) => screen.getByRole('button', { name: nom })

describe('EcranFicheClient', () => {
  it('affiche la fiche LEROY, son compte et son solde', () => {
    const { container } = render(<EcranFicheClient />)
    expect(valeur('Raison sociale')).toBe('SELARL LEROY & ASSOCIES')
    expect(valeur('Adresse')).toBe('12 rue du Palais\n51100 Reims')
    // ToString("N2") en fr-FR : milliers separes par U+202F, comme .NET.
    expect(valeur('Montant de provision')).toBe('1 500,00')
    expect(valeur('Nb factures')).toBe('412')
    expect(screen.getByText('Releve de facture actif').className).toBe('rig-etat rig-etat--ok')
    expect(synthese(container)).toBe('3 mouvements · solde actuel 812,40 €')
    // Debit, credit et solde avec leur mini-barre ; un montant nul reste une cellule vide.
    expect(lignes(container)).toEqual([
      ['04/07/2026', 'Facture FA-44120', '194,85 €', '', '194,85 €'],
      ['28/07/2026', 'Encaissement CH-4471', '', '194,85 €', '0,00 €'],
      ['19/08/2026', 'Facture FA-45120', '812,40 €', '', '812,40 €'],
    ])
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll('.rig-lm-montant')).toHaveLength(2)
    expect([...container.querySelectorAll('.rig-lm-carte__valeur')].map((v) => v.textContent))
      .toEqual(['812,40 €', '1\u00a0007,25 € 2 mouvement(s)', '194,85 € 1 mouvement(s)'])
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 mouvements affichesDebit 1\u00a0007,25 €Credit 194,85 €Solde 812,40 €')
  })

  it('changer de client change la fiche ; la puce Contacts liste ses contacts', () => {
    const { container } = render(<EcranFicheClient />)
    fireEvent.change(screen.getByLabelText('Choisir un client'), { target: { value: 'DUPONT' } })
    expect(valeur('Etat du compte')).toBe('Bloque')
    expect(valeur('Montant de provision')).toBe('0,00')
    expect(screen.getByText('Pas de releve de facture').className).toBe('rig-etat rig-etat--attente')
    expect(synthese(container)).toBe('1 mouvement · solde actuel 145,00 €')

    fireEvent.change(screen.getByLabelText('Choisir un client'), { target: { value: 'LEROY' } })
    expect(puce(/^Contacts/).textContent).toBe('Contacts 2')
    fireEvent.click(puce(/^Contacts/))
    expect(puce(/^Contacts/).getAttribute('aria-pressed')).toBe('true')
    expect(puce(/^Compte/).getAttribute('aria-pressed')).toBe('false')
    expect(synthese(container)).toBe('2 contacts · 1 principal')
    // Avatar d'initiales, nom puis fonction ; principal en point vert.
    expect(lignes(container)).toEqual([
      ['ALAntoine LEROYMandataire', '03 26 40 11 22', 'a.leroy@exemple.fr', 'oui'],
      ['SHSarah HUETAssistante', '03 26 40 11 25', 's.huet@exemple.fr', ''],
    ])
    expect(container.querySelector('tbody .rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
  })

  it('les cartes Debits et Credits filtrent les mouvements sans changer le solde ; « Aller a un mouvement… » aussi', () => {
    const { container } = render(<EcranFicheClient />)
    const [, debits, credits] = [...container.querySelectorAll<HTMLElement>('.rig-lm-carte')]
    fireEvent.click(credits!)
    expect(lignes(container).map((l) => l[1])).toEqual(['Encaissement CH-4471'])
    expect(synthese(container)).toBe('3 mouvements · solde actuel 812,40 €')
    fireEvent.click(debits!)
    expect(lignes(container).map((l) => l[1])).toEqual(['Facture FA-44120', 'Facture FA-45120'])
    fireEvent.click(debits!)
    expect(lignes(container)).toHaveLength(3)

    fireEvent.change(screen.getByLabelText('Aller a un mouvement…'), { target: { value: 'fa-45' } })
    expect(lignes(container).map((l) => l[1])).toEqual(['Facture FA-45120'])
    fireEvent.change(screen.getByLabelText('Aller a un mouvement…'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun mouvement ne repond a ce filtre.')
  })
})
