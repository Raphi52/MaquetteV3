import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranControleVidiged, oublierVuesDeLaSession } from './EcranControleVidiged'

afterEach(() => { cleanup(); oublierVuesDeLaSession() })

const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const page = () => screen.getByText(/^Page /).textContent
const rangees = (c: HTMLElement) => [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')]
// La premiere cellule est le chevron : le Guid est la deuxieme.
const guids = (c: HTMLElement) => rangees(c).map((tr) => tr.querySelectorAll('td')[1]!.textContent)
/** Une puce de vue : son libelle suivi de son compte. */
const puce = (libelle: string) => screen.getByRole('button', { name: new RegExp(`^${libelle} \\d+$`) })

describe('EcranControleVidiged', () => {
  it('pagine les cinq archives par trois, celles sans CD surlignees', () => {
    const { container } = render(<EcranControleVidiged />)
    expect(compte(container)).toBe('5 archive(s) — 3 affichee(s) · 2 sans CD')
    expect(page()).toBe('Page 1 / 2')
    expect(guids(container)).toEqual(['a41f-2026-0091', 'a41f-2026-0092', 'b77c-2026-0103'])
    expect(rangees(container).map((tr) => tr.className))
      .toEqual(['', '', 'rig-table__ligne--alerte'])
  })

  it('« Suivant » et « Precedent » restent dans les bornes', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    expect(page()).toBe('Page 2 / 2')
    expect(guids(container)).toEqual(['b77c-2026-0104', 'c12d-2026-0110'])
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    expect(page()).toBe('Page 2 / 2')
    fireEvent.click(screen.getByRole('button', { name: 'Precedent' }))
    fireEvent.click(screen.getByRole('button', { name: 'Precedent' }))
    expect(page()).toBe('Page 1 / 2')
  })

  it('filtre « Sans support CD » et par type ; les lignes par page se changent', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Sans support CD uniquement' }))
    expect(compte(container)).toBe('2 archive(s) — 2 affichee(s) · 2 sans CD')
    expect(page()).toBe('Page 1 / 1')
    fireEvent.click(screen.getByRole('button', { name: 'Sans support CD uniquement' }))

    fireEvent.change(screen.getByLabelText('Type d\'archive'), { target: { value: 'Comptes annuels' } })
    expect(compte(container)).toBe('2 archive(s) — 2 affichee(s) · 1 sans CD')
    fireEvent.change(screen.getByLabelText('Type d\'archive'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Lignes par page'), { target: { value: '10' } })
    expect(compte(container)).toBe('5 archive(s) — 5 affichee(s) · 2 sans CD')
    expect(page()).toBe('Page 1 / 1')
  })

  it('le numero affiche est celui des boutons, la page montree reste dans les bornes', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    fireEvent.change(screen.getByLabelText('Lignes par page'), { target: { value: '5' } })
    // Comme dans Blazor : « Page 2 / 1 », mais la seule page existante est montree.
    expect(page()).toBe('Page 2 / 1')
    expect(compte(container)).toBe('5 archive(s) — 5 affichee(s) · 2 sans CD')
  })
})

describe('EcranControleVidiged en liste moderne', () => {
  it('la tete garde le nom actuel de la liste, « Resultats », pour titre', () => {
    // Regle du plan (titre = nom actuel tel quel, decision du lot 5). Faux si : la tete prenait un nom
    // invente, comme « Archives » dans la premiere version du lot 3.
    const { container } = render(<EcranControleVidiged />)
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Resultats')
  })

  it('une ligne par archive : type en pastille, support en point d\'etat ; Date et Taille dans la ligne depliee', () => {
    const { container } = render(<EcranControleVidiged />)
    const [avecCd, , sansCd] = rangees(container)
    expect(avecCd!.querySelector('.rig-lm-pastille')!.textContent).toBe('Lot de numerisation')
    expect(avecCd!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(avecCd!.querySelector('.rig-lm-etat')!.textContent).toBe('CD-2026-014')
    expect(sansCd!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(sansCd!.querySelector('.rig-lm-etat')!.textContent).toBe('Sans CD')

    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[2]!)
    expect(rangees(container)[2]!.className).toBe('rig-table__ligne--alerte rig-lm-ouverte')
    expect([...container.querySelectorAll('tr.rig-lm-detail .rig-lm-champ')].map((c) => c.textContent))
      .toEqual(['Date20/08/2026', 'Taille1,2 Go'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('3 archives affichees' + 'Sans CD 1' + 'Documents 1513')
  })

  it('« Guid ou info » est le champ « Aller a une archive… » de la tete : guid ou info, hors des vues', () => {
    const { container } = render(<EcranControleVidiged />)
    expect(screen.queryByLabelText('Guid ou info')).toBeNull()
    const champ = screen.getByRole('textbox', { name: 'Aller a une archive…' })
    fireEvent.change(champ, { target: { value: 'b77c' } })
    expect(guids(container)).toEqual(['b77c-2026-0103', 'b77c-2026-0104'])
    expect(compte(container)).toBe('2 archive(s) — 2 affichee(s) · 1 sans CD')
    fireEvent.change(champ, { target: { value: 'ged judiciaire' } })
    expect(guids(container)).toEqual(['c12d-2026-0110'])
    // Les vues ne portent que les criteres de gauche : « Toutes » reste choisie, et la choisir garde le texte.
    expect(puce('Toutes').getAttribute('aria-pressed')).toBe('true')
    expect(puce('Toutes').textContent).toBe('Toutes 5')
    fireEvent.click(puce('Sans support CD'))
    expect((champ as HTMLInputElement).value).toBe('ged judiciaire')
    expect(guids(container)).toEqual(['c12d-2026-0110'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune archive ne repond a ces criteres.')).toBeTruthy()
  })

  it('les vues posent les criteres de gauche et portent leur compte', () => {
    const { container } = render(<EcranControleVidiged />)
    expect(puce('Toutes').getAttribute('aria-pressed')).toBe('true')
    expect(['Toutes', 'Sans support CD', 'Comptes annuels', 'Documents judiciaires', 'Lot de numerisation'].map((l) => puce(l).textContent))
      .toEqual(['Toutes 5', 'Sans support CD 2', 'Comptes annuels 2', 'Documents judiciaires 1', 'Lot de numerisation 2'])

    fireEvent.click(puce('Sans support CD'))
    expect(screen.getByRole('button', { name: 'Sans support CD uniquement' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(guids(container)).toEqual(['b77c-2026-0103', 'c12d-2026-0110'])
    expect(puce('Sans support CD').getAttribute('aria-pressed')).toBe('true')

    fireEvent.click(puce('Comptes annuels'))
    expect((screen.getByLabelText('Type d\'archive') as HTMLSelectElement).value).toBe('Comptes annuels')
    expect(screen.getByRole('button', { name: 'Sans support CD uniquement' }).className).toBe('rig-bascule')

    // « + Nouvelle vue » garde les criteres courants le temps de la session.
    fireEvent.click(screen.getByRole('button', { name: 'Sans support CD uniquement' }))
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    const nom = screen.getByRole('textbox', { name: 'Nom de la nouvelle vue' })
    fireEvent.change(nom, { target: { value: 'Comptes sans CD' } })
    fireEvent.keyDown(nom, { key: 'Enter' })
    expect(puce('Comptes sans CD').textContent).toBe('Comptes sans CD 1')
    expect(puce('Comptes sans CD').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(puce('Toutes'))
    expect(guids(container)).toHaveLength(3)
    fireEvent.click(puce('Comptes sans CD'))
    expect(guids(container)).toEqual(['b77c-2026-0103'])
  })
})
