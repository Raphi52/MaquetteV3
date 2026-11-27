import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranEncaissements, type VueEncaissement } from './EcranEncaissements'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de l'ecran Blazor (EcranEncaissements.razor) : 357,57 encaisses,
// deux ventilations choisies, date de virement = JeuDemo.Aujourdhui (09/09/2026).
function ouvrir() {
  const pont = installerFauxPont({ 'EcranEncaissements.DateDuJour': () => '2026-09-09' })
  return { pont, ...render(<EcranEncaissements />) }
}
// La synthese de la tete de liste : choisies, ventile sur encaisse, reste.
const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent!.trim()
const identifiants = (c: HTMLElement) => [...c.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('td')!.textContent)

describe('EcranEncaissements', () => {
  it('affiche la saisie, la date du pont et une ventilation complete', async () => {
    const { container, pont } = ouvrir()
    expect(await screen.findByDisplayValue('2026-09-09')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranEncaissements.DateDuJour')
    expect((screen.getByLabelText('Payeur') as HTMLInputElement).value).toBe('SELARL LEROY & ASSOCIES')
    expect(compte(container)).toBe('2 choisie(s) sur 4 · 357,57 € ventile(s) sur 357,57 € · solde nul')
    expect(container.querySelectorAll('tbody tr.rig-ligne--valide')).toHaveLength(2)
    // Client en avatar et abrege, montant avec sa mini-barre, ventile en point vert.
    expect([...container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['V-2026-8801', 'LASELARL LEROY & ASSOCIESLEROY', '2026B00811 — Immatriculation', '194,85 €', '194,85 / 812,40', 'oui'])
    expect(container.querySelectorAll('tbody tr')[2]!.querySelectorAll('td')[5]!.textContent).toBe('')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('4 ventilations affichees')
    expect(screen.getByText('Encaissement entierement ventile').className).toBe('rig-etat rig-etat--ok')
    expect(screen.getByText(/Encaissement repris : E-2026-0447/)).toBeTruthy()
  })

  it('decocher une affaire laisse un reste a ventiler ; le montant saisi le change', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-09')
    fireEvent.click(container.querySelectorAll('tbody tr')[1]!)
    expect(compte(container)).toBe('1 choisie(s) sur 4 · 194,85 € ventile(s) sur 357,57 € · 162,72 € a ventiler')
    expect(screen.getByText('Ventilation incomplete').className).toBe('rig-etat rig-etat--attente')

    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '194.85' } })
    expect(compte(container)).toBe('1 choisie(s) sur 4 · 194,85 € ventile(s) sur 194,85 € · solde nul')
    // Une saisie illisible garde le dernier montant lisible (le @bind d'un decimal).
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '' } })
    expect(compte(container)).toBe('1 choisie(s) sur 4 · 194,85 € ventile(s) sur 194,85 € · solde nul')
  })

  it('les cartes filtrent l\'affichage sans rien cocher : ventilations choisies, puis un client', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-09')
    const [carteVentile] = [...container.querySelectorAll<HTMLElement>('.rig-lm-carte')]
    expect([...container.querySelectorAll('.rig-lm-carte__valeur')].map((v) => v.textContent))
      .toEqual(['357,57 € sur 357,57 €', '500,00 €', 'solde nul'])
    fireEvent.click(carteVentile!)
    expect(identifiants(container)).toEqual(['V-2026-8801', 'V-2026-8802'])
    expect(compte(container)).toBe('2 choisie(s) sur 4 · 357,57 € ventile(s) sur 357,57 € · solde nul')
    fireEvent.click(carteVentile!)
    fireEvent.click(screen.getByRole('button', { name: 'BLANC 53,52 €', pressed: false }))
    expect(identifiants(container)).toEqual(['V-2026-8803'])
    // Cocher la ligne affichee reste le geste de la ligne : la ventilation change, le filtre reste.
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(compte(container)).toBe('3 choisie(s) sur 4 · 411,09 € ventile(s) sur 357,57 € · -53,52 € a ventiler')
    fireEvent.click(screen.getByRole('button', { name: /^Montants par client/ }))
    expect(identifiants(container)).toHaveLength(4)
  })

  it('« Aller a une ventilation… » filtre la liste affichee ; Entree coche ou decoche la ventilation restee seule', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-09')
    const champ = screen.getByLabelText('Aller a une ventilation…')
    fireEvent.change(champ, { target: { value: 'dupont' } })
    expect(identifiants(container)).toEqual(['V-2026-8804'])
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(container.querySelector('tbody tr')!.className).toBe('rig-ligne--valide')
    expect(compte(container)).toBe('3 choisie(s) sur 4 · 446,48 € ventile(s) sur 357,57 € · -88,91 € a ventiler')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune ventilation ne repond a ce filtre.')
  })

  it('« Emettre un recu » eteint fait disparaitre l\'adresse du recu', () => {
    ouvrir()
    expect(screen.getByLabelText('Adresse du recu')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Emettre un recu' }))
    expect(screen.queryByLabelText('Adresse du recu')).toBeNull()
  })
})

// Ouvert depuis la page d'une affaire : le reglement est pour elle (bloc « Pour
// le dossier »), et seules SES ventilations sont cochees au depart -- jamais
// celles d'autres affaires (plan « Vue du dossier », lot 2).
describe('EcranEncaissements ouvert depuis une affaire', () => {
  const rendre = (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranEncaissements.DateDuJour': () => '2026-09-09' })
    return render(<EcranEncaissements dossier={dossier} />)
  }

  it('coche ses ventilations, et elles seules', () => {
    const { container } = rendre(contexteDossier({ numero: '2019B00233', nom: 'SCI DU MOULIN' }))
    expect((screen.getByLabelText('N de dossier') as HTMLInputElement).value).toBe('2019B00233')
    const cochees = container.querySelectorAll('tbody tr.rig-ligne--valide')
    expect([...cochees].map((tr) => tr.querySelector('td')!.textContent)).toEqual(['V-2026-8802'])
  })

  it('une affaire sans ventilation n\'en coche aucune ; sans affaire, les deux d\'avant', () => {
    const avec = rendre(contexteDossier())
    expect(screen.getByText('Pour le dossier')).toBeTruthy()
    expect(avec.container.querySelectorAll('tbody tr.rig-ligne--valide')).toHaveLength(0)
    expect(screen.getByText('Choisissez les affaires a solder')).toBeTruthy()
    avec.unmount()
    retirerFauxPont()

    const sans = rendre(null)
    expect(screen.queryByText('Pour le dossier')).toBeNull()
    expect(sans.container.querySelectorAll('tbody tr.rig-ligne--valide')).toHaveLength(2)
    expect(compte(sans.container)).toBe('2 choisie(s) sur 4 · 357,57 € ventile(s) sur 357,57 € · solde nul')
  })
})

// D'ou part l'encaissement (plan « RIG v3 100 % fonctionnel », lot 12) : la demonstration est marquee « exemple » ; branche, l'ecran
// part de l'encaissement que RIG v2 prepare pour les factures du dossier ; depuis l'accueil, d'un encaissement vide ; en panne, le
// motif et aucune ventilation (Pont/EcranEncaissements.cs).
describe('EcranEncaissements : provenance de l encaissement', () => {
  const base: VueEncaissement = {
    payeur: 'CLIENT DU BANC', montant: 14250, mode: 'serveur', message: null,
    ventilations: [{ identifiant: '2201101', affaire: '2201101 — Affaire du banc', client: 'CLIENT DU BANC', abrege: 'L12ENC',
                     montant: 14250, soldeAffaire: 14250, soldeClient: 20000 }],
  }

  it('sans serveur, les ventilations de demonstration sont marquees « exemple »', () => {
    ouvrir()
    expect(document.querySelector('[role="note"]')!.textContent).toContain('Ventilations de démonstration')
  })

  it('ouvert pour un dossier, branche : le payeur, le montant et la ventilation de la base, cochee, sans mention', async () => {
    const pont = installerFauxPont({ 'EcranEncaissements.DateDuJour': () => '2026-11-27', 'EcranEncaissements.Depart': () => base })
    render(<EcranEncaissements dossier={contexteDossier({ numero: 'L12DOS01', nom: 'CLIENT DU BANC' })} />)
    await screen.findByText('Affaire du banc', { exact: false })
    expect(pont.appels).toHaveBeenCalledWith('EcranEncaissements.Depart', 'L12DOS01')
    expect(document.querySelector('[role="note"]')).toBeNull()
    expect((screen.getByLabelText('Payeur') as HTMLInputElement).value).toBe('CLIENT DU BANC')
    expect(document.querySelector('.rig-lm-synthese')!.textContent).toContain('1 choisie(s) sur 1')
    expect(screen.queryByText('SELARL LEROY & ASSOCIES')).toBeNull()
    expect(screen.queryByText('Encaissement repris', { exact: false })).toBeNull()
  })

  it('depuis l accueil, branche : un encaissement vide, aucune ventilation', async () => {
    const pont = installerFauxPont({ 'EcranEncaissements.DateDuJour': () => '2026-11-27',
      'EcranEncaissements.Depart': () => ({ payeur: '', montant: 0, ventilations: [], mode: 'serveur', message: null }) })
    render(<EcranEncaissements />)
    await waitFor(() => expect(document.querySelector('.rig-lm-synthese')!.textContent).toContain('0 choisie(s) sur 0'))
    expect(pont.appels).toHaveBeenCalledWith('EcranEncaissements.Depart', null)
    expect((screen.getByLabelText('Payeur') as HTMLInputElement).value).toBe('')
  })

  it('en panne, le motif et aucune ventilation (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranEncaissements.DateDuJour': () => '2026-11-27',
      'EcranEncaissements.Depart': () => ({ payeur: '', montant: 0, ventilations: [], mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    render(<EcranEncaissements />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(screen.queryByText('SELARL LEROY & ASSOCIES')).toBeNull()
  })
})
