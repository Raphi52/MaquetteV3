import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranConsultationJud } from './EcranConsultationJud'
import { contexteDossier } from '../test/dossier'

afterEach(cleanup)

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese sous le titre de la liste : le compte, puis les en attente et les rejets. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]

describe('EcranConsultationJud', () => {
  it('liste les quatre enrolements, une ligne par affaire, le rejete surligne ; le reste dans la ligne depliee', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Enrolements')
    expect(synthese(container)).toBe('4 affaires · 2 en attente · 1 rejet')
    const lignes = rangees(container)
    expect(lignes).toHaveLength(4)
    const rejetee = lignes[2]!
    expect(rejetee.querySelector('.rig-lm-avatar')!.textContent).toBe('CM')
    expect(rejetee.querySelector('.rig-lm-identite__nom')!.textContent).toBe('EURL COIFFURE MARIE')
    expect(rejetee.querySelector('.rig-lm-identite__objet')!.textContent).toBe('2026J00140')
    const pastille = rejetee.querySelector('.rig-lm-pastille')!
    expect([pastille.textContent, pastille.className]).toEqual(['Dematerialise', 'rig-lm-pastille rig-lm-t-bleu'])
    const etat = rejetee.querySelector('.rig-lm-etat')!
    expect([etat.textContent, etat.className]).toEqual(['Rejete', 'rig-lm-etat rig-lm-t-rouge'])
    expect([...rejetee.querySelectorAll('td')].slice(4, 6).map((td) => td.textContent)).toEqual(['02/06/2026', '3'])
    expect(rejetee.className).toBe('rig-table__ligne--alerte')
    expect(lignes[0]!.className).toBe('')

    // Type de rejet et date de traitement passent dans la ligne depliee par le chevron.
    fireEvent.click(within(rejetee).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = rejetee.nextElementSibling!
    expect(detail.className).toBe('rig-lm-detail')
    expect(detail.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect([...detail.querySelectorAll('.rig-lm-champ')].map((c) => [
      c.querySelector('.rig-lm-champ__libelle')!.textContent, c.querySelector('.rig-lm-champ__valeur')!.textContent]))
      .toEqual([['Type de rejet', 'Piece manquante'], ['Date traitement', '03/06/2026'], ['Dossier', '2023B01120'],
                ['Anomalies', 'Acte de saisine absent du depot']])
    expect(rejetee.className).toBe('rig-table__ligne--alerte rig-lm-ouverte')

    // Une affaire non traitee n'a pas de date de traitement.
    fireEvent.click(within(lignes[1]!).getByRole('button', { name: 'Deplier la ligne' }))
    const enAttente = lignes[1]!.nextElementSibling!
    expect(enAttente.querySelectorAll('.rig-lm-champ__valeur')[1]!.textContent).toBe('—')
    // Une ligne depliee a la fois.
    expect(container.querySelectorAll('.rig-lm-detail')).toHaveLength(1)
  })

  it('filtre par texte sans casse, par etat et par « Rejets uniquement »', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Aller a une affaire…'), { target: { value: '  marne ' } })
    expect(synthese(container)).toBe('1 affaire · 1 en attente · 0 rejet')
    fireEvent.change(screen.getByLabelText('Aller a une affaire…'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Etat de traitement'), { target: { value: 'En attente' } })
    expect(synthese(container)).toBe('2 affaires · 2 en attente · 0 rejet')
    fireEvent.change(screen.getByLabelText('Etat de traitement'), { target: { value: '' } })

    fireEvent.click(screen.getByRole('button', { name: 'Rejets uniquement' }))
    expect(synthese(container)).toBe('1 affaire · 0 en attente · 1 rejet')
  })

  it('« Traitement jusqu\'au » ecarte les affaires non traitees', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Traitement jusqu\'au'), { target: { value: '2026-12-31' } })
    expect(synthese(container)).toBe('2 affaires · 0 en attente · 1 rejet')
  })

  it('une ligne cliquee montre son detail ; la piece remonte le dossier, le XML se deplie', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranConsultationJud ouvrirDossier={ouvrirDossier} />)
    expect(screen.queryByText(/Detail de/)).toBeNull()

    fireEvent.click(screen.getByText('2026J00140'))
    expect(screen.getByText('Detail de 2026J00140')).toBeTruthy()
    expect((screen.getByLabelText('Anomalies') as HTMLTextAreaElement).value).toBe('Acte de saisine absent du depot')
    expect((screen.getByLabelText('Motif de rejet') as HTMLTextAreaElement).value).toBe('Dossier incomplet')

    fireEvent.click(screen.getByRole('button', { name: 'Visualiser la piece' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2023B01120')

    expect(container.querySelector('pre.rig-code')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Visualiser le XML' }))
    expect(container.querySelector('pre.rig-code')!.textContent).toBe('<enrolement num="2026J00140" />')
  })

  it('le chevron deplie sans choisir la ligne ; la ligne depliee et le menu « … » portent la piece et le XML', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranConsultationJud ouvrirDossier={ouvrirDossier} />)
    const rangee = screen.getByText('SAS TRANSPORTS MARNE').closest('tr')!
    fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
    expect(screen.queryByText(/Detail de/)).toBeNull()
    const detail = rangee.nextElementSibling as HTMLElement
    fireEvent.click(within(detail).getByRole('button', { name: 'Visualiser la piece' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00407')
    fireEvent.click(within(detail).getByRole('button', { name: 'Visualiser le XML' }))
    expect(screen.getByText('Detail de 2026J00134')).toBeTruthy()
    expect(container.querySelector('pre.rig-code')!.textContent).toBe('<enrolement num="2026J00134" />')

    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'affaire 2026J00121' }))
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Visualiser la piece', 'Visualiser le XML', 'Deplier la ligne'])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Visualiser la piece' }))
    expect(ouvrirDossier).toHaveBeenLastCalledWith('2024B00318')
  })

  // Un seul champ pour un seul filtre (decision du plan des listes, lot 7) : « Aller a une affaire… » EST
  // l'ancien critere « Affaire / dossier ». S'il redevenait un second filtre pose sur la liste affichee, le
  // compte des vues (« Toutes 1 » ci-dessous) resterait a 4 et l'ancien champ de gauche reviendrait.
  it('« Aller a une affaire… » (Ctrl K) est le critere de texte : numero ou denomination, vues comprises ; Entree choisit l\'affaire restee seule', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    expect(screen.queryByLabelText('Affaire / dossier')).toBeNull()
    const champ = screen.getByLabelText('Aller a une affaire…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)

    fireEvent.change(champ, { target: { value: 'moulin' } })
    expect(rangees(container).map((r) => r.querySelector('b')!.textContent)).toEqual(['SCI DU MOULIN'])
    expect(espaces(screen.getByRole('button', { name: /^Toutes/ }).textContent)).toBe('Toutes 1')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Detail de 2026J00155')).toBeTruthy()

    // Le numero d'affaire est cherche ; le numero du dossier lie ne l'est pas (memes colonnes que le critere v2).
    fireEvent.change(champ, { target: { value: '2026j00140' } })
    expect(rangees(container).map((r) => r.querySelector('b')!.textContent)).toEqual(['EURL COIFFURE MARIE'])
    fireEvent.change(champ, { target: { value: '2023B01120' } })
    expect(screen.getByText('Aucune affaire ne repond a ces criteres.')).toBeTruthy()
  })

  it('les vues et les cartes posent les criteres d\'etat de gauche', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    const vues = () => [...container.querySelectorAll('.rig-lm-vue')].map((v) => [espaces(v.textContent), v.getAttribute('aria-pressed')])
    expect(vues()).toEqual([['Toutes 4', 'true'], ['En attente 2', 'false'], ['Rejets 1', 'false'], ['Non dematerialisees 1', 'false']])

    const groupe = screen.getByRole('group', { name: 'Vues' })
    fireEvent.click(within(groupe).getByRole('button', { name: 'En attente 2' }))
    expect((screen.getByLabelText('Etat de traitement') as HTMLSelectElement).value).toBe('En attente')
    expect(synthese(container)).toBe('2 affaires · 2 en attente · 0 rejet')

    fireEvent.click(within(groupe).getByRole('button', { name: 'Rejets 1' }))
    expect((screen.getByLabelText('Etat de traitement') as HTMLSelectElement).value).toBe('')
    expect(screen.getByRole('button', { name: 'Rejets uniquement' }).className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.click(within(groupe).getByRole('button', { name: 'Toutes 4' }))
    expect(synthese(container)).toBe('4 affaires · 2 en attente · 1 rejet')

    // Cartes : la legende du traitement pose l'etat, la carte des rejets la bascule.
    const cartes = container.querySelectorAll('.rig-lm-carte')
    expect([...cartes].map((c) => espaces(c.querySelector('.rig-lm-carte__valeur')!.textContent))).toEqual(['4 affaires', '1 sur 4', '2 sur 4'])
    fireEvent.click(cartes[0]!.querySelector('.rig-lm-legende__entree.rig-lm-t-vert')!)
    expect((screen.getByLabelText('Etat de traitement') as HTMLSelectElement).value).toBe('Traite')
    expect(synthese(container)).toBe('1 affaire · 0 en attente · 0 rejet')
    fireEvent.click(cartes[0]!)
    expect((screen.getByLabelText('Etat de traitement') as HTMLSelectElement).value).toBe('')
    fireEvent.click(cartes[1]!)
    expect(screen.getByRole('button', { name: 'Rejets uniquement' }).className).toBe('rig-bascule rig-bascule--actif')
  })

  it('le pied compte les affaires affichees, leurs pieces et les en attente', () => {
    const { container } = render(<EcranConsultationJud ouvrirDossier={vi.fn()} />)
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['4 affaires affichees', 'Pieces 20', 'En attente 2'])
  })
})

// Ouvert depuis la page d'une affaire judiciaire : cherchee par son numero dans
// « Aller a une affaire… », l'ancien critere « Affaire / dossier » (plan « Vue du dossier », lot 2).
describe('EcranConsultationJud ouvert depuis une affaire', () => {
  it('ne montre qu\'elle, le critere deja rempli ; sans affaire, les quatre', () => {
    const avec = render(<EcranConsultationJud ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '2026J00134', type: 'JUD' })} />)
    expect(synthese(avec.container)).toBe('1 affaire · 1 en attente · 0 rejet')
    expect(screen.getByText('SAS TRANSPORTS MARNE')).toBeTruthy()
    expect((screen.getByLabelText('Aller a une affaire…') as HTMLInputElement).value).toBe('2026J00134')
    avec.unmount()

    const sans = render(<EcranConsultationJud ouvrirDossier={vi.fn()} dossier={null} />)
    expect(synthese(sans.container)).toBe('4 affaires · 2 en attente · 1 rejet')
    expect((screen.getByLabelText('Aller a une affaire…') as HTMLInputElement).value).toBe('')
  })
})
