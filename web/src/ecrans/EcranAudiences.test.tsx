import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranAudiences } from './EcranAudiences'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La demonstration du pont (Pont/EcranAudiences.cs, Exemple) telle qu'il la serialise : les trois audiences et les listes que
// l'ecran declarait (capture de l'ecran Blazor, 18/11/2026) ; date proposee 16/09/2026 (JeuDemo.Aujourdhui + 7).
const exemple = {
  mode: 'demo', message: null, total: 3,
  lignes: [
    { date: '2026-09-15', heure: '09:00', type: 'Audience de procedure collective', chambre: '1re', section: 'A', publique: true, web: true, nbAffaires: 14 },
    { date: '2026-09-22', heure: '10:15', type: 'Audience du juge-commissaire', chambre: '1re', section: 'A', publique: false, web: false, nbAffaires: 6 },
    { date: '2026-09-29', heure: '14:00', type: 'Audience de plaidoirie', chambre: '2e', section: 'B', publique: true, web: true, nbAffaires: 9 },
  ],
  types: ['Audience de procedure collective', 'Audience de reference', 'Audience de plaidoirie', 'Audience de conciliation', 'Audience du juge-commissaire'],
  chambres: ['1re', '2e', '3e'], sections: ['A', 'B', 'C'],
  classements: ['Insuffisance d\'actif', 'Extinction du passif', 'Conversion en liquidation', 'Report'],
}

/** L'ecran monte, une fois la date proposee et la vue du pont arrivees. */
async function ouvrir(vue: unknown = exemple) {
  const pont = installerFauxPont({ 'EcranAudiences.DateProposee': () => '2026-09-16', 'EcranAudiences.Vue': () => vue })
  const rendu = render(<EcranAudiences />)
  await screen.findByDisplayValue('2026-09-16')
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.Vue'))
  await vi.waitFor(() => expect(rendu.container.querySelector('.rig-eg-note')).not.toBeNull())
  return { pont, ...rendu }
}

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese : le compte, les publiques, les affaires. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]
const lignes = (c: HTMLElement) => rangees(c).map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
/** Section et « Publiee sur le web » d'une rangee : dans sa ligne depliee. */
function deplier(rangee: HTMLTableRowElement): string[][] {
  fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
  return [...rangee.nextElementSibling!.querySelectorAll('.rig-lm-champ')].map((c) => [
    c.querySelector('.rig-lm-champ__libelle')!.textContent!, c.querySelector('.rig-lm-champ__valeur')!.textContent!])
}

describe('EcranAudiences', () => {
  it('affiche les audiences programmees et la date proposee par le pont', async () => {
    const { container, pont } = await ouvrir()
    expect(screen.getByDisplayValue('2026-09-16')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.DateProposee')
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Audiences programmees')
    expect(synthese(container)).toBe('3 audiences · 2 publiques · 29 affaires')
    expect(lignes(container)[0]).toEqual(['', '15/09/2026', '09:00', 'Audience de procedure collective', '1re', 'oui', '14'])
    expect(lignes(container)[1]).toEqual(['', '22/09/2026', '10:15', 'Audience du juge-commissaire', '1re', 'non', '6'])
    const premiere = rangees(container)[0]!
    expect(premiere.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-orange')
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(rangees(container)[1]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-gris')
    expect(deplier(premiere)).toEqual([['Section', 'A'], ['Publiee sur le web', 'oui']])
    expect(premiere.nextElementSibling!.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect(deplier(rangees(container)[1]!)).toEqual([['Section', 'A'], ['Publiee sur le web', '—']])
    // Une ligne depliee a la fois.
    expect(container.querySelectorAll('.rig-lm-detail')).toHaveLength(1)
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['3 audiences affichees', 'Publiques 2', 'Affaires 29'])
    // Le @bind d'un TimeOnly ecrivait « HH:mm:ss ».
    expect((container.querySelector('input[type="time"]') as HTMLInputElement).value).toBe('09:00:00')
    expect(screen.getByText('Envoi de mail actif').className).toBe('rig-etat rig-etat--ok')
    // Sans serveur, la demonstration est dite.
    expect(screen.getByRole('note').textContent).toContain('Audiences de démonstration')
  })

  it('les bascules s\'allument et s\'eteignent : publique allumee, classement admis par defaut', async () => {
    await ouvrir()
    const publique = screen.getByRole('button', { name: 'Audience publique' })
    // La case de RIG v2 : « Non disponible dans les choix d'Audience WEB », decochee par defaut (lot 9 du parcours complet).
    const web = screen.getByRole('button', { name: 'Non disponible dans les choix d\'audience web' })
    expect(publique.className).toBe('rig-bascule rig-bascule--actif')
    expect(web.className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Insuffisance d\'actif' }).className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.click(web)
    fireEvent.click(screen.getByRole('button', { name: 'Report' }))
    expect(web.className).toBe('rig-bascule rig-bascule--actif')
    expect(screen.getByRole('button', { name: 'Report' }).className).toBe('rig-bascule rig-bascule--actif')
  })

  it('« Creer l\'audience » n\'ajoute rien et le dit : sans serveur, la demonstration', async () => {
    const { container } = await ouvrir()
    fireEvent.change(container.querySelector('input[type="time"]')!, { target: { value: '14:30' } })
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '3e' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))

    expect(screen.getByRole('status').textContent).toBe('Démonstration : rien n\'est enregistré.')
    expect(synthese(container)).toBe('3 audiences · 2 publiques · 29 affaires')
  })

  // La base du greffe : les listes portent leur code (options du pont), la section se choisit dans celles de la chambre (RIG v2).
  const enBase = (lignes: unknown[]) => ({
    mode: 'serveur', message: null, total: lignes.length, lignes,
    types: ['Audience contentieuse'], chambres: ['Chambre 1', 'Plaidoiries'], sections: ['audience interactive', 'plaidoirie lundi'],
    classements: ['Renvoi'],
    options: [
      { liste: 'type', code: 'CX', libelle: 'Audience contentieuse' },
      { liste: 'chambre', code: 'INT', libelle: 'Chambre 1' }, { liste: 'chambre', code: 'PLD', libelle: 'Plaidoiries' },
      { liste: 'section', code: 'INT.LU', libelle: 'audience interactive' }, { liste: 'section', code: 'INT.VE', libelle: 'audience interactive' },
      { liste: 'section', code: 'PLD.LU', libelle: 'plaidoirie lundi' },
      { liste: 'classement', code: 'RE', libelle: 'Renvoi' },
    ],
  })
  const existante = { date: '2026-12-01', heure: '14:00', type: 'Audience contentieuse', chambre: 'Chambre 1', section: '', publique: true, web: false, nbAffaires: 3 }

  it('branche a la base : ses listes par code, les sections de la chambre, aucune mention d\'exemple', async () => {
    const pont = installerFauxPont({ 'EcranAudiences.DateProposee': () => '2026-09-16', 'EcranAudiences.Vue': () => enBase([existante]) })
    const { container } = render(<EcranAudiences />)
    await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.Vue'))
    await vi.waitFor(() => expect(lignes(container)).toEqual([['', '01/12/2026', '14:00', 'Audience contentieuse', 'Chambre 1', 'oui', '3']]))
    expect(screen.queryByRole('note')).toBeNull()
    expect([...(screen.getByLabelText('Chambre') as HTMLSelectElement).options].map((o) => [o.value, o.text]))
      .toEqual([['INT', 'Chambre 1'], ['PLD', 'Plaidoiries']])
    // Les deux sections « audience interactive » de la chambre INT, distinguees par leur code ; PLD.LU n'est pas de cette chambre.
    expect([...(screen.getByLabelText('Section') as HTMLSelectElement).options].map((o) => o.value)).toEqual(['', 'INT.LU', 'INT.VE'])
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: 'PLD' } })
    expect([...(screen.getByLabelText('Section') as HTMLSelectElement).options].map((o) => o.value)).toEqual(['', 'PLD.LU'])
    expect(screen.getByRole('button', { name: 'Renvoi' }).className).toBe('rig-bascule rig-bascule--actif')
  })

  it('« Creer l\'audience » : la saisie part au pont en codes ; faite, la liste relit la base et montre l\'audience', async () => {
    let creee = false
    const creer = vi.fn(() => { creee = true; return { fait: true, message: 'Audience créée : Audience contentieuse du 16/09/2026 à 10:30, Chambre 1.', sansEcrivain: false, ids: ['77'] } })
    const pont = installerFauxPont({
      'EcranAudiences.DateProposee': () => '2026-09-16',
      'EcranAudiences.Vue': () => enBase(creee
        ? [existante, { date: '2026-09-16', heure: '10:30', type: 'Audience contentieuse', chambre: 'Chambre 1', section: 'audience interactive', publique: true, web: true, nbAffaires: 0 }]
        : [existante]),
      'EcranAudiences.Creer': creer,
    })
    const { container } = render(<EcranAudiences />)
    await screen.findByDisplayValue('2026-09-16')
    await vi.waitFor(() => expect(lignes(container)).toHaveLength(1))
    fireEvent.change(container.querySelector('input[type="time"]')!, { target: { value: '10:30' } })
    fireEvent.change(screen.getByLabelText('Section'), { target: { value: 'INT.VE' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))
    expect((await screen.findByRole('status')).textContent).toBe('Audience créée : Audience contentieuse du 16/09/2026 à 10:30, Chambre 1.')
    expect(creer).toHaveBeenCalledWith({ date: '2026-09-16', heure: '10:30', type: 'CX', chambre: 'INT', section: 'INT.VE', classements: ['RE'],
                                         publique: true, nonDisponibleWeb: false })
    await vi.waitFor(() => expect(lignes(container).map((l) => [l[1], l[2]])).toEqual([['01/12/2026', '14:00'], ['16/09/2026', '10:30']]))
    expect(pont.appels.mock.calls.filter((a) => a[0] === 'EcranAudiences.Vue')).toHaveLength(2)
  })

  it('« Creer l\'audience » refusee par RIG v2 : le motif, rien d\'ajoute ni de relu', async () => {
    const pont = installerFauxPont({
      'EcranAudiences.DateProposee': () => '2026-09-16',
      'EcranAudiences.Vue': () => enBase([existante]),
      'EcranAudiences.Creer': () => ({ fait: false, message: 'L\'audience saisie existe déjà. Merci de vérifier votre saisie.', sansEcrivain: false, ids: [] }),
    })
    const { container } = render(<EcranAudiences />)
    await vi.waitFor(() => expect(lignes(container)).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Refus L\'audience saisie existe déjà. Merci de vérifier votre saisie.')
    expect(synthese(container)).toBe('1 audience · 1 publique · 3 affaires')
    expect(pont.appels.mock.calls.filter((a) => a[0] === 'EcranAudiences.Vue')).toHaveLength(1)
  })

  it('base injoignable : le motif, aucune audience ni liste', async () => {
    const pont = installerFauxPont({
      'EcranAudiences.DateProposee': () => '2026-09-16',
      'EcranAudiences.Vue': () => ({ mode: 'erreur', message: 'Le serveur ne répond pas.', total: 0, lignes: [], types: [], chambres: [], sections: [], classements: [] }),
    })
    const { container } = render(<EcranAudiences />)
    await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.Vue'))
    expect((await screen.findByRole('alert')).textContent).toContain('Le serveur ne répond pas.')
    expect(synthese(container)).toBe('0 audience · 0 publique · 0 affaire')
    expect((screen.getByLabelText('Chambre') as HTMLSelectElement).options).toHaveLength(0)
  })

  it('sans date d\'audience, rien n\'est cree', async () => {
    const { container } = await ouvrir()
    fireEvent.change(container.querySelector('input[type="date"]')!, { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))
    expect(synthese(container)).toBe('3 audiences · 2 publiques · 29 affaires')
  })

  it('« Aller a une audience… » (Ctrl K) filtre la liste affichee', async () => {
    const { container } = await ouvrir()
    const champ = screen.getByLabelText('Aller a une audience…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'plaidoirie' } })
    expect(lignes(container).map((l) => l[1])).toEqual(['29/09/2026'])
    expect(synthese(container)).toBe('1 audience · 1 publique · 9 affaires')
    // Sans casse ni accents, comme la Recherche d'affaires : « PROCÉDURE » trouve « Audience de procedure collective ».
    fireEvent.change(champ, { target: { value: 'PROCÉDURE' } })
    expect(lignes(container).map((l) => l[1])).toEqual(['15/09/2026'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 3 audiences ne contient « zzz ».')).toBeTruthy()
  })
})
