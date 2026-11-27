import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranDocDemat, type VueDocDemat } from './EcranDocDemat'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const liens = (c: HTMLElement) => [...c.querySelectorAll('.rig-menu__lien')].map((b) => b.textContent)

describe('EcranDocDemat', () => {
  it('ouvre le premier dossier deplie, sa premiere piece selectionnee', () => {
    const { container } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    expect([...container.querySelectorAll('.rig-menu__tete')].map((b) => b.textContent))
      .toEqual(['▸2026B008113', '▸2019B002332', '▸2011B005121'])
    expect(liens(container)).toEqual(['STATStatuts constitutifs', 'CNICarte d\'identite du gerant', 'DOMJustificatif de domiciliation'])
    expect(container.querySelector('.rig-menu__lien--actif')!.textContent).toBe('STATStatuts constitutifs')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('Statuts constitutifs')
    expect((screen.getByLabelText('Date de la piece') as HTMLInputElement).value).toBe('28/08/2026')
  })

  it('un dossier se deplie et se replie ; la recherche filtre les pieces sans casse', () => {
    const { container } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /2019B00233/ }))
    expect(liens(container)).toHaveLength(5)
    expect(container.querySelectorAll('.rig-menu__groupe--ouvert')).toHaveLength(2)

    fireEvent.change(screen.getByLabelText('Rechercher'), { target: { value: 'stat' } })
    expect(liens(container)).toEqual(['STATStatuts constitutifs', 'STATStatuts mis a jour'])

    fireEvent.click(screen.getByRole('button', { name: /2026B00811/ }))
    expect(liens(container)).toEqual(['STATStatuts mis a jour'])
  })

  it('une piece choisie s\'affiche ; « Voir » remonte son dossier, les actions s\'annoncent', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranDocDemat ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByRole('button', { name: 'CNICarte d\'identite du gerant' }))
    expect((screen.getByLabelText('Controle de la piece') as HTMLInputElement).value).toBe('A verifier')

    // Aucune action n'enregistre rien : elle le dit, jamais un faux succes (lot 11).
    fireEvent.click(screen.getByRole('button', { name: 'Verifier la carte d\'identite' }))
    expect(container.querySelector('p.rig-nonmaq__texte')!.textContent).toBe('Démonstration : rien n’est enregistré — « Verifier la carte d\'identite ».')

    fireEvent.click(screen.getByRole('button', { name: 'Voir' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2026B00811')
  })

  // Lot 11 : branche, l'ecran montre les pieces de la base (Pont/EcranDocDemat.cs), sans la mention « exemple ».
  const base: VueDocDemat = {
    mode: 'serveur', message: null, total: 3, pieces: [
      { id: '7', dossier: '004151', demande: 'TRANSNORD', demat: 'Liasse CFE 12', abrege: 'KBIS', libelle: 'Extrait Kbis', date: '2004-05-28', etat: 'En vigueur', fichier: 'kbis.pdf', infogreffe: 'ACQ' },
      { id: '8', dossier: '004151', demande: 'TRANSNORD', demat: 'Liasse CFE 12', abrege: 'STAT', libelle: 'Statuts', date: '', etat: 'Supprimée', fichier: 'statuts.pdf', infogreffe: '' },
      { id: '9', dossier: 'D0042', demande: '', demat: 'DAS 3', abrege: 'DAS', libelle: 'Declaration', date: '2004-06-01', etat: 'En vigueur', fichier: 'das.pdf', infogreffe: '' },
    ],
  }

  it('branche : les pieces de la base, groupees par dossier, la premiere choisie, sans « exemple »', async () => {
    const pont = installerFauxPont({ 'EcranDocDemat.Pieces': () => base })
    const { container } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect([...container.querySelectorAll('.rig-menu__tete')].map((b) => b.textContent)).toEqual(['▸0041512', '▸D00421']))
    expect(pont.appels).toHaveBeenCalledWith('EcranDocDemat.Pieces')
    expect(liens(container)).toEqual(['KBISExtrait Kbis', 'STATStatuts'])
    expect(container.querySelector('.rig-menu__lien--actif')!.textContent).toBe('KBISExtrait Kbis')
    expect((screen.getByLabelText('Nom du fichier') as HTMLInputElement).value).toBe('kbis.pdf')
    expect((screen.getByLabelText('Etat Infogreffe') as HTMLInputElement).value).toBe('ACQ')
    expect(screen.queryByText(/de démonstration/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'STATStatuts' }))
    expect((screen.getByLabelText('Etat') as HTMLInputElement).value).toBe('Supprimée')
    // Sans date de piece (DMCOPJ_DATE_PIECE NULL) : le champ reste vide (capture du 27/11/2026 : « undefined/undefined/ »).
    expect((screen.getByLabelText('Date de la piece') as HTMLInputElement).value).toBe('')
  })

  it('sans serveur : la demonstration, marquee « exemple » ; en panne : le motif et aucune piece', async () => {
    installerFauxPont({ 'EcranDocDemat.Pieces': () => ({ mode: 'demo', pieces: [], total: 0 }) })
    const { container, unmount } = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(screen.getByText(/Pièces de démonstration/)).toBeTruthy())
    expect(container.querySelectorAll('.rig-menu__tete')).toHaveLength(3)
    unmount(); retirerFauxPont()

    installerFauxPont({ 'EcranDocDemat.Pieces': () => ({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', pieces: [], total: 0 }) })
    const panne = render(<EcranDocDemat ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('La base du greffe 3801 ne répond pas.'))
    expect(panne.container.querySelectorAll('.rig-menu__tete')).toHaveLength(0)
  })
})
