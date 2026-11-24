import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCompteurs, porteAnnee, type CompteurNumeros, type VueEcranCompteurs } from './EcranCompteurs'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : tete, filtre Ctrl K, points d'etat, pied -- pas un correctif.

// Les regles d'une restitution (format, jamais donne, deja rendu, serie sans restitution) vivent dans
// le serveur et sont prouvees par tests/NumerotationCompteurCheck. Ici : l'ecran montre les compteurs
// que le pont lui rend et lui remonte chaque restitution, avec son explication.
function compteur(partiel: Partial<CompteurNumeros> = {}): CompteurNumeros {
  return {
    type: 'JPC', annee: 2026, jour: 0, libelle: 'n° de PC du ressort du greffe', structure: 'ALC', codageAnnee: 4,
    lettre: 'RJ', separateur: '', tailleChrono: 4, codeGreffe: null, debut: 1, chrono: 242, gestionRendus: true,
    redirection: null, tableCible: null, colonneCible: null, derniereAttribution: '2026-11-20T17:42:00Z', numerosRendus: 0,
    ...partiel,
  }
}

function vue(partiel: Partial<VueEcranCompteurs> = {}): VueEcranCompteurs {
  return {
    mode: 'serveur', message: null, echec: false, greffe: '8002',
    compteurs: [
      compteur(),
      compteur({ type: 'DMD', jour: 325, libelle: 'Numéro des Demandes', structure: 'LAQC', chrono: 31, gestionRendus: false }),
      compteur({ type: 'SIREN', libelle: 'Numéro SIREN', structure: 'C', codageAnnee: 0, chrono: 12, numerosRendus: 2 }),
    ],
    ...partiel,
  }
}

function remplir(type: string, numero: string, explication: string) {
  fireEvent.change(screen.getByLabelText('Série'), { target: { value: type } })
  fireEvent.change(screen.getByLabelText('Numéro donné par erreur'), { target: { value: numero } })
  fireEvent.change(screen.getByLabelText('Explication'), { target: { value: explication } })
}

describe('EcranCompteurs', () => {
  it('NUM-001 CA01 : montre une ligne par serie, annee et jour, avec son dernier chrono', async () => {
    installerFauxPont({ 'EcranCompteurs.Ouvrir': () => vue() })
    render(<EcranCompteurs />)
    expect(await screen.findByText(/^3 compteur\(s\) — greffe 8002/)).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(3)
    expect(lignes[0]!.textContent).toContain('JPC')
    expect(lignes[0]!.textContent).toContain('242')
    expect(lignes[1]!.textContent).toContain('325')
    expect(lignes[1]!.textContent).toContain('Non redonnés')
    expect(lignes[2]!.textContent).toContain('2 en attente')
    // La restitution en point colore : redonnes vert, sans restitution gris, en attente orange.
    expect(lignes.map((l) => l.querySelector('.rig-lm-etat')!.className))
      .toEqual(['rig-lm-etat rig-lm-t-vert', 'rig-lm-etat rig-lm-t-gris', 'rig-lm-etat rig-lm-t-orange'])
    expect(document.querySelector('.rig-lm-titre')!.textContent).toBe('Compteurs de numéros')
    expect(document.querySelector('.rig-lm-synthese')!.textContent).toBe('3 compteur(s) — greffe 8002 · 2 numéro(s) rendu(s) en attente')
    expect(document.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 compteur(s) affiché(s)Rendus en attente 2Clic sur une ligne pour choisir sa série à gauche.')
  })

  it('NUM-004 CA10 : rend un numero avec son explication et montre la reponse du serveur', async () => {
    const pont = installerFauxPont({
      'EcranCompteurs.Ouvrir': () => vue(),
      'EcranCompteurs.Rendre': () => vue({ message: '2026RJ0012 (JPC) rendu : il sera redonné au prochain acte de sa série.' }),
    })
    render(<EcranCompteurs />)
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    const bouton = screen.getByRole('button', { name: 'Rendre le numéro' }) as HTMLButtonElement
    remplir('JPC', ' 2026RJ0012 ', '')
    expect(bouton.disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('Explication'), { target: { value: 'procédure supprimée' } })
    expect(bouton.disabled).toBe(false)
    fireEvent.click(bouton)
    expect((await screen.findByRole('status')).textContent).toContain('rendu : il sera redonné')
    expect(pont.appels).toHaveBeenCalledWith('EcranCompteurs.Rendre',
      { type: 'JPC', numero: '2026RJ0012', explication: 'procédure supprimée', annee: 0 })
    expect((screen.getByLabelText('Numéro donné par erreur') as HTMLInputElement).value).toBe('')
  })

  it('NUM-004 CA09 : un refus du serveur s\'affiche en alerte et garde la saisie', async () => {
    installerFauxPont({
      'EcranCompteurs.Ouvrir': () => vue(),
      'EcranCompteurs.Rendre': () => vue({ message: '2026RJ9999 (JPC) refusé (jamais donné).', echec: true }),
    })
    render(<EcranCompteurs />)
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    remplir('JPC', '2026RJ9999', 'erreur de saisie')
    fireEvent.click(screen.getByRole('button', { name: 'Rendre le numéro' }))
    const alerte = await screen.findByRole('alert')
    expect(alerte.textContent).toContain('Refusé')
    expect(alerte.textContent).toContain('jamais donné')
    expect((screen.getByLabelText('Numéro donné par erreur') as HTMLInputElement).value).toBe('2026RJ9999')
  })

  it('NUM-004 CA07 : une serie dont le format ne porte pas l\'annee la demande, et l\'envoie', async () => {
    const pont = installerFauxPont({ 'EcranCompteurs.Ouvrir': () => vue(), 'EcranCompteurs.Rendre': () => vue() })
    render(<EcranCompteurs />)
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    expect(screen.queryByLabelText('Année (le numéro ne la porte pas)')).toBeNull()
    remplir('SIREN', '000000012', 'doublon')
    fireEvent.change(screen.getByLabelText('Année (le numéro ne la porte pas)'), { target: { value: '2025' } })
    fireEvent.click(screen.getByRole('button', { name: 'Rendre le numéro' }))
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    expect(pont.appels).toHaveBeenCalledWith('EcranCompteurs.Rendre',
      { type: 'SIREN', numero: '000000012', explication: 'doublon', annee: 2025 })
    expect(porteAnnee('ALC')).toBe(true)
    expect(porteAnnee('C')).toBe(false)
  })

  it('NUM-004 CA08 : au-dela de 50 caracteres, dit que l\'ancien logiciel n\'en recevra que le debut', async () => {
    installerFauxPont({ 'EcranCompteurs.Ouvrir': () => vue() })
    render(<EcranCompteurs />)
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    fireEvent.change(screen.getByLabelText('Explication'), { target: { value: 'x'.repeat(50) } })
    expect(screen.queryByText(/50 premiers caractères/)).toBeNull()
    fireEvent.change(screen.getByLabelText('Explication'), { target: { value: 'x'.repeat(51) } })
    expect(screen.getByText(/50 premiers caractères/)).toBeTruthy()
  })

  it('sans serveur : les compteurs de demonstration, en lecture seule', async () => {
    installerFauxPont({ 'EcranCompteurs.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranCompteurs />)
    expect(await screen.findAllByText('Démonstration')).toHaveLength(2)
    expect((screen.getByLabelText('Série') as HTMLSelectElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Rendre le numéro' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('branchee au serveur sans greffe ouvert : le dit, sans montrer de compteurs', async () => {
    installerFauxPont({
      'EcranCompteurs.Ouvrir': () => vue({ mode: 'sans-greffe', greffe: null, compteurs: [], echec: true, message: 'Aucun greffe n\'est ouvert dans cette session.' }),
    })
    render(<EcranCompteurs />)
    expect((await screen.findByRole('alert')).textContent).toContain('Aucun greffe ouvert')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('cherche par serie ou par libelle', async () => {
    installerFauxPont({ 'EcranCompteurs.Ouvrir': () => vue() })
    render(<EcranCompteurs />)
    await screen.findByText(/^3 compteur\(s\) — greffe 8002/)
    // Le champ de recherche est le filtre de la tete : Ctrl K y pose le curseur (touche gardee a la palette).
    const champ = screen.getByLabelText('Série ou libellé…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'demandes' } })
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain('DMD')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toContain('1 compteur(s) affiché(s)')
    // Le clic sur une ligne choisit toujours sa serie a gauche.
    fireEvent.click(lignes[0]!)
    expect((screen.getByLabelText('Série') as HTMLSelectElement).value).toBe('DMD')
  })
})
