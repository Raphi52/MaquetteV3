import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranGestionAudiences } from './EcranGestionAudiences'

afterEach(cleanup)

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
const entetes = (c: HTMLElement) => [...c.querySelectorAll('thead th')].map((th) => th.textContent)
/** La phrase de synthese : « n sur max maximum », puis les publiques et les supprimees. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]
const options = (libelle: string) =>
  [...(screen.getByLabelText(libelle) as HTMLSelectElement).options].map((o) => o.textContent)
/** Les colonnes secondaires d'une rangee, dans sa ligne depliee : [libelle, valeur]. */
function deplier(rangee: HTMLTableRowElement): string[][] {
  fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
  return [...rangee.nextElementSibling!.querySelectorAll('.rig-lm-champ')].map((c) => [
    c.querySelector('.rig-lm-champ__libelle')!.textContent!, c.querySelector('.rig-lm-champ__valeur')!.textContent!])
}

describe('EcranGestionAudiences', () => {
  it('affiche les cinq audiences en mode cabinets ; les treize colonnes v2, Greffier compris, entre la ligne et sa ligne depliee', () => {
    const { container } = render(<EcranGestionAudiences />)
    expect(synthese(container)).toBe('5 sur 100 maximum · 4 publiques · 1 supprimee')
    expect(entetes(container)).toEqual(['', 'Jour', 'Date', 'Heure', 'Type', 'Juge', 'Type de classement'])
    const premiere = rangees(container)[0]!
    expect([...premiere.querySelectorAll('td')].slice(1, 4).map((td) => td.textContent)).toEqual(['Lundi', '14/09/2026', '09h00'])
    expect(premiere.querySelector('.rig-lm-pastille')!.textContent).toBe('Audience de procedure')
    // Le juge est une personne : avatar et nom.
    expect(premiere.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Mme LEROY')
    const classement = premiere.querySelector('.rig-lm-etat')!
    expect([classement.textContent, classement.className]).toEqual(['Renvoi', 'rig-lm-etat rig-lm-t-orange'])
    expect(deplier(premiere)).toEqual([
      ['Code de cabinet', 'CAB-01'], ['Greffier', 'M. PETIT'], ['Public', 'Oui'], ['Auteur suppression', '—'],
      ['Date suppression', '—'], ['Envoi suppression Infogreffe', '—'], ['Web (non dispo.)', 'non dispo.']])
    expect(premiere.nextElementSibling!.querySelector('td')!.getAttribute('colspan')).toBe('7')
    // L'audience supprimee : sans cabinet ni greffier, sa suppression dite.
    expect(deplier(rangees(container)[3]!)).toEqual([
      ['Code de cabinet', '—'], ['Greffier', '—'], ['Public', 'Oui'], ['Auteur suppression', 'rvilain'],
      ['Date suppression', '02/09/2026'], ['Envoi suppression Infogreffe', 'Envoye'], ['Web (non dispo.)', 'non dispo.']])
  })

  it('hors mode cabinets, la colonne unique devient Section et Greffier disparait', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.click(screen.getByRole('button', { name: 'Recherche par cabinets' }))
    expect(entetes(container)[5]).toBe('Section')
    expect(entetes(container)).not.toContain('Juge')
    const premiere = rangees(container)[0]!
    expect(premiere.querySelectorAll('td')[5]!.textContent).toBe('1re section')
    const champs = deplier(premiere).map(([libelle]) => libelle)
    expect(champs).toHaveLength(6)
    expect(champs).not.toContain('Greffier')
  })

  it('les listes de choix sont les valeurs distinctes, triees comme Order()', () => {
    render(<EcranGestionAudiences />)
    expect(options('Juge')).toEqual(['Tous', 'M. DUVAL', 'Mme LEROY'])
    expect(options('Type de classement')).toEqual(['Tous', 'Clos', 'Mise en delibere', 'Radiation', 'Renvoi'])
    // Un cabinet vide n'est pas propose.
    expect(options('Code de cabinet')).toEqual(['Tous', 'CAB-01', 'CAB-02', 'CAB-03'])
  })

  it('filtre par juge et par dates, et borne au nombre maximum', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.change(screen.getByLabelText('Juge'), { target: { value: 'M. DUVAL' } })
    expect(synthese(container)).toBe('2 sur 100 maximum · 1 publique · 1 supprimee')
    fireEvent.change(screen.getByLabelText('Juge'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2026-09-15' } })
    fireEvent.change(screen.getByLabelText('Au'), { target: { value: '2026-09-17' } })
    expect(synthese(container)).toBe('2 sur 100 maximum · 1 publique · 1 supprimee')

    fireEvent.change(screen.getByLabelText('Nombre de resultats maximum'), { target: { value: '1' } })
    expect(synthese(container)).toBe('1 sur 1 maximum · 0 publique · 0 supprimee')
    // Une saisie illisible garde la derniere valeur lisible.
    fireEvent.change(screen.getByLabelText('Nombre de resultats maximum'), { target: { value: '' } })
    expect(synthese(container)).toBe('1 sur 1 maximum · 0 publique · 0 supprimee')
  })

  it('dit qu\'aucune audience ne repond, sur toute la largeur', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2027-01-01' } })
    const vide = screen.getByText('Aucune audience ne repond a ces criteres.')
    expect(vide.getAttribute('colspan')).toBe(String(entetes(container).length))
    expect(vide.parentElement!.className).toBe('rig-table__ligne--inerte')
    expect(synthese(container)).toBe('0 sur 100 maximum · 0 publique · 0 supprimee')
  })

  it('les vues preregent le type d\'audience ; « Aller a une audience… » (Ctrl K) filtre la liste affichee', () => {
    const { container } = render(<EcranGestionAudiences />)
    const groupe = screen.getByRole('group', { name: 'Vues' })
    expect([...groupe.querySelectorAll('.rig-lm-vue')].map((v) => espaces(v.textContent)))
      .toEqual(['Toutes 5', 'Audience de procedure 2', 'Audience publique 2', 'Cabinet du juge 1'])
    fireEvent.click(within(groupe).getByRole('button', { name: 'Audience publique 2' }))
    expect((screen.getByLabelText('Type d\'audience') as HTMLSelectElement).value).toBe('Audience publique')
    expect(synthese(container)).toBe('2 sur 100 maximum · 2 publiques · 1 supprimee')
    fireEvent.click(within(groupe).getByRole('button', { name: 'Toutes 5' }))
    expect((screen.getByLabelText('Type d\'audience') as HTMLSelectElement).value).toBe('')

    const champ = screen.getByLabelText('Aller a une audience…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'faure' } })
    expect(rangees(container).map((r) => r.querySelectorAll('td')[1]!.textContent)).toEqual(['Mardi'])
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['1 audience affichee', 'Publiques 0', 'Supprimees 0'])
    // Sans casse ni accents, comme la Recherche d'affaires : « procédure » trouve les deux « Audience de procedure ».
    fireEvent.change(champ, { target: { value: 'procédure' } })
    expect(rangees(container).map((r) => r.querySelectorAll('td')[1]!.textContent)).toEqual(['Lundi', 'Vendredi'])
  })
})
