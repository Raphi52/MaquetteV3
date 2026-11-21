import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranRechAffaires, type LigneAffaire, type VueAffaires } from './EcranRechAffaires'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le fonds d'affaires, le filtre et le tri sont faits par Pont/EcranRechAffaires.cs,
// executes par tests/TriCheck, OuvertureCheck et GedCheck. Ici : l'ecran affiche
// ce que le pont rend, lui transmet les criteres saisis, et remonte les gestes.
const ligne = (numero: string, nature = 'instance'): LigneAffaire => ({
  numero, idInstance: '55339', role: '2026F00301', codeAffaire: '010', nature, nomNature: 'Instance',
  libelle: `Affaire ${numero}`, nom: 'AMIENS BILLARDS', etat: 'En cours', siren: '109 678 235', type: 'RCS',
  saisine: '30/08/2026', creation: '01/09/2026', solde: '142,50',
})

function vue(c: unknown, _cle: unknown, _asc: unknown, selection: unknown): VueAffaires {
  const criteres = c as Record<string, unknown>
  const numero = String(criteres.critNumero ?? '')
  const lignes = numero === 'ZZZ' ? [] : [ligne('2026B01067'), ligne('004176')]
  return {
    natures: [{ code: 'instance', nom: 'Instance' }], etats: ['En cours', 'Radiee'],
    lignes, resume: lignes.length ? `${lignes.length} instance` : '',
    aucunCritere: Object.values(criteres).every((v) => v === '' || v === null),
    sirenCherche: String(criteres.critLibelle ?? '').replace(/\D/g, '').length === 9 ? String(criteres.critLibelle).replace(/\D/g, '') : '',
    choisie: selection ? { numero: String(selection), idInstance: '55339', role: '2026F00301' } : null,
  }
}

describe('EcranRechAffaires', () => {
  it('affiche les affaires rendues par le pont, colonnes et resume compris', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)

    await screen.findByText('Resultats (2) — 2 instance')
    expect(pont.appels.mock.calls[0]![0]).toBe('EcranRechAffaires.Trouver')
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees).toHaveLength(2)
    expect(rangees[0]!.querySelectorAll('td')).toHaveLength(13)
    expect(rangees[0]!.lastElementChild!.textContent).toBe('142,50 €')
    expect(container.querySelectorAll('th.rig-th--triable')).toHaveLength(13)
  })

  it('pre-remplit le numero d\'affaire recu de l\'accueil', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires noAffaire=" 004176 " ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/Resultats/)
    expect((pont.appels.mock.calls[0]![1] as { critNumero: string }).critNumero).toBe('004176')
  })

  it('double-clic : remonte le numero de l\'affaire a ouvrir', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    const ouvrir = vi.fn()
    render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    fireEvent.doubleClick(await screen.findByText('004176'))
    expect(ouvrir).toHaveBeenCalledWith('004176')
  })

  it('un clic sur un en-tete demande le tri au pont, un second l\'inverse', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/Resultats/)
    fireEvent.click(screen.getByText('Nom'))
    await waitFor(() => expect(pont.appels.mock.lastCall!.slice(2, 4)).toEqual(['Nom', true]))
    fireEvent.click(screen.getByText('Nom'))
    await waitFor(() => expect(pont.appels.mock.lastCall!.slice(2, 4)).toEqual(['Nom', false]))
  })

  it('recherche restee vide : « Ouvrir un dossier » emporte le SIREN cherche', async () => {
    installerFauxPont({
      'EcranRechAffaires.Trouver': (c: unknown, ...r: unknown[]) =>
        ({ ...vue(c, ...(r as [unknown, unknown, unknown])), lignes: [], resume: '' }),
    })
    const ouvrirUnDossier = vi.fn()
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={ouvrirUnDossier} />)
    expect(await screen.findByText('Saisissez au moins un critere, puis lancez la recherche.')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Libelle, SIREN'), { target: { value: '512 345 678' } })
    await screen.findByText('Aucune affaire ne repond a ces criteres.')
    const pourLeSiren = await screen.findByText('pour le SIREN 512345678')
    expect(morceaux(pourLeSiren)).toEqual([' pour le SIREN ', '512345678'])
    expect(morceaux(pourLeSiren.parentElement)).toEqual(['<button>', '<span>'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir un dossier' }))
    expect(ouvrirUnDossier).toHaveBeenCalledWith('512345678')
  })

  // SOC-015 (lot 3 de la phase 2) : l'aiguillage vient de Pont/AncienRig.cs (banc tests/SocleAiguillageCheck).
  const aiguiller = (numeros: unknown) => ({
    mode: 'demo', greffe: null, passage: '01/08/2026 08:00', message: null,
    affaires: (numeros as string[]).map((numero) => numero === '004176'
      ? { numero, logiciel: 'V2', motif: 'creee le 24/08/2026 00:00, avant le passage du 01/09/2026 08:00 : traitee dans la V2' }
      : { numero, logiciel: 'V3', motif: 'traitee dans la V3' }),
  })

  it('SOC-015 CA01 : une affaire creee avant le passage porte la mention « traitee dans la V2 », les autres non', async () => {
    const pont = installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    const mention = await screen.findByText('traitee dans la V2')
    expect(mention.closest('tr')!.firstElementChild!.firstChild!.textContent).toBe('004176')
    expect(container.querySelectorAll('.rig-badge')).toHaveLength(1)
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')).toHaveLength(13)
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.Aiguiller')![1]).toEqual(['004176', '2026B01067'])

    // Un tri ne change pas la liste des numeros : l'aiguillage n'est pas redemande.
    fireEvent.click(screen.getByText('Nom'))
    await waitFor(() => expect(pont.appels.mock.lastCall!.slice(2, 4)).toEqual(['Nom', true]))
    expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranRechAffaires.Aiguiller')).toHaveLength(1)
  })

  it('SOC-015 CA02, CA03 : une ancienne affaire choisie propose « Ouvrir dans l\'ancien RIG », qui rend compte du geste', async () => {
    const pont = installerFauxPont({
      'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller,
      'EcranRechAffaires.OuvrirDansAncienRig': (numero: unknown) => ({ fait: true, geste: 'lance', numero, ligneDeCommande: '"C:\\RIG\\exe\\RigClientAccueil.exe" /g8002',
        message: `Le numero ${String(numero)} est copie : collez-le dans « N° affaire » de la recherche d'affaires de l'ancien RIG.` }),
    })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText('traitee dans la V2')
    // Une affaire de la V3 choisie : pas de bloc « Ancien RIG », l'ecran est celui d'avant.
    fireEvent.click(screen.getByText('2026B01067'))
    await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')
    expect(screen.queryByRole('button', { name: 'Ouvrir dans l\'ancien RIG' })).toBeNull()

    fireEvent.click(screen.getByText('004176'))
    fireEvent.click(await screen.findByRole('button', { name: 'Ouvrir dans l\'ancien RIG' }))
    expect((await screen.findByRole('status')).textContent).toContain('Le numero 004176 est copie')
    expect(pont.appels.mock.calls.find((c) => c[0] === 'EcranRechAffaires.OuvrirDansAncienRig')!.slice(1)).toEqual(['004176'])
  })

  it('SOC-015 CA04 : la suppression d\'une ancienne affaire est refusee ; celle d\'une affaire de la V3 reste simulee', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue, 'EcranRechAffaires.Aiguiller': aiguiller })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText('traitee dans la V2')
    fireEvent.click(screen.getByText('004176'))
    await screen.findByRole('button', { name: 'Ouvrir dans l\'ancien RIG' })
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/affaire traitee dans la V2, elle ne se supprime pas ici\. Ouvrez-la dans l'ancien RIG\./)).toBeTruthy()
    expect(screen.queryByText(/suppression simulee/)).toBeNull()

    fireEvent.click(screen.getByText('2026B01067'))
    await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/suppression simulee — numeros metiers liberes\./)).toBeTruthy()
  })

  it('Entree dans les criteres et suppression simulee de l\'instance choisie', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    fireEvent.click(await screen.findByText('2026B01067'))
    expect(morceaux(await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')))
      .toEqual(['2026B01067', ' · instance ', '55339', ' · role ', '2026F00301'])
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/suppression simulee — numeros metiers liberes\./)).toBeTruthy()
    fireEvent.keyDown(screen.getByLabelText('N° Affaire'), { key: 'Enter' })
    expect(screen.queryByText(/suppression simulee/)).toBeNull()
  })

  it('F5 et F7 dans les criteres relancent la recherche comme Entree (touches de l\'ancien RIG)', async () => {
    installerFauxPont({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    fireEvent.click(await screen.findByText('2026B01067'))
    for (const touche of ['F5', 'F7']) {
      fireEvent.click(await screen.findByRole('button', { name: 'Supprimer l\'instance' }))
      expect(screen.getByText(/suppression simulee/)).toBeTruthy()
      expect(fireEvent.keyDown(screen.getByLabelText('N° Affaire'), { key: touche })).toBe(false)
      expect(screen.queryByText(/suppression simulee/)).toBeNull()
    }
  })
})
