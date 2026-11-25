import { afterEach, describe, expect, it, vi } from 'vitest'
// Milliers : montant() (ListeModerne.tsx) les separe par l'espace insecable U+00A0, ecrite \u00a0 ici.
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCloture, oublierVuesDeLaSession } from './EcranCloture'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont(); oublierVuesDeLaSession() })

// Valeurs attendues : celles de l'ecran Blazor (EcranCloture.razor), JeuDemo.Aujourdhui = 09/09/2026 :
// seule la limite d'examen du 05/09/2026 (SAS TRANSPORTS MARNE) est depassee.
function ouvrir(ouvrirDossier = vi.fn()) {
  const pont = installerFauxPont({ 'EcranCloture.DateDuJour': () => '2026-09-09' })
  return { pont, ouvrirDossier, ...render(<EcranCloture ouvrirDossier={ouvrirDossier} />) }
}
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const alertes = (c: HTMLElement) => c.querySelectorAll('tbody tr.rig-table__ligne--alerte')
const entetes = (c: HTMLElement) => [...c.querySelectorAll('thead th')].map((th) => th.textContent)
// La synthese de la tete de liste : le compte, les affaires a facturer, le total des forfaits.
const resultat = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent!.trim()
// Le N PC de chaque affaire affichee (« Selection PC ») : le debut de l'objet de sa cellule.
const numerosPc = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody .rig-lm-identite__objet')].map((o) => o.textContent!.split(' · ')[0])

describe('EcranCloture', () => {
  it('affiche les cinq affaires de « Selection PC » et marque la limite depassee', async () => {
    const { container, pont } = ouvrir()
    expect(resultat(container)).toBe('5 affaires sur 5 · 3 a facturer · forfaits 2\u00a0350,00 €')
    // Les colonnes principales ; les secondaires de v2 sont dans la ligne depliee (test suivant).
    expect(entetes(container)).toEqual(['', 'Nom de l\'Affaire', 'Date d\'audience', 'Type de classement', 'Limite Examen', 'A facturer', 'Forfait', ''])
    expect(lignes(container)[0]!.slice(1, 7)).toEqual([
      'BDSARL BOULANGERIE DUVALPC-2024-0118 · 1re', '15/09/2026 09:00', 'Insuffisance d\'actif', '12/09/2026', 'oui', '450,00 €'])
    await vi.waitFor(() => expect(alertes(container)).toHaveLength(1))
    expect(pont.appels).toHaveBeenCalledWith('EcranCloture.DateDuJour')
    expect(alertes(container)[0]!.textContent).toContain('SAS TRANSPORTS MARNE')
    // La limite depassee est un point rouge, les autres un point vert.
    expect(alertes(container)[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(container.querySelector('tbody tr .rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('Limite depassee 1')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('5 affaires affichees')
  })

  it('le chevron deplie les colonnes secondaires de v2 sans ouvrir le dossier ; « Ouvrir le dossier » l\'ouvre', () => {
    const { container, ouvrirDossier } = ouvrir()
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect(ouvrirDossier).not.toHaveBeenCalled()
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent)).toEqual([
      'Heure de debut08:30', 'Dernier courrier envoye28/08/2026', 'DirigeantsDUVAL Marc', 'LiquidateursMe LEROY',
      'HuissierSCP HUISSIERS REIMS', 'Code Postal - Ville51100 Reims'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir le dossier' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00318')
  })

  it('un clic sur une ligne remonte le dossier a Blazor', () => {
    const { container, ouvrirDossier } = ouvrir()
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(ouvrirDossier).toHaveBeenCalledWith('2024B00318')
  })

  it('les criteres filtrent ; l\'onglet « Enrolement / requete » a ses propres colonnes', async () => {
    const { container } = ouvrir()
    await vi.waitFor(() => expect(alertes(container)).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Limite d\'examen depassee' }))
    expect(numerosPc(container)).toEqual(['PC-2024-0207'])
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '1re' } })
    expect(resultat(container)).toBe('2 affaires sur 5 · 2 a facturer · forfaits 1\u00a0070,00 €')

    fireEvent.click(screen.getByRole('button', { name: 'Enrolement / requete' }))
    expect(screen.getByText('Enrolement et requete')).toBeTruthy()
    expect(entetes(container)).toEqual(['Defendeur', 'Demandeur', 'Acte de saisine', 'Audience', 'Depot', 'Facturation'])
    expect(lignes(container)[0]).toEqual([
      'BDSARL BOULANGERIE DUVAL2024/00318 · PC-2024-0118', 'Me LEROY', 'Assignation', '15/09/2026', '04/03/2026', 'Facturee'])
    fireEvent.change(screen.getByLabelText('Aller a une affaire…'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.getAttribute('colspan')).toBe('6')
  })

  it('les cartes chiffrent les forfaits et POSENT les criteres existants (classement, a facturer, limite depassee)', async () => {
    const { container } = ouvrir()
    await vi.waitFor(() => expect(alertes(container)).toHaveLength(1))
    const valeurs = () => [...container.querySelectorAll('.rig-lm-carte__valeur')].map((v) => v.textContent)
    expect(valeurs()).toEqual(['2\u00a0350,00 €', '3 sur 5', '1 sur 5'])
    // Insuffisance d'actif : DUVAL et PLOMBERIE NORD, 450 + 450.
    fireEvent.click(screen.getByRole('button', { name: 'Insuffisance d\'actif 900,00 €', pressed: false }))
    expect(screen.getByRole('button', { name: 'Insuffisance d\'actif' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(numerosPc(container)).toEqual(['PC-2024-0118', 'PC-2022-0876'])
    fireEvent.click(screen.getByRole('button', { name: /^Forfaits/ }))
    expect(numerosPc(container)).toHaveLength(5)

    const [, carteAFacturer, carteExamen] = [...container.querySelectorAll<HTMLElement>('.rig-lm-carte')]
    fireEvent.click(carteAFacturer!)
    expect(screen.getByRole('button', { name: 'A facturer uniquement' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(numerosPc(container)).toEqual(['PC-2024-0118', 'PC-2023-0920', 'PC-2022-0876'])
    fireEvent.click(carteAFacturer!)
    fireEvent.click(carteExamen!)
    expect(screen.getByRole('button', { name: 'Limite d\'examen depassee' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(numerosPc(container)).toEqual(['PC-2024-0207'])
  })

  it('les vues POSENT leurs criteres et sont comptees ; « + Nouvelle vue » garde les criteres pour la session', async () => {
    const premier = ouvrir()
    await vi.waitFor(() => expect(alertes(premier.container)).toHaveLength(1))
    const vue = (nom: RegExp) => screen.getByRole('button', { name: nom })
    expect([...premier.container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent)).toEqual([
      'Toutes 5', 'A facturer 3', 'Limite d\'examen depassee 1', 'Insuffisance d\'actif 2', '+ Nouvelle vue'])
    expect(vue(/^Toutes/).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(premier.container.querySelectorAll('.rig-lm-vue')[1]!)
    expect(screen.getByRole('button', { name: 'A facturer uniquement' }).className).toBe('rig-bascule rig-bascule--actif')
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '2e' } })
    expect(premier.container.querySelector('.rig-lm-vue--active')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.change(screen.getByLabelText('Nom de la nouvelle vue'), { target: { value: '2e a facturer' } })
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(vue(/^2e a facturer/).getAttribute('aria-pressed')).toBe('true')
    premier.unmount()
    retirerFauxPont()

    const second = ouvrir()
    fireEvent.click(vue(/^2e a facturer/))
    expect((screen.getByLabelText('Chambre') as HTMLSelectElement).value).toBe('2e')
    expect(numerosPc(second.container)).toEqual(['PC-2022-0876'])
  })

  it('« Aller a une affaire… » EST le critere « Affaire » (nom ou n de role) : un seul champ, il compte et pose les vues ; Entree ouvre le dossier reste seul', () => {
    const { container, ouvrirDossier } = ouvrir()
    // Un seul champ pour un seul filtre : plus de champ « Affaire » a gauche.
    expect(screen.queryByLabelText('Affaire')).toBeNull()
    const champ = screen.getByLabelText('Aller a une affaire…') as HTMLInputElement
    fireEvent.change(champ, { target: { value: 'moulin' } })
    expect(numerosPc(container)).toEqual(['PC-2025-0044'])
    // La tete et le pied comptent la liste filtree, pas les cinq.
    expect(resultat(container)).toBe('1 affaire sur 5 · 0 a facturer · forfaits 380,00 €')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('1 affaire affichee')
    // C'est un critere : la vue « Toutes » n'est plus la vue posee.
    expect(container.querySelector('.rig-lm-vue--active')).toBeNull()
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('2025B00044')
    // Le n de role, comme le critere d'avant.
    fireEvent.change(champ, { target: { value: '2024/00318' } })
    expect(numerosPc(container)).toEqual(['PC-2024-0118'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune affaire ne repond a ces criteres.')
    // « Effacer » le vide comme les autres criteres, et la vue « Toutes » revient.
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(champ.value).toBe('')
    expect(screen.getByRole('button', { name: /^Toutes/ }).getAttribute('aria-pressed')).toBe('true')
  })
})

// Ouvert depuis la page d'une affaire : cherchee par son NOM dans « Affaire »
// (nom ou n de role ; plan « Vue du dossier », lot 2).
describe('EcranCloture ouvert depuis une affaire', () => {
  const rendre = (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranCloture.DateDuJour': () => '2026-09-09' })
    return render(<EcranCloture ouvrirDossier={vi.fn()} dossier={dossier} />)
  }

  it('ne montre qu\'elle, le critere deja rempli', () => {
    const { container } = rendre(contexteDossier({ numero: '2025B00044', nom: 'SCI DU MOULIN' }))
    expect(resultat(container)).toBe('1 affaire sur 5 · 0 a facturer · forfaits 380,00 €')
    expect(lignes(container)[0]![1]).toBe('MSCI DU MOULINPC-2025-0044 · 3e')
    expect((screen.getByLabelText('Aller a une affaire…') as HTMLInputElement).value).toBe('SCI DU MOULIN')
  })

  it('une affaire sans procedure le dit ; sans affaire, les cinq', () => {
    const avec = rendre(contexteDossier())
    expect(screen.getByText('Aucune affaire ne repond a ces criteres.')).toBeTruthy()
    avec.unmount()
    retirerFauxPont()

    const sans = rendre(null)
    expect(resultat(sans.container)).toBe('5 affaires sur 5 · 3 a facturer · forfaits 2\u00a0350,00 €')
    expect((screen.getByLabelText('Aller a une affaire…') as HTMLInputElement).value).toBe('')
  })
})
