import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranVerrous, societeEtObjet, type LigneVerrou, type VueVerrous } from './EcranVerrous'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

/** La synthese de la tete de liste (« 2 dossier(s) · 2 detenteur(s) · plus ancien 11 j »). */
const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')?.textContent ?? ''

// Le calcul (filtre, tri, age, detenteurs) est fait par Pont/EcranVerrous.cs
// et execute par tests/AlertesCheck. Ici : l'ecran AFFICHE ce que le pont rend,
// et lui transmet le bon login et la bonne bascule.
const lignes: LigneVerrou[] = [
  { dossier: '004151', nom: 'Depot de bilan - TRANSNORD SA', statut: 'alerte', etat: 'En controle', utilisateur: 'C. BENIER', age: 11 },
  { dossier: '004176', nom: 'Modification - SCI DU PARC', statut: 'alerte', etat: 'En controle', utilisateur: 'M. DURAND', age: 9 },
  { dossier: '004179', nom: 'Radiation - MARTIN EURL', statut: 'attente', etat: 'En controle', utilisateur: 'R. VILAIN', age: 4 },
]

function vue(utilisateur: unknown, moiCompris: unknown): VueVerrous {
  const l = lignes.filter((x) => moiCompris === true || x.utilisateur !== utilisateur)
  const noms = [...new Set(l.map((x) => x.utilisateur))].sort()
  return { lignes: l, detenteurs: noms.map((u) => ({ utilisateur: u, nombre: l.filter((x) => x.utilisateur === u).length })) }
}

describe('EcranVerrous', () => {
  it('affiche les dossiers retenus par un autre poste, avec leur compte et leurs detenteurs', async () => {
    const pont = installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)

    await waitFor(() => expect(synthese(container)).toBe('2 dossier(s) · 2 detenteur(s) · plus ancien 11 j'))
    expect(pont.appels).toHaveBeenCalledWith('EcranVerrous.Verrouilles', 'R. VILAIN', false)
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees).toHaveLength(2)
    // Une ligne : la societe en avatar + nom, l'objet et le numero en gris ; l'etat en point colore.
    expect([...rangees[0]!.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['TTRANSNORD SADepot de bilan · 004151', 'En controle', 'C. BENIER', '11 j'])
    expect(rangees[0]!.querySelector('.rig-lm-avatar')!.textContent).toBe('T')
    expect(rangees[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(rangees[0]!.getAttribute('title')).toBe('Ouvrir l\'affaire 004151')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('2 dossier(s) affiche(s)Clic sur une ligne pour ouvrir l\'affaire.')
    expect([...container.querySelectorAll('.rig-alerte__titre')].map((e) => e.textContent)).toEqual(['C. BENIER', 'M. DURAND'])
  })

  it('« Inclure mes propres dossiers » redemande au pont avec la bascule allumee', async () => {
    const pont = installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(synthese(container)).toMatch(/^2 dossier\(s\)/))

    const bascule = screen.getByRole('button', { name: 'Inclure mes propres dossiers' })
    expect(bascule.className).toBe('rig-bascule')
    fireEvent.click(bascule)

    await waitFor(() => expect(synthese(container)).toMatch(/^3 dossier\(s\)/))
    expect(bascule.className).toBe('rig-bascule rig-bascule--actif')
    expect(pont.appels).toHaveBeenLastCalledWith('EcranVerrous.Verrouilles', 'R. VILAIN', true)
    // Le detenteur a l'etat d'attente prend le point orange.
    expect(container.querySelectorAll('tbody tr')[2]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
  })

  it('« Mon login » change le poste de reference', async () => {
    const pont = installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await waitFor(() => expect(synthese(container)).toMatch(/^2 dossier\(s\)/))

    fireEvent.change(screen.getByLabelText('Mon login'), { target: { value: 'C. BENIER' } })
    await waitFor(() => expect(pont.appels)
      .toHaveBeenLastCalledWith('EcranVerrous.Verrouilles', 'C. BENIER', false))
    await waitFor(() => expect(screen.queryByText('TRANSNORD SA')).toBeNull())
    expect(synthese(container)).toMatch(/^2 dossier\(s\)/)
  })

  it('remonte le NUMERO clique a la coquille Blazor', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const ouvrirDossier = vi.fn()
    render(<EcranVerrous ouvrirDossier={ouvrirDossier} />)

    fireEvent.click(await screen.findByText('004176'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')
  })

  it('dit qu\'aucun dossier n\'est retenu, mais seulement une fois la reponse arrivee', async () => {
    let repondre: (v: VueVerrous) => void = () => {}
    installerFauxPont({ 'EcranVerrous.Verrouilles': () => new Promise<VueVerrous>((r) => { repondre = r }) })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)

    expect(screen.queryByText('Aucun dossier n\'est retenu par un autre poste.')).toBeNull()
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('')

    repondre({ lignes: [], detenteurs: [] })
    await screen.findByText('Aucun dossier n\'est retenu par un autre poste.')
    expect(synthese(container)).toBe('0 dossier(s)')
  })

  it('ignore une reponse perimee arrivee apres une plus recente', async () => {
    const enAttente: Array<{ args: unknown[]; repondre: (v: VueVerrous) => void }> = []
    installerFauxPont({
      'EcranVerrous.Verrouilles': (...args) => new Promise<VueVerrous>((r) => { enAttente.push({ args, repondre: r }) }),
    })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Inclure mes propres dossiers' }))
    await waitFor(() => expect(enAttente).toHaveLength(2))

    enAttente[1]!.repondre(vue('R. VILAIN', true))
    await waitFor(() => expect(synthese(container)).toMatch(/^3 dossier\(s\)/))
    enAttente[0]!.repondre(vue('R. VILAIN', false))
    await new Promise((r) => setTimeout(r, 0))
    expect(synthese(container)).toMatch(/^3 dossier\(s\)/)
  })
})

// Ouvert depuis la page d'une affaire : « qui tient ce dossier ? » -- la liste se
// limite a elle, une bascule rend la liste entiere (plan « Vue du dossier », lot 2).
describe('EcranVerrous ouvert depuis une affaire', () => {
  it('ne montre que son verrou ; la bascule rend toute la liste', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} dossier={contexteDossier()} />)
    await waitFor(() => expect(synthese(container)).toMatch(/^1 dossier\(s\)/))
    expect([...container.querySelectorAll('tbody tr td')].map((td) => td.textContent).slice(0, 3))
      .toEqual(['PSCI DU PARCModification · 004176', 'En controle', 'M. DURAND'])

    const bascule = screen.getByRole('button', { name: 'Dossier 004176 seulement' })
    expect(bascule.className).toBe('rig-bascule rig-bascule--actif')
    fireEvent.click(bascule)
    await waitFor(() => expect(synthese(container)).toMatch(/^2 dossier\(s\)/))
    expect(bascule.className).toBe('rig-bascule')
  })

  it('une affaire que personne d\'autre ne tient le dit ; sans affaire, ni bascule ni filtre', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const avec = render(<EcranVerrous ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '004179' })} />)
    expect(await screen.findByText('Le dossier 004179 n\'est retenu par aucun autre poste.')).toBeTruthy()
    avec.unmount()

    const sans = render(<EcranVerrous ouvrirDossier={vi.fn()} dossier={null} />)
    await waitFor(() => expect(synthese(sans.container)).toMatch(/^2 dossier\(s\)/))
    expect(screen.queryByRole('button', { name: /seulement$/ })).toBeNull()
  })
})

// « Aller a un dossier… » (Ctrl K) filtre la liste AFFICHEE, sans redemander au pont ; Entree ouvre le seul restant.
describe('EcranVerrous, filtre rapide', () => {
  it('Ctrl K pose le curseur dans le champ ; la frappe filtre ; Entree ouvre le seul dossier restant', async () => {
    const pont = installerFauxPont({ 'EcranVerrous.Verrouilles': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranVerrous ouvrirDossier={ouvrirDossier} />)
    await waitFor(() => expect(synthese(container)).toMatch(/^2 dossier\(s\)/))
    const appels = pont.appels.mock.calls.length

    const champ = screen.getByLabelText('Aller a un dossier…') as HTMLInputElement
    // fireEvent rend faux quand la touche est marquee traitee (preventDefault) : la palette ne la recoit pas.
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'durand' } })
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
    expect(synthese(container)).toBe('1 dossier(s) · 1 detenteur(s) · plus ancien 9 j')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')
    expect(pont.appels.mock.calls.length).toBe(appels)

    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 2 dossiers ne contient « zzz ».')).toBeTruthy()
  })

  it('separe la societe de l\'objet', () => {
    expect(societeEtObjet('Depot de bilan - TRANSNORD SA')).toEqual({ societe: 'TRANSNORD SA', objet: 'Depot de bilan' })
    expect(societeEtObjet('TRANSNORD SA')).toEqual({ societe: 'TRANSNORD SA', objet: '' })
  })
})

// D'ou viennent les verrous (plan « RIG v3 100 % fonctionnel », lot 9) : la demonstration est marquee « exemple », la base ne
// l'est pas, une panne montre son motif et AUCUNE ligne (Pont/EcranVerrousBase.cs).
describe('EcranVerrous : provenance des verrous', () => {
  it('sans serveur, les verrous de demonstration sont marques « exemple »', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': (u: unknown, m: unknown) => ({ ...vue(u, m), mode: 'demo', message: null }) })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await screen.findByText('TRANSNORD SA')
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Verrous de démonstration')
  })

  it('branche au serveur, les verrous de la base, sans mention', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': (u: unknown, m: unknown) => ({ ...vue(u, m), mode: 'serveur', message: null }) })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    await screen.findByText('TRANSNORD SA')
    expect(container.querySelector('[role="note"]')).toBeNull()
  })

  it('en panne, le motif et aucun verrou (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranVerrous.Verrouilles': () => ({ lignes: [], detenteurs: [], mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    const { container } = render(<EcranVerrous ouvrirDossier={vi.fn()} />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(container.querySelectorAll('tbody tr.rig-lm-ligne')).toHaveLength(0)
  })
})
