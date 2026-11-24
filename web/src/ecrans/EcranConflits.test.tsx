import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranConflits, destination, lisible, nomDeFiche, type EcartRecopie, type VueEcranConflits } from './EcranConflits'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : tete, vues, filtre Ctrl K, pastilles, pied -- pas un correctif.

// Qui gagne, ce qui est un conflit et qui peut traiter : le serveur (tests/RecopieDeuxSensCheck). Ici : l'ecran montre
// les ecarts que le pont lui rend, la valeur perdue et la valeur gardee de chaque champ, ouvre la fiche, et remonte
// « Traité ».
function ecart(partiel: Partial<EcartRecopie> = {}): EcartRecopie {
  return {
    id: 7, tableV2: 'CLIENT', libelleTable: 'Client', idV2: '3002', idV3: '3001', fiche: 'client 3001 — DUPONT', cible: 'ecran:PROC_FCLIENT',
    detecteLe: '2026-11-21T10:15:00', dateV2: '2026-11-21T10:12:00', auteurV2: null, dateV3: '2026-11-21T10:10:00', auteurV3: 'Claire MARTIN',
    champs: [{ champ: 'CLIEN_EMAIL', libelle: 'E-mail', valeurV2: 'v2@exemple.fr', valeurV3: 'v3@exemple.fr' }], traiteLe: null, traitePar: null, enRouge: false,
    ...partiel,
  }
}

function vue(partiel: Partial<VueEcranConflits> = {}): VueEcranConflits {
  return {
    mode: 'serveur', message: null, echec: false, greffe: '8002', peutTraiter: true, traites: false, table: null,
    ecarts: [
      ecart(),
      ecart({ id: 6, tableV2: 'DOSSIER_RCS', libelleTable: 'Dossier RCS', fiche: 'dossier RCS 12 — 2019B00412', cible: 'dossier:2019B00412', enRouge: true,
              champs: [{ champ: 'DSSRC_DATE_RADIATION', libelle: 'Date de radiation', valeurV2: '2026-11-18T00:00:00', valeurV3: null }] }),
    ],
    tables: [{ code: 'CLIENT', libelle: 'Client' }, { code: 'DOSSIER_RCS', libelle: 'Dossier RCS' }], alerte: { nonTraites: 2, enRouge: 1, plusAncien: '2026-11-12T09:00:00' },
    ...partiel,
  }
}

describe('EcranConflits', () => {
  it('REC-004 CA01 : montre chaque fiche, et pour chaque champ la valeur perdue de l\'ancien RIG et celle gardee par la V3', async () => {
    installerFauxPont({ 'EcranConflits.Ouvrir': () => vue() })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    expect(await screen.findByText(/^2 écart\(s\) — greffe 8002/)).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1, 3)
    // La fiche en avatar + nom, sa sorte en gris ; le type en pastille ; l'etat en point colore.
    expect(lignes[0]!.querySelector('td')!.textContent).toBe('DDUPONTclient 3001')
    expect(lignes[0]!.querySelector('td')!.getAttribute('title')).toBe('client 3001 — DUPONT')
    expect(lignes[0]!.textContent).toContain('À traiter')
    expect(lignes[1]!.textContent).toContain('En retard')
    expect(lignes.map((l) => l.querySelector('.rig-lm-pastille')!.className))
      .toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-orange'])
    expect(lignes.map((l) => l.querySelector('.rig-lm-etat')!.className))
      .toEqual(['rig-lm-etat rig-lm-t-orange', 'rig-lm-etat rig-lm-t-rouge'])
    expect(document.querySelector('.rig-lm-synthese')!.textContent).toBe('2 écart(s) — greffe 8002 · 1 en retard')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toBe('2 écart(s) affiché(s)En retard 1Clic sur une ligne pour voir ses champs ci-dessous.')
    const detail = screen.getByLabelText("Détail de l'écart")
    expect(within(detail).getByText('v2@exemple.fr')).toBeTruthy()
    expect(within(detail).getByText('v3@exemple.fr')).toBeTruthy()
    expect(detail.textContent).toContain('par Claire MARTIN')
    expect(detail.textContent).toContain("auteur non noté par l'ancien RIG")
    // L'agent lit les titres de la fiche de la V2, jamais les noms de colonnes (refus du chef du 21/11).
    expect(within(detail).getByText('E-mail')).toBeTruthy()
    expect(lignes[0]!.textContent).toContain('Client')
    expect(document.body.textContent).not.toContain('CLIEN_EMAIL')
    expect(document.body.textContent).not.toContain('DOSSIER_RCS')
    // Une autre ligne : son detail, une date lisible et une valeur vide nommee.
    fireEvent.click(lignes[1]!)
    expect(within(screen.getByLabelText("Détail de l'écart")).getByText('18/11/2026')).toBeTruthy()
    expect(within(screen.getByLabelText("Détail de l'écart")).getByText('(vide)')).toBeTruthy()
  })

  it('REC-004 CA02 : l\'alerte donne le nombre a revoir, et « Ouvrir la fiche » mene a l\'affaire ou a l\'ecran de la fiche', async () => {
    installerFauxPont({ 'EcranConflits.Ouvrir': () => vue() })
    const ouvrirDossier = vi.fn()
    const ouvrir = vi.fn()
    render(<EcranConflits ouvrirDossier={ouvrirDossier} ouvrir={ouvrir} />)
    expect(await screen.findByText('2 à revoir')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir la fiche' }))
    expect(ouvrir).toHaveBeenCalledWith('PROC_FCLIENT')
    fireEvent.click(screen.getAllByRole('row')[2]!)
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir la fiche' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('2019B00412')
  })

  it('REC-004 CA03 : « Traité » remonte l\'ecart au pont, qui rend la liste sans lui et le compte rendu', async () => {
    const pont = installerFauxPont({
      'EcranConflits.Ouvrir': () => vue(),
      'EcranConflits.Traiter': () => vue({ ecarts: [vue().ecarts[1]!], alerte: { nonTraites: 1, enRouge: 1, plusAncien: null },
                                           message: 'client 3001 — DUPONT : marqué traité par Claire MARTIN le 21/11/2026 à 10:20.' }),
    })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    fireEvent.click(screen.getByRole('button', { name: 'Traité' }))
    expect(await screen.findByText(/marqué traité par Claire MARTIN/)).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranConflits.Traiter', 7, false, null)
    expect(screen.getByText('1 à revoir')).toBeTruthy()
  })

  it('le bouton « Traité » n\'est pas montre a qui n\'a pas le droit, ni pour un ecart deja traite', async () => {
    installerFauxPont({ 'EcranConflits.Ouvrir': () => vue({ peutTraiter: false }) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    expect(screen.queryByRole('button', { name: 'Traité' })).toBeNull()
    cleanup()
    installerFauxPont({ 'EcranConflits.Ouvrir': () => vue({ traites: true, ecarts: [ecart({ traiteLe: '2026-11-21T10:20:00', traitePar: 'Claire MARTIN' })] }) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^1 écart\(s\) — greffe 8002/)
    expect(screen.queryByRole('button', { name: 'Traité' })).toBeNull()
    expect(screen.getByLabelText("Détail de l'écart").textContent).toContain('traité par Claire MARTIN le 21/11/2026 10:20')
  })

  it('un agent non designe voit le refus du serveur, sans liste', async () => {
    installerFauxPont({ 'EcranConflits.Ouvrir': () => vue({ mode: 'refuse', message: 'Les fiches en conflit sont réservées à l\'agent désigné.', ecarts: [], alerte: null }) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    expect((await screen.findByRole('alert')).textContent).toContain('Accès refusé')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it("le filtre par type de fiche relit la liste aupres du pont, sous le nom que lit l'agent", async () => {
    const pont = installerFauxPont({ 'EcranConflits.Ouvrir': (_t, table) => vue(table === 'CLIENT' ? { table: 'CLIENT', ecarts: [ecart()] } : {}) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    expect(screen.getByRole('option', { name: 'Dossier RCS' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Type de fiche'), { target: { value: 'CLIENT' } })
    expect(await screen.findByText(/^1 écart\(s\) — greffe 8002/)).toBeTruthy()
    expect(pont.appels).toHaveBeenLastCalledWith('EcranConflits.Ouvrir', false, 'CLIENT')
  })

  it('lisible et destination', () => {
    expect(lisible(null)).toBe('(vide)')
    expect(lisible('  ')).toBe('(vide)')
    expect(lisible('2026-11-18T00:00:00')).toBe('18/11/2026')
    expect(lisible('DUPONT')).toBe('DUPONT')
    expect(lisible('true')).toBe('Oui')
    expect(lisible('false')).toBe('Non')
    expect(destination('dossier:2019B00412')).toEqual({ genre: 'dossier', valeur: '2019B00412' })
    expect(destination('ecran:PROC_FCLIENT')).toEqual({ genre: 'ecran', valeur: 'PROC_FCLIENT' })
    expect(destination('dossier:')).toBeNull()
    expect(destination(null)).toBeNull()
    expect(destination('autre:x')).toBeNull()
  })
})

// La tete de la liste moderne : vues du critere « Ecarts », filtre rapide Ctrl K (lot 6 des listes modernes).
describe('EcranConflits, liste moderne', () => {
  it('les vues « À traiter » et « Déjà traités » relisent le pont comme le critere « Écarts »', async () => {
    const pont = installerFauxPont({ 'EcranConflits.Ouvrir': (traites) => vue(traites === true ? { traites: true, ecarts: [] } : {}) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    const vues = within(screen.getByRole('group', { name: 'Vues' }))
    expect(vues.getByRole('button', { name: 'À traiter 2' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vues.getByRole('button', { name: 'Déjà traités' }))
    expect(await screen.findByText('Aucun écart traité pour ce filtre.')).toBeTruthy()
    expect(pont.appels).toHaveBeenLastCalledWith('EcranConflits.Ouvrir', true, null)
    expect((screen.getByLabelText('Écarts') as HTMLSelectElement).value).toBe('traites')
    expect(vues.getByRole('button', { name: 'Déjà traités' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('un type de fiche choisi : aucune vue n\'est active', async () => {
    installerFauxPont({ 'EcranConflits.Ouvrir': (_t, table) => vue(table === 'CLIENT' ? { table: 'CLIENT', ecarts: [ecart()] } : {}) })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    fireEvent.change(screen.getByLabelText('Type de fiche'), { target: { value: 'CLIENT' } })
    await screen.findByText(/^1 écart\(s\) — greffe 8002/)
    const vues = within(screen.getByRole('group', { name: 'Vues' }))
    expect(vues.getAllByRole('button').map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'false'])
  })

  it('Ctrl K pose le curseur dans « Aller a une fiche… » ; la frappe filtre la liste affichee sans relire le pont', async () => {
    const pont = installerFauxPont({ 'EcranConflits.Ouvrir': () => vue() })
    render(<EcranConflits ouvrirDossier={() => {}} ouvrir={() => {}} />)
    await screen.findByText(/^2 écart\(s\) — greffe 8002/)
    const appels = pont.appels.mock.calls.length
    const champ = screen.getByLabelText('Aller à une fiche…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'dossier rcs' } })
    const lignes = screen.getAllByRole('row').slice(1, 2)
    expect(lignes[0]!.textContent).toContain('2019B00412')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toContain('1 écart(s) affiché(s)')
    // Le detail suit la premiere ligne affichee : la date de radiation de ce dossier.
    expect(within(screen.getByLabelText("Détail de l'écart")).getByText('Date de radiation')).toBeTruthy()
    expect(pont.appels.mock.calls.length).toBe(appels)
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 2 fiches ne contient « zzz ».')).toBeTruthy()
  })

  it('nomDeFiche separe le nom de la sorte de fiche', () => {
    expect(nomDeFiche('client 3001 — DUPONT')).toEqual({ nom: 'DUPONT', objet: 'client 3001' })
    expect(nomDeFiche('DUPONT')).toEqual({ nom: 'DUPONT', objet: '' })
  })
})
