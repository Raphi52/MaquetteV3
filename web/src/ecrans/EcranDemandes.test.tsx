import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranDemandes, identiteDemande, type LigneDemande, type VueDemandes } from './EcranDemandes'
import { oublierVuesDeSession } from '../composants/ListeModerne'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont(); oublierVuesDeSession() })

// Le filtre et l'etat AFFICHE (la validation au controle final prime) sont
// calcules par Pont/EcranDemandes.cs, execute par tests/SaisieCheck. Ici : la
// grille montre ce que le pont rend, et la liste des valides lui est transmise.
const ligne = (numero: string, dossier: string, valide: boolean): LigneDemande => ({
  numero, nom: `Demande ${numero}`, dossier, siren: '109 678 235', client: 'CLIENT', traitement: 'PROC_DEMANDE',
  etat: valide ? 'Validee' : 'En controle', statut: valide ? 'ok' : 'alerte', enCours: !valide,
  creation: '01/09/2026', validation: valide ? '09/09/2026' : '', utilisateur: 'R. VILAIN', alerte: !valide,
  traitementLibelle: 'Demandes',
})

const vue = (_c: unknown, valides: unknown): VueDemandes => ({
  traitements: ['PROC_DEMANDE', 'PROC_DEPOTJUD'], etats: ['En controle', 'Validee'], total: 12,
  lignes: [ligne('D1', '004151', (valides as string[]).includes('004151')), ligne('D2', '004176', false)],
  libellesTraitements: ['Demandes', 'Depots judiciaires'],
})

const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const rangees = (c: HTMLElement) => [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')]
/** Les appels « Trouver » qui portent ces criteres (l'ecran en fait aussi un par vue, pour son compte). */
type Criteres = { critDemande: string; critEtats: string[]; critAlerte: boolean; critEnCours: boolean; critTopMax: number }
const criteresAppeles = (appels: unknown[][]) => appels.map((a) => a[1] as Criteres)

describe('EcranDemandes', () => {
  it('montre l\'etat rendu par le pont pour les dossiers valides', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={['004151']} ouvrir={vi.fn()} />)

    await screen.findByText('Demande D2')
    expect(synthese(container)).toBe('2 affichee(s) sur 12 · 1 en cours · 1 en alerte')
    expect(pont.appels.mock.calls[0]![2]).toEqual(['004151'])
    const [valide, enControle] = rangees(container)
    expect(valide!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(valide!.querySelector('.rig-lm-etat')!.textContent).toBe('Validee')
    expect(enControle!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(valide!.className).toBe('')
    expect(enControle!.className).toBe('rig-table__ligne--alerte')
    // « En cours » est passe dans la ligne depliee.
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[1]!)
    const detail = container.querySelector('tr.rig-lm-detail')!
    expect([...detail.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent))
      .toEqual(['N DemandeD2', 'N Dossier004176', 'Siren109 678 235', 'ClientCLIENT', 'En coursoui', 'Date de validation—'])
  })

  it('le traitement se lit par le nom de son ecran ; la liste Processus garde le code pour valeur', async () => {
    // Parcours du lot 6 (20/11/2026) : ouvert depuis 004151, l'ecran montrait « PROC_DEPOTJUD ».
    // Faux si : le code repartait a l'ecran, ou si le choix d'un processus remontait le libelle
    // au pont (son filtre compare le code).
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('Demande D2')
    expect(container.querySelector('tbody')!.textContent).not.toContain('PROC_')
    const traitement = rangees(container)[0]!.querySelectorAll('td')[2]!
    expect(traitement.textContent).toBe('Demandes')
    expect(traitement.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    const liste = screen.getByRole('combobox', { name: 'Processus' })
    expect([...liste.querySelectorAll('option')].map((o) => [o.value, o.textContent]))
      .toEqual([['', 'Tous'], ['PROC_DEMANDE', 'Demandes'], ['PROC_DEPOTJUD', 'Depots judiciaires']])
    fireEvent.change(liste, { target: { value: 'PROC_DEPOTJUD' } })
    await waitFor(() => expect((pont.appels.mock.calls.at(-1)![1] as { critProcessus: string }).critProcessus).toBe('PROC_DEPOTJUD'))
  })

  it('une bascule d\'etat part au pont, « Effacer » remet les criteres', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    const bascule = await screen.findByRole('button', { name: 'Validee' })
    fireEvent.click(bascule)
    await waitFor(() => expect((pont.appels.mock.lastCall![1] as { critEtats: string[] }).critEtats).toEqual(['Validee']))
    expect(bascule.className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.change(screen.getByLabelText('Nombre maxi. de demandes affichees'), { target: { value: '12a' } })
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critEtats: [], critTopMax: 50 }))
  })

  it('un clic sur une demande remonte son dossier', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const ouvrir = vi.fn()
    render(<EcranDemandes valides={[]} ouvrir={ouvrir} />)
    fireEvent.click(await screen.findByText('Demande D2'))
    expect(ouvrir).toHaveBeenCalledWith('004176')
  })
})

describe('EcranDemandes en liste moderne', () => {
  it('la tete garde le nom actuel de la liste, « Resultats de la recherche », pour titre', async () => {
    // Regle du plan (titre = nom actuel tel quel, decision du lot 5). Faux si : la tete prenait un nom
    // invente, comme « Demandes » dans la premiere version du lot 3.
    installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('Demande D2')
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Resultats de la recherche')
  })

  it('« Aller a une demande… » filtre la liste affichee sans toucher aux criteres ; Entree ouvre la seule restante', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const ouvrir = vi.fn()
    const { container } = render(<EcranDemandes valides={[]} ouvrir={ouvrir} />)
    await screen.findByText('Demande D2')
    const appelsAvant = pont.appels.mock.calls.length
    const champ = screen.getByRole('textbox', { name: 'Aller a une demande…' })

    fireEvent.change(champ, { target: { value: '004176' } })
    expect(rangees(container).map((r) => r.querySelector('.rig-lm-identite__nom')!.textContent)).toEqual(['Demande D2'])
    expect(synthese(container)).toBe('1 affichee(s) sur 12 · 1 en cours · 1 en alerte')
    expect(pont.appels.mock.calls.length).toBe(appelsAvant)
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrir).toHaveBeenCalledWith('004176')

    // Sans accents, comme l'ecran modele : « démande d2 » trouve « Demande D2 ».
    fireEvent.change(champ, { target: { value: 'démande d2' } })
    expect(rangees(container).map((r) => r.querySelector('.rig-lm-identite__nom')!.textContent)).toEqual(['Demande D2'])

    fireEvent.change(champ, { target: { value: 'rien' } })
    expect(screen.getByText('Aucune des 2 demandes ne contient « rien ».')).toBeTruthy()
    fireEvent.keyDown(champ, { key: 'Escape' })
    expect(rangees(container)).toHaveLength(2)
  })

  it('le chevron deplie la ligne sans ouvrir le dossier ; « Ouvrir le dossier » de la ligne l\'ouvre', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const ouvrir = vi.fn()
    const { container } = render(<EcranDemandes valides={[]} ouvrir={ouvrir} />)
    await screen.findByText('Demande D2')
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect(ouvrir).not.toHaveBeenCalled()
    expect(rangees(container)[0]!.className).toBe('rig-table__ligne--alerte rig-lm-ouverte')
    expect(container.querySelector('tr.rig-lm-detail td')!.getAttribute('colspan')).toBe('7')
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir le dossier' }))
    expect(ouvrir).toHaveBeenCalledWith('004151')
    fireEvent.click(screen.getByRole('button', { name: 'Replier la ligne' }))
    expect(container.querySelector('tr.rig-lm-detail')).toBeNull()
  })

  it('une vue pose ses criteres et se dit active ; chaque puce porte le compte que le pont rend pour elle', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('Demande D2')
    // Une demande par vue, avec ses criteres et la meme limite d'affichage.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Toutes 2' }).getAttribute('aria-pressed')).toBe('true'))
    const appeles = criteresAppeles(pont.appels.mock.calls)
    expect(appeles.some((c) => c.critAlerte && !c.critEnCours && c.critTopMax === 50)).toBe(true)
    expect(appeles.some((c) => c.critEtats.join() === 'Validee')).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'En alerte 2' }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critAlerte: true, critEnCours: false, critEtats: [] }))
    expect(screen.getByRole('button', { name: 'En alerte 2' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'Alerte uniquement' }).className).toBe('rig-bascule rig-bascule--actif')

    // « + Nouvelle vue » garde les criteres courants sous un nom, le temps de la session.
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    const nom = screen.getByRole('textbox', { name: 'Nom de la nouvelle vue' })
    fireEvent.change(nom, { target: { value: 'Mes alertes' } })
    fireEvent.keyDown(nom, { key: 'Enter' })
    fireEvent.click(screen.getByRole('button', { name: /^Toutes/ }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critAlerte: false }))
    fireEvent.click(await screen.findByRole('button', { name: /^Mes alertes/ }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critAlerte: true }))
  })

  it('les cartes posent les criteres existants : etat, « En cours uniquement », « Alerte uniquement »', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('Demande D2')
    const carte = (titre: string) => [...container.querySelectorAll('.rig-lm-carte')]
      .find((c) => c.querySelector('.rig-lm-carte__titre')!.textContent === titre) as HTMLElement

    fireEvent.click(carte('En cours'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critEnCours: true }))
    expect(screen.getByRole('button', { name: 'En cours uniquement' }).className).toBe('rig-bascule rig-bascule--actif')
    fireEvent.click(carte('En cours'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critEnCours: false }))

    fireEvent.click(carte('Etats').querySelector('.rig-lm-legende__entree')!)
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critEtats: ['En controle'] }))
    fireEvent.click(carte('En alerte'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critAlerte: true, critEtats: ['En controle'] }))
  })

  it('chaque traitement du jeu de demonstration a sa propre teinte (capture orange du 24/11 : deux paires confondues)', async () => {
    // Les codes du jeu de demonstration et le nom que le pont leur donne (Pont/CatalogueEcrans.cs,
    // LibelleTraitement : un code hors catalogue perd son prefixe, PROC_BODACC -> BODACC).
    const traites: Array<[code: string, nom: string]> = [
      ['PROC_ACTES_NUM', 'Actes numerises'], ['PROC_ARCHIRAD', 'Archivage / radiation'], ['PROC_ARRIVEE', 'Arrivee du courrier'],
      ['PROC_BODACC', 'BODACC'], ['PROC_CLOT', 'Cloture'], ['PROC_DEMANDE', 'Demandes'], ['PROC_DEPOTJUD', 'Depots judiciaires'],
    ]
    const codes = traites.map(([c]) => c)
    installerFauxPont({ 'EcranDemandes.Trouver': (): VueDemandes => ({
      traitements: codes, etats: [], total: codes.length, libellesTraitements: traites.map(([, n]) => n),
      lignes: traites.map(([c, n], i) => ({ ...ligne(`D${i}`, `00${i}`, false), traitement: c, traitementLibelle: n })),
    }) })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('Demande D6')
    const teintes = rangees(container).map((r) => r.querySelector('.rig-lm-pastille')!.className)
    expect(new Set(teintes).size).toBe(codes.length)
  })

  it('une demande se lit par sa societe, son objet et son numero', () => {
    expect(identiteDemande('Modification - SCI DU PARC')).toEqual({ societe: 'SCI DU PARC', objet: 'Modification' })
    expect(identiteDemande('Demande D2')).toEqual({ societe: 'Demande D2', objet: '' })
  })
})

// Ouvert depuis la page d'une affaire (barre d'actions, plan « Vue du dossier »,
// lot 2) : ses demandes, sans saisie. Le faux pont filtre sur « Demande » comme
// Pont/EcranDemandes.cs (le numero ou le nom contient le texte).
describe('EcranDemandes ouvert depuis une affaire', () => {
  const filtrer = (c: unknown): VueDemandes => {
    const t = (c as { critDemande: string }).critDemande
    const toutes = [{ ...ligne('004176', '004176', false), nom: 'Modification - SCI DU PARC' }, ligne('004151', '004151', false)]
    return { traitements: [], etats: [], total: toutes.length, lignes: toutes.filter((l) => l.numero.includes(t) || l.nom.includes(t)), libellesTraitements: [] }
  }

  it('cherche ses demandes par son numero des le premier appel, sans saisie', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': filtrer })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} dossier={contexteDossier()} />)
    expect(await screen.findByText('SCI DU PARC')).toBeTruthy()
    expect(rangees(container)).toHaveLength(1)
    expect(container.querySelector('.rig-lm-identite__objet')!.textContent).toBe('Modification · 004176')
    expect(container.querySelector('tbody td[title]')!.getAttribute('title')).toBe('Modification - SCI DU PARC · 004176')
    expect(pont.appels.mock.calls[0]![1]).toMatchObject({ critDemande: '004176' })
    expect((screen.getByLabelText('Demande') as HTMLInputElement).value).toBe('004176')
  })

  it('sans affaire (ouvert depuis l\'accueil) : aucun critere, toutes les demandes', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': filtrer })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} dossier={null} />)
    await screen.findByText('SCI DU PARC')
    expect(rangees(container)).toHaveLength(2)
    expect(pont.appels.mock.calls[0]![1]).toMatchObject({ critDemande: '' })
    expect((screen.getByLabelText('Demande') as HTMLInputElement).value).toBe('')
  })
})

// D'ou viennent les demandes (plan « RIG v3 100 % fonctionnel », lot 17) : la base du greffe sans mention, la
// demonstration marquee « exemple », une panne dite sans aucune ligne (Pont/LectureEcranMain.cs).
describe('EcranDemandes et la base du greffe', () => {
  const exemple = (c: HTMLElement) => c.querySelector('.rig-tcw__exemple')

  it('des demandes lues en base : aucune mention « exemple »', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': (c: unknown, v: unknown): VueDemandes => ({ ...vue(c, v), mode: 'serveur', message: null }) })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('D1')
    expect(exemple(container)).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('la demonstration (sans serveur) porte la mention « exemple »', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': (c: unknown, v: unknown): VueDemandes => ({ ...vue(c, v), mode: 'demo', message: null }) })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('D1')
    expect(exemple(container)!.textContent).toBe('exemple')
  })

  it('les libelles de la base (« Validée ») : la vue « Validees » les coche, la teinte verte les suit', async () => {
    const enBase = (c: unknown, v: unknown): VueDemandes => {
      const lignes = vue(c, v).lignes.map((l) => ({ ...l, etat: 'Validée' }))
      return { ...vue(c, v), etats: ['Arrivée', 'Validée'], lignes, mode: 'serveur', message: null }
    }
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': enBase })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('D1')
    expect(rangees(container)[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    fireEvent.click(screen.getByRole('button', { name: /^Validees/ }))
    await waitFor(() => expect(criteresAppeles(pont.appels.mock.calls).some((c) => c.critEtats.join('|') === 'Validée')).toBe(true))
  })

  it('un acces refuse : son motif, et aucune demande', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': (): VueDemandes => ({
      traitements: [], etats: [], lignes: [], total: 0, libellesTraitements: [], mode: 'refuse',
      message: 'Vous n’avez pas accès aux données du greffe 3801.' }) })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    expect((await screen.findByRole('alert')).textContent).toBe('Accès refusé Vous n’avez pas accès aux données du greffe 3801.')
    expect(rangees(container).map((r) => r.textContent)).toEqual(['Aucune demande ne repond a ces criteres.'])
    expect(exemple(container)).toBeNull()
  })
})
