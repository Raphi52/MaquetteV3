import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranBanques, type VueBanques } from './EcranBanques'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranBanques.razor), 4 banques, CA choisie.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

describe('EcranBanques', () => {
  it('affiche les quatre banques et la fiche de la premiere', () => {
    const { container } = render(<EcranBanques />)
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('4 banques')
    expect(screen.getByText('Liste (4)')).toBeTruthy()
    // Avatar « BP » (LA ne donne pas d'initiale), designation, code ; compte ; modes en pastilles.
    expect(lignes(container)[2]).toEqual(['BPLA BANQUE POSTALELBP', '512300', 'Cheque Virement Carte bancaire'])
    expect([...container.querySelectorAll('tbody tr')[2]!.querySelectorAll('.rig-lm-pastille')].map((p) => p.className))
      .toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-violet', 'rig-lm-pastille rig-lm-t-orange'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('4 banques affichees')
    expect(screen.getByRole('button', { name: 'CA — CREDIT AGRICOLE' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(valeur('Designation')).toBe('CREDIT AGRICOLE')
    expect(valeur('Adresse de l\'agence')).toBe('25 rue de Vesle\n51100 Reims')
    expect(screen.getByText('Fournisseur : oui')).toBeTruthy()
  })

  it('la recherche filtre sans casse ; un clic sur une ligne change la fiche', () => {
    const { container } = render(<EcranBanques />)
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'post' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['BPLA BANQUE POSTALELBP'])
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune banque ne repond a cette recherche.')

    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: '' } })
    fireEvent.click(container.querySelectorAll('tbody tr')[1]!)
    expect(valeur('Designation')).toBe('BANQUE POPULAIRE')
    expect(valeur('Modes de paiement')).toBe('Cheque')
    expect(screen.getByText('Notification : non')).toBeTruthy()
  })
})

// D'ou viennent les banques (plan « RIG v3 100 % fonctionnel », lot 12) : la demonstration est marquee « exemple », la base ne
// l'est pas et remplace la demonstration, une panne montre son motif et AUCUNE banque (Pont/EcranBanques.cs).
describe('EcranBanques : provenance des banques', () => {
  afterEach(retirerFauxPont)
  const base: VueBanques = {
    banques: [{ code: 'L12B', designation: 'BANQUE DU BANC', compte: '5121', journal: 'BQ9', adresse: '1 rue du Banc\n80000 AMIENS',
                maquetteCheque: 'LC_BANC', modes: ['CHQ', 'VIR'], numLettreCheque: '42', chequeFournisseur: false, chequeNotification: true,
                chequeTropPercu: true }],
    total: 1, mode: 'serveur', message: null,
  }

  it('sans pont, la demonstration est marquee « exemple »', () => {
    const { container } = render(<EcranBanques />)
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Banques de démonstration')
  })

  it('branche au serveur, les banques de la base, sans mention ; la premiere est choisie', async () => {
    installerFauxPont({ 'EcranBanques.Banques': () => base })
    const { container } = render(<EcranBanques />)
    await screen.findByRole('button', { name: 'L12B — BANQUE DU BANC' })
    expect(container.querySelector('[role="note"]')).toBeNull()
    expect(lignes(container)).toEqual([['BBBANQUE DU BANCL12B', '5121', 'CHQ VIR']])
    expect(valeur('Adresse de l\'agence')).toBe('1 rue du Banc\n80000 AMIENS')
    expect(valeur('Num lettre cheque')).toBe('42')
    expect(screen.getByText('Fournisseur : non')).toBeTruthy()
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('Fichier 1')
  })

  it('en panne, le motif et aucune banque (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranBanques.Banques': () => ({ banques: [], total: 0, mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    const { container } = render(<EcranBanques />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(container.querySelectorAll('tbody tr td.rig-table__vide')).toHaveLength(1)
    expect(screen.queryByText('CREDIT AGRICOLE')).toBeNull()
  })
})
