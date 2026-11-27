import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ATransmettreNonLu, EcranCoffreFort, type VueCoffreFort } from './EcranCoffreFort'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Sans serveur, le pont rend le mode « demo » : l'ecran montre ses depots d'exemple (Pont/EcranCoffreFortBase.cs).
const Demo: VueCoffreFort = { mode: 'demo', message: null, depots: [], total: 0 }
/** L'ecran sans serveur, une fois le pont repondu (les depots d'exemple sont alors comptes). */
async function ouvrir(v: VueCoffreFort = Demo) {
  const pont = installerFauxPont({ 'EcranCoffreFort.Depots': () => v })
  const r = render(<EcranCoffreFort />)
  await vi.waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranCoffreFort.Depots'))
  await vi.waitFor(() => expect(r.container.querySelector('.rig-lm-vue')!.textContent).not.toBe('A transmettre 0'))
  return r
}

// Valeurs attendues : celles de l'ecran Blazor (EcranCoffreFort.razor) : 4 depots, dont 2
// « A transmettre » (jamais envoye, ou en erreur) et 2 « Deja transmis ».
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
// Le code de chaque depot affiche : le debut de l'objet de sa cellule (« CF-2026-0603 · MARIE »).
const codes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody .rig-lm-identite__objet')].map((o) => o.textContent!.split(' · ')[0])
const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
// Les onglets de l'ecran, en puces comptees.
const puce = (nom: RegExp) => screen.getByRole('button', { name: nom })

describe('EcranCoffreFort', () => {
  it('affiche les depots a transmettre, le depot en erreur en alerte', async () => {
    const { container } = await ouvrir()
    expect(compte(container)).toBe('2 depots a transmettre · 1 en erreur')
    expect([...container.querySelectorAll('thead th')].map((th) => th.textContent))
      .toEqual(['', 'Destinataire', 'Etat', 'Date envoi infogreffe', 'Code de depot', 'Erreur'])
    expect(codes(container)).toEqual(['CF-2026-0603', 'CF-2026-0610'])
    expect(container.querySelectorAll('tbody tr')[1]!.className).toBe('rig-table__ligne--alerte')
    expect(lignes(container)[1]!.slice(2, 4)).toEqual(['En erreur', '04/09/2026'])
    expect(container.querySelectorAll('tbody tr')[1]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(container.querySelectorAll('tbody tr')[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(puce(/^A transmettre/).textContent).toBe('A transmettre 2')
    expect(puce(/^A transmettre/).getAttribute('aria-pressed')).toBe('true')
    // Distinct().Order() des coffres, apres « Tous ».
    expect([...(screen.getByLabelText('Nom du coffre') as HTMLSelectElement).options].map((o) => o.text))
      .toEqual(['Tous', 'COFFRE_BLANC', 'COFFRE_LEROY', 'COFFRE_PETIT'])
  })

  it('la puce « Deja transmis » et les criteres filtrent la liste', async () => {
    const { container } = await ouvrir()
    fireEvent.click(puce(/^Deja transmis/))
    expect(puce(/^Deja transmis/).getAttribute('aria-pressed')).toBe('true')
    expect(puce(/^A transmettre/).getAttribute('aria-pressed')).toBe('false')
    expect(codes(container)).toEqual(['CF-2026-0412', 'CF-2026-0518'])
    fireEvent.change(screen.getByLabelText('Nom du coffre'), { target: { value: 'COFFRE_BLANC' } })
    expect(codes(container)).toEqual(['CF-2026-0518'])
    // Chaque puce compte sa liste sous les criteres.
    expect(puce(/^Deja transmis/).textContent).toBe('Deja transmis 1')
    expect(puce(/^A transmettre/).textContent).toBe('A transmettre 0')
    fireEvent.change(screen.getByLabelText('Aller a un depot…'), { target: { value: 'leroy' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun depot ne repond a ces criteres.')
  })

  it('un clic sur un depot montre son historique, borne par le nombre de lignes', async () => {
    const { container } = await ouvrir()
    fireEvent.click(puce(/^Deja transmis/))
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(screen.getByText('Historique de CF-2026-0412 (20 ligne(s) au plus)')).toBeTruthy()
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(3)
    fireEvent.change(screen.getByLabelText('Nombre de lignes d\'historique'), { target: { value: '2' } })
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(2)
  })

  it('le chevron deplie les dates de suivi sans choisir le depot ; « Voir l\'historique » le choisit', async () => {
    const { container } = await ouvrir()
    fireEvent.click(puce(/^Deja transmis/))
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect(screen.queryByText(/^Historique de/)).toBeNull()
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent)).toEqual([
      'Date AR Infogreffe07/03/2026', 'Date AR destinataire08/03/2026', 'Date lecture destinataire09/03/2026',
      'Date traitement RIG07/03/2026', 'Date avis de depot coffre08/03/2026', 'CoffreCOFFRE_LEROY', 'Type de decisionACAD'])
    fireEvent.click(screen.getByRole('button', { name: 'Voir l\'historique' }))
    expect(screen.getByText('Historique de CF-2026-0412 (20 ligne(s) au plus)')).toBeTruthy()
  })

  it('« Aller a un depot… » EST le critere « Code ou destinataire » : un seul champ, les puces le comptent ; Entree choisit le depot reste seul', async () => {
    const { container } = await ouvrir()
    // Un seul champ pour un seul filtre : plus de champ « Code ou destinataire » a gauche.
    expect(screen.queryByLabelText('Code ou destinataire')).toBeNull()
    const champ = screen.getByLabelText('Aller a un depot…')
    fireEvent.change(champ, { target: { value: 'petit' } })
    expect(codes(container)).toEqual(['CF-2026-0610'])
    // C'est un critere : chaque puce compte sa liste sous lui (Me PETIT n'a rien de deja transmis).
    expect(puce(/^Deja transmis/).textContent).toBe('Deja transmis 0')
    expect(puce(/^A transmettre/).textContent).toBe('A transmettre 1')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByText('Historique de CF-2026-0610 (20 ligne(s) au plus)')).toBeTruthy()
    // Le code du depot, comme le critere d'avant.
    fireEvent.change(champ, { target: { value: 'cf-2026-0603' } })
    expect(codes(container)).toEqual(['CF-2026-0603'])
  })
})

// Branche sur la base (plan « RIG v3 100 % fonctionnel », lot 20) : l'historique des depots de RIG v2 ; « A transmettre » vide
// et dit pourquoi ; aucun exemple ; en panne, le motif et aucun depot.
describe('EcranCoffreFort branche sur la base', () => {
  const lu = {
    code: '2160001', abrege: '2004J00007', destinataire: 'MANDATAIRE BANC', coffre: '', typeDecision: 'ACAD', etat: '3 - Déposé', statut: 'ok',
    envoiInfogreffe: null, arInfogreffe: '2004-06-02', arDestinataire: '2004-06-03', lectureDestinataire: null, traitementRig: '2004-06-01',
    avisDepotCoffre: '2004-06-02', codeDepot: 'DEP-1', erreur: '', historique: [], nomDocument: 'decision.pdf', aTransmettre: false,
  }

  it('s ouvre sur l historique de la base ; « A transmettre » le dit vide ; le nom du document se deplie', async () => {
    installerFauxPont({ 'EcranCoffreFort.Depots': () => ({ mode: 'serveur', message: null, depots: [lu], total: 1 }) })
    const r = render(<EcranCoffreFort />)
    await screen.findByText('MANDATAIRE BANC')
    expect(screen.getByRole('button', { name: /^Deja transmis/ }).getAttribute('aria-pressed')).toBe('true')
    expect(r.container.querySelector('.rig-tcw__exemple')).toBeNull()
    expect(screen.queryByText('Me LEROY')).toBeNull()
    fireEvent.click(r.container.querySelector('tbody .rig-lm-chevron')!)
    expect(r.container.querySelector('.rig-lm-detail')!.textContent).toContain('decision.pdf')
    fireEvent.click(screen.getByRole('button', { name: /^A transmettre/ }))
    expect(screen.getByText(ATransmettreNonLu)).toBeTruthy()
  })

  it('en panne : le motif, aucun depot d exemple', async () => {
    installerFauxPont({ 'EcranCoffreFort.Depots': () => ({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', depots: [], total: 0 }) })
    render(<EcranCoffreFort />)
    await screen.findByText(/La base du greffe 3801 ne répond pas/)
    expect(screen.queryByText('Me LEROY')).toBeNull()
  })
})
