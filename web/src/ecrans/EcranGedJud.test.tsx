import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { EcranGedJud, type VueGedJud } from './EcranGedJud'
import { contexteDossier } from '../test/dossier'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese sous le titre : le compte, les brouillons, les docs lies, puis l'action demandee. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]

describe('EcranGedJud', () => {
  it('liste les quatre documents, une ligne par document, sans date de decision pour le brouillon', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Decisions judiciaires et documents lies')
    expect(synthese(container)).toBe('4 documents · 1 brouillon · 7 docs lies')
    const brouillon = rangees(container)[2]!
    // Une decision est un document : cellule simple, sans avatar d'initiales ; l'instance a sa colonne.
    expect([...brouillon.querySelectorAll('td')].slice(1, 3).map((td) => td.textContent)).toEqual(['Jugement de cloture', '2025J00088'])
    expect(brouillon.querySelector('.rig-lm-avatar, .rig-lm-identite')).toBeNull()
    expect(brouillon.querySelector('.rig-lm-pastille')!.textContent).toBe('Jugement')
    expect([...brouillon.querySelectorAll('td')].slice(4, 6).map((td) => td.textContent)).toEqual(['', '0'])
    const etat = brouillon.querySelector('.rig-lm-etat')!
    expect([etat.textContent, etat.className]).toEqual(['Brouillon', 'rig-lm-etat rig-lm-t-orange'])
    expect(rangees(container)[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')

    // Numero de minute, date prevue et dossier passent dans la ligne depliee.
    fireEvent.click(within(brouillon).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = brouillon.nextElementSibling!
    expect(detail.querySelector('td')!.getAttribute('colspan')).toBe('8')
    expect([...detail.querySelectorAll('.rig-lm-champ')].map((c) => [
      c.querySelector('.rig-lm-champ__libelle')!.textContent, c.querySelector('.rig-lm-champ__valeur')!.textContent]))
      .toEqual([['Numero minute', '—'], ['Date prevue de la decision', '20/09/2026'], ['Dossier', '2023B01120']])
  })

  it('filtre par nature, par etat et par « Non lies a une decision »', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    expect([...(screen.getByLabelText('Nature du document') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Toutes', 'Copie', 'Jugement', 'Ordonnance'])
    fireEvent.change(screen.getByLabelText('Nature du document'), { target: { value: 'Jugement' } })
    expect(synthese(container)).toBe('2 documents · 1 brouillon · 4 docs lies')
    fireEvent.change(screen.getByLabelText('Etat'), { target: { value: 'Notifie' } })
    expect(synthese(container)).toBe('1 document · 0 brouillon · 4 docs lies')
    fireEvent.change(screen.getByLabelText('Nature du document'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Etat'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Non lies a une decision' }))
    expect(synthese(container)).toBe('1 document · 1 brouillon · 0 docs lies')
  })

  it('une action s\'inscrit a la suite de la synthese', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Lier a une decision' }))
    expect(synthese(container)).toBe('4 documents · 1 brouillon · 7 docs lies — Lier a une decision judiciaire')
  })

  it('un document clique ouvre ses actions et son historique ; « Afficher » remonte le dossier', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranGedJud ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(screen.getByText('2026J00121'))
    expect(screen.getByText('Jugement d\'ouverture — minute 2026/0412')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Historique' }))
    expect([...container.querySelectorAll('.rig-historique__liste li')].map((li) => li.textContent))
      .toEqual(['06/03/2026 — signe par le president', '07/03/2026 — notifie au mandataire'])

    fireEvent.click(screen.getByRole('button', { name: 'Imprimer' }))
    expect(synthese(container)).toBe('4 documents · 1 brouillon · 7 docs lies — Imprimer')

    fireEvent.click(screen.getByRole('button', { name: 'Afficher le document' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00318')
  })

  it('la ligne depliee et le menu « … » portent les actions du document ; le chevron ne choisit pas la ligne', () => {
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranGedJud ouvrirDossier={ouvrirDossier} />)
    const rangee = screen.getByText('Ordonnance de designation').closest('tr')!
    fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
    expect(container.querySelector('.rig-historique__liste')).toBeNull()
    expect(screen.queryByText(/— minute/)).toBeNull()
    const detail = rangee.nextElementSibling as HTMLElement
    expect(within(detail).getAllByRole('button').map((b) => b.textContent))
      .toEqual(['Afficher le document', 'Imprimer', 'Envoyer par e-mail', 'Envoyer par courrier', 'Historique'])
    fireEvent.click(within(detail).getByRole('button', { name: 'Afficher le document' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00407')
    fireEvent.click(within(detail).getByRole('button', { name: 'Envoyer par courrier' }))
    expect(synthese(container)).toBe('4 documents · 1 brouillon · 7 docs lies — Envoyer par courrier')
    // L'action d'une ligne vise SON document : il devient le document choisi (sans cela, « Envoyer par courrier »
    // de cette ligne aurait vise le document choisi avant, ou aucun).
    expect(screen.getByText('Ordonnance de designation — minute 2026/0518')).toBeTruthy()
    fireEvent.click(within(detail).getByRole('button', { name: 'Historique' }))
    expect([...container.querySelectorAll('.rig-historique__liste li')].map((li) => li.textContent))
      .toEqual(['02/06/2026 — signe par le juge-commissaire'])

    fireEvent.click(screen.getByRole('button', { name: 'Actions du document 2024J00507' }))
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(
      ['Afficher le document', 'Imprimer', 'Envoyer par e-mail', 'Envoyer par courrier', 'Historique', 'Deplier la ligne'])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Afficher le document' }))
    expect(ouvrirDossier).toHaveBeenLastCalledWith('2025B00044')
  })

  // Un seul champ pour un seul filtre (decision du plan des listes, lot 7) : « Aller a une decision… » EST l'ancien
  // critere « Instance / decision ». S'il redevenait un second filtre pose sur la liste affichee, le compte des vues
  // (« Tous 1 ») resterait a 4 ; s'il cherchait aussi la minute, « 2024/1188 » trouverait la copie conforme.
  it('« Aller a une decision… » (Ctrl K) est le critere instance/decision ; les vues et les cartes posent les criteres', () => {
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    expect(screen.queryByLabelText('Instance / decision')).toBeNull()
    const champ = screen.getByLabelText('Aller a une decision…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: ' copie ' } })
    expect(rangees(container).map((r) => r.querySelectorAll('td')[1]!.textContent)).toEqual(['Copie conforme'])
    expect(espaces(screen.getByRole('button', { name: /^Tous/ }).textContent)).toBe('Tous 1')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Copie conforme — minute 2024/1188')).toBeTruthy()
    fireEvent.change(champ, { target: { value: '2024j00507' } })
    expect(rangees(container).map((r) => r.querySelectorAll('td')[1]!.textContent)).toEqual(['Copie conforme'])
    fireEvent.change(champ, { target: { value: '2024/1188' } })
    expect(screen.getByText('Aucun document ne repond a ces criteres.')).toBeTruthy()
    fireEvent.change(champ, { target: { value: '' } })

    const groupe = screen.getByRole('group', { name: 'Vues' })
    expect([...groupe.querySelectorAll('.rig-lm-vue')].map((v) => espaces(v.textContent)))
      .toEqual(['Tous 4', 'Brouillons 1', 'Signes 1', 'Notifies 2', 'Non lies a une decision 1'])
    fireEvent.click(within(groupe).getByRole('button', { name: 'Notifies 2' }))
    expect((screen.getByLabelText('Etat') as HTMLSelectElement).value).toBe('Notifie')
    expect(within(groupe).getByRole('button', { name: 'Notifies 2' }).getAttribute('aria-pressed')).toBe('true')

    const cartes = container.querySelectorAll('.rig-lm-carte')
    fireEvent.click(cartes[0]!)
    expect((screen.getByLabelText('Etat') as HTMLSelectElement).value).toBe('')
    fireEvent.click(cartes[1]!.querySelector('.rig-lm-legende__entree.rig-lm-t-violet')!)
    expect((screen.getByLabelText('Nature du document') as HTMLSelectElement).value).toBe('Ordonnance')
    expect(synthese(container)).toBe('1 document · 0 brouillon · 2 docs lies')
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['1 document affiche', 'Docs lies 2', 'Non lies a une decision 0'])
  })
})

// Ouvert depuis la page d'une affaire judiciaire : ses documents, par son numero
// dans « Aller a une decision… », l'ancien critere « Instance / decision » (plan « Vue du dossier », lot 2).
describe('EcranGedJud ouvert depuis une affaire', () => {
  it('ne montre que ses documents, le critere deja rempli ; sans affaire, les quatre', () => {
    const avec = render(<EcranGedJud ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '2026J00134', type: 'JUD' })} />)
    expect(synthese(avec.container)).toBe('1 document · 0 brouillon · 2 docs lies')
    expect(screen.getByText('Ordonnance de designation')).toBeTruthy()
    expect((screen.getByLabelText('Aller a une decision…') as HTMLInputElement).value).toBe('2026J00134')
    avec.unmount()

    const sans = render(<EcranGedJud ouvrirDossier={vi.fn()} dossier={null} />)
    expect(synthese(sans.container)).toBe('4 documents · 1 brouillon · 7 docs lies')
    expect((screen.getByLabelText('Aller a une decision…') as HTMLInputElement).value).toBe('')
  })

  // Lot 11 : branche, les documents de la GED judiciaire du greffe (Pont/EcranGedJud.cs), les criteres de RIG v2 appliques.
  const doc = (id: string, libelle: string, codeEtat: string, etat: string, reception: string, nbLies: number, commentaire = '') => ({
    id, decision: libelle, minute: '', instance: '2004J00007', typeEntite: 'Instance', nature: 'Assignation', dateDecision: nbLies > 0 ? '2004-06-02' : null,
    datePrevue: '', nbLies, etat, codeEtat, statut: codeEtat === 'R' ? 'alerte' : 'ok', dossier: '2004J00007', reception, commentaire,
    motifRejet: codeEtat === 'R' ? 'Illisible' : '', historiques: [],
  })
  const base: VueGedJud = {
    mode: 'serveur', message: null, total: 3, lignes: [
      doc('11', 'Assignation du 01/06', 'V', 'Validé', '2004-06-01', 1, 'piece originale'),
      doc('12', 'Conclusions', 'A', 'En attente', '2004-06-03', 0),
      doc('13', 'Scan illisible', 'R', 'Rejeté', '2004-06-03', 0),
    ],
  }

  it('branche : les documents de la base, un rejete cache sauf « Afficher tout » (OPE_RECHERCHE_DOC.cs:376)', async () => {
    const pont = installerFauxPont({ 'EcranGedJud.Documents': () => base })
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(rangees(container).map((r) => r.cells[1]!.textContent)).toEqual(['Assignation du 01/06', 'Conclusions']))
    expect(pont.appels).toHaveBeenCalledWith('EcranGedJud.Documents')
    expect(screen.queryByText(/de démonstration/)).toBeNull()
    // RIG v2 n'a pas de brouillon : la synthese de la base ne le compte pas.
    expect(synthese(container)).toBe('2 documents · 1 docs lies')
    // Les etats de RIG v2 lus deviennent les choix du critere et les vues, dans l'ordre V, VR, A, R.
    expect([...(screen.getByLabelText('Etat') as HTMLSelectElement).options].map((o) => o.value)).toEqual(['', 'Validé', 'En attente', 'Rejeté'])

    fireEvent.click(screen.getByRole('button', { name: 'Afficher tout' }))
    expect(rangees(container).map((r) => r.cells[1]!.textContent)).toEqual(['Assignation du 01/06', 'Conclusions', 'Scan illisible'])

    fireEvent.click(screen.getByRole('button', { name: 'Non lies a une decision' }))
    expect(rangees(container).map((r) => r.cells[1]!.textContent)).toEqual(['Conclusions', 'Scan illisible'])
  })

  it('branche : la date de reception seule vaut pour ce jour, le commentaire est cherche (OPE_RECHERCHE_DOC.cs:342-373)', async () => {
    installerFauxPont({ 'EcranGedJud.Documents': () => base })
    const { container } = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(rangees(container)).toHaveLength(2))
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2004-06-03' } })
    expect(rangees(container).map((r) => r.cells[1]!.textContent)).toEqual(['Conclusions'])
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2004-06-01' } })
    fireEvent.change(screen.getByLabelText('Au'), { target: { value: '2004-06-03' } })
    expect(rangees(container)).toHaveLength(2)
    fireEvent.change(screen.getByLabelText('Commentaire'), { target: { value: 'ORIGINALE' } })
    expect(rangees(container).map((r) => r.cells[1]!.textContent)).toEqual(['Assignation du 01/06'])
  })

  it('sans serveur : la demonstration, marquee « exemple » ; en panne : le motif et aucun document', async () => {
    installerFauxPont({ 'EcranGedJud.Documents': () => ({ mode: 'demo', lignes: [], total: 0 }) })
    const demo = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(screen.getByText(/Documents de démonstration/)).toBeTruthy())
    expect(rangees(demo.container)).toHaveLength(4)
    demo.unmount(); retirerFauxPont()

    installerFauxPont({ 'EcranGedJud.Documents': () => ({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', lignes: [], total: 0 }) })
    const panne = render(<EcranGedJud ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('La base du greffe 3801 ne répond pas.'))
    expect(rangees(panne.container).map((r) => r.textContent)).toEqual(['Aucun document ne repond a ces criteres.'])
  })
})
