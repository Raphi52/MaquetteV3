import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranBodacc, type VueBodacc } from './EcranBodacc'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// JeuDemo.Aujourdhui = 09/09/2026 : les annonces prevues le 08/09 et le 04/09
// sont en retard, celle du 09/09 ne l'est pas (capture Blazor du 18/11/2026).
// Sans serveur, le pont rend le mode « demo » et la date du jeu : l'ecran montre ses annonces d'exemple (Pont/EcranBodacc.cs).
const Demonstration: VueBodacc = { mode: 'demo', message: null, annonces: [], total: 0, aujourdhui: '2026-09-09', periode: '' }
function ouvrir(ouvrirDossier = vi.fn()) {
  installerFauxPont({ 'EcranBodacc.Annonces': () => Demonstration })
  return { ouvrirDossier, ...render(<EcranBodacc ouvrirDossier={ouvrirDossier} />) }
}

const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const entetes = (c: HTMLElement) => [...c.querySelectorAll('thead th')].map((th) => th.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')]
const cellules = (tr: Element) => [...tr.querySelectorAll('td')].map((td) => td.textContent)
/** « Num annonce / dossier », filtre sur place : le champ « Aller a… » de la tete (decision du plan, lot 2). */
const champ = () => screen.getByRole('textbox', { name: 'Aller a une annonce…' })
/** La puce d'un onglet v2 : son nom est suivi de son compte. */
const puce = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom} \\d+$`) })

describe('EcranBodacc', () => {
  it('« a envoyer » : six colonnes, les annonces en retard surlignees', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    expect(compte(container)).toBe('3 annonce(s) a envoyer · 2 en retard')
    expect(entetes(container)).toEqual(['Num PC ou prevention', 'Code eve pro', 'Date eve pro', 'Texte Bodacc', 'Date prevue d\'envoi', 'Type annonce'])
    const lignes = rangees(container)
    expect(lignes.map((r) => r.className)).toEqual(['rig-table__ligne--alerte', '', 'rig-table__ligne--alerte'])
    // Le texte reste une cellule simple, sans avatar ni classes d'identite : ce n'est pas un nom (decision du lot 4).
    expect(cellules(lignes[0]!))
      .toEqual(['PC2026-041', 'IMM', '02/09/2026', 'Immatriculation SARL LES TILLEULS', '08/09/2026', 'Immatriculation'])
    expect(container.querySelector('tbody .rig-lm-identite')).toBeNull()
    // Le retard se lit aussi au point de la date prevue, le type en pastille.
    expect(lignes[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(lignes[1]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-bleu')
    expect(lignes[0]!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-vert')
    expect(container.querySelector('tbody .rig-lm-avatar')).toBeNull()
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('3 annonces afficheesEn retard 2Clic sur une annonce pour ouvrir son dossier.')
  })

  it('n\'affiche aucune ligne tant que la date du jour n\'est pas connue', () => {
    installerFauxPont({ 'EcranBodacc.Annonces': () => new Promise<VueBodacc>(() => {}) })
    const { container } = render(<EcranBodacc ouvrirDossier={vi.fn()} />)
    expect(container.querySelectorAll('tbody tr')).toHaveLength(0)
    expect(screen.queryByText('Aucune annonce ne repond a ces criteres.')).toBeNull()
    expect(container.querySelector('.rig-lm-synthese')).toBeNull()
  })

  it('« deja envoye » : les douze colonnes v2, cinq sur la ligne et le reste dans la ligne depliee', async () => {
    const { container, ouvrirDossier } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    expect(puce('BODACC a envoyer').getAttribute('aria-pressed')).toBe('true')
    expect(puce('BODACC a envoyer').textContent).toBe('BODACC a envoyer 3')
    expect(puce('BODACC deja envoye').textContent).toBe('BODACC deja envoye 2')
    fireEvent.click(puce('BODACC deja envoye'))
    expect(puce('BODACC deja envoye').getAttribute('aria-pressed')).toBe('true')
    expect(puce('BODACC a envoyer').getAttribute('aria-pressed')).toBe('false')
    expect(entetes(container)).toEqual(['', 'Texte Bodacc', 'Type annonce', 'Date envoi', 'Date parution', 'Num parution', ''])
    expect(compte(container)).toBe('2 annonce(s) envoyee(s) · 2 parue(s)')
    const premiere = rangees(container)[0]!
    expect(premiere.className).toBe('')
    expect(cellules(premiere)).toEqual(['', 'Radiation EURL LE PETIT CAFE', 'Radiation', '18/08/2026', '24/08/2026', '2026-171', ''])

    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect(ouvrirDossier).not.toHaveBeenCalled()
    expect([...container.querySelectorAll('tr.rig-lm-detail .rig-lm-champ')].map((c) => c.textContent)).toEqual([
      'Num ann.A2026-1150', 'Dossier2004B01344', 'Num PC ou prevention—', 'Code eve proRAD', 'Date eve pro12/08/2026', 'Date trans.19/08/2026', 'Num bord.BD-2610', 'Num fact.FA-45120'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir le dossier' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2004B01344')
  })

  it('filtre par texte et par type ; « Effacer » remet ces criteres a vide', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    // Le critere n'est plus a gauche : il est dans la tete de la liste.
    expect(screen.queryByLabelText('Num annonce / dossier')).toBeNull()
    expect([...(screen.getByLabelText('Type d\'annonce') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Tous', 'Depot des comptes', 'Immatriculation', 'Modification', 'Procedure collective', 'Radiation'])
    fireEvent.change(champ(), { target: { value: '2019b' } })
    expect(compte(container)).toBe('1 annonce(s) a envoyer · 0 en retard')
    // Les puces comptent avec les memes criteres.
    expect(puce('BODACC deja envoye').textContent).toBe('BODACC deja envoye 0')
    fireEvent.change(champ(), { target: { value: 'rien' } })
    const vide = screen.getByText('Aucune annonce ne repond a ces criteres.')
    expect(vide.getAttribute('colspan')).toBe('6')

    fireEvent.change(screen.getByLabelText('Num bordereau'), { target: { value: 'BD' } })
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(compte(container)).toBe('3 annonce(s) a envoyer · 2 en retard')
    expect((champ() as HTMLInputElement).value).toBe('')
    // Depuis le lot 17 (RIG v3 100 % fonctionnel), bordereau et facture FILTRENT comme RIG v2 (OPE_RECH_BOD.cs:242-243) :
    // « Effacer » les vide aussi, sinon un filtre resterait actif sans se voir. (Avant, inertes, ils restaient remplis.)
    expect((screen.getByLabelText('Num bordereau') as HTMLInputElement).value).toBe('')
  })

  it('une annonce cliquee remonte son dossier', async () => {
    const { ouvrirDossier } = ouvrir()
    fireEvent.click(await screen.findByText('Changement de gerant'))
    expect(ouvrirDossier).toHaveBeenCalledWith('2019B00233')
  })
})

// Ouvert depuis la page d'une affaire : ses annonces, par son numero dans
// « Num annonce / dossier », devenu le champ « Aller a… » de la tete (plan « Vue du dossier », lot 2).
describe('EcranBodacc ouvert depuis une affaire', () => {
  const rendre = (dossier: ReturnType<typeof contexteDossier> | null) => {
    installerFauxPont({ 'EcranBodacc.Annonces': () => Demonstration })
    return render(<EcranBodacc ouvrirDossier={vi.fn()} dossier={dossier} />)
  }

  it('montre ses seules annonces, le critere deja rempli', async () => {
    const { container } = rendre(contexteDossier({ numero: '2019B00233' }))
    expect(await screen.findByText('Changement de gerant')).toBeTruthy()
    expect(compte(container)).toBe('1 annonce(s) a envoyer · 0 en retard')
    expect((champ() as HTMLInputElement).value).toBe('2019B00233')
  })

  it('une affaire sans annonce le dit ; sans affaire, toute la liste', async () => {
    const avec = rendre(contexteDossier())
    expect(await screen.findByText('Aucune annonce ne repond a ces criteres.')).toBeTruthy()
    expect(compte(avec.container)).toBe('0 annonce(s) a envoyer · 0 en retard')
    avec.unmount()
    retirerFauxPont()

    const sans = rendre(null)
    expect(await screen.findByText('Immatriculation SARL LES TILLEULS')).toBeTruthy()
    expect(compte(sans.container)).toBe('3 annonce(s) a envoyer · 2 en retard')
    expect((champ() as HTMLInputElement).value).toBe('')
  })
})

// Branche au serveur (plan « RIG v3 100 % fonctionnel », lot 17) : les annonces de la base du greffe, la vraie date du jour,
// la periode de recherche de RIG v2 ; sans serveur, la mention « exemple » ; une panne, son motif et aucune ligne.
describe('EcranBodacc et la base du greffe', () => {
  const lue = {
    numAnnonce: '2026-0042', numPc: '2025P00031', typeAnnonce: 'Création', codeEvePro: 'OJRJ', dateEvePro: '2026-11-02',
    prevueEnvoi: '2026-11-20', envoi: null, numParution: '', parution: null, numBord: '', numFact: '', texte: 'Ouverture RJ BASE',
    dossier: '2019B00411', transmission: null,
  }
  const envoyee = { ...lue, numAnnonce: '2026-0007', texte: 'Cloture BASE', prevueEnvoi: '2026-03-02', envoi: '2026-03-02', numBord: 'B26-7', numFact: 'F26-9' }
  const Base: VueBodacc = { mode: 'serveur', message: null, annonces: [lue, envoyee], total: 2, aujourdhui: '2026-11-26', periode: 'du 01/01/2026 au 26/11/2026' }

  it('montre les annonces de la base, pas les exemples, sans la mention « exemple »', async () => {
    installerFauxPont({ 'EcranBodacc.Annonces': () => Base })
    const { container } = render(<EcranBodacc ouvrirDossier={vi.fn()} />)
    expect(await screen.findByText('Ouverture RJ BASE')).toBeTruthy()
    expect(screen.queryByText('Immatriculation SARL LES TILLEULS')).toBeNull()
    expect(container.querySelector('.rig-tcw__exemple')).toBeNull()
    // Prevue le 20/11, au 26/11 : en retard.
    expect(compte(container)).toBe('1 annonce(s) a envoyer · 1 en retard · envoi du 01/01/2026 au 26/11/2026')
    expect(puce('BODACC deja envoye').textContent).toBe('BODACC deja envoye 1')
  })

  it('« Num bordereau » et « Num facture » filtrent comme RIG v2 (egalite) ; « Effacer » les vide', async () => {
    installerFauxPont({ 'EcranBodacc.Annonces': () => Base })
    const { container } = render(<EcranBodacc ouvrirDossier={vi.fn()} />)
    await screen.findByText('Ouverture RJ BASE')
    fireEvent.click(puce('BODACC deja envoye'))
    const bordereau = screen.getByRole('textbox', { name: 'Num bordereau' })
    fireEvent.change(bordereau, { target: { value: 'B26' } })
    expect(screen.queryByText('Cloture BASE')).toBeNull()
    fireEvent.change(bordereau, { target: { value: ' B26-7 ' } })
    expect(screen.getByText('Cloture BASE')).toBeTruthy()
    fireEvent.change(screen.getByRole('textbox', { name: 'Num facture' }), { target: { value: 'F26-8' } })
    expect(screen.queryByText('Cloture BASE')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect((bordereau as HTMLInputElement).value).toBe('')
    expect(screen.getByText('Cloture BASE')).toBeTruthy()
    expect(container.querySelector('.rig-tcw__exemple')).toBeNull()
  })

  it('sans serveur, les annonces de demonstration portent la mention « exemple »', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    expect(container.querySelector('.rig-tcw__exemple')!.textContent).toBe('exemple')
  })

  it('une base qui ne repond pas : son motif, et aucune annonce d’exemple a la place', async () => {
    installerFauxPont({ 'EcranBodacc.Annonces': (): VueBodacc => ({ ...Base, mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', annonces: [], periode: '' }) })
    const { container } = render(<EcranBodacc ouvrirDossier={vi.fn()} />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(screen.queryByText('Immatriculation SARL LES TILLEULS')).toBeNull()
    expect(rangees(container).map((r) => r.textContent)).toEqual(['Aucune annonce ne repond a ces criteres.'])
  })
})
