import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranHistoriqueV2, texteLien, type AffaireHistorique, type DemandeHistorique, type VueEcranHistorique } from './EcranHistoriqueV2'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Ce qui est une ancienne affaire, le blocage entre logiciels, la date relue : le serveur (tests/RecopieRetourCheck). Ici : l'ecran
// montre ce que le pont rend -- les marques des deux logiciels, les liens et leur logiciel, la demande liee en lecture, l'arrivee,
// les anciens numeros -- et lance la recherche par Entree.
function demande(partiel: Partial<DemandeHistorique> = {}): DemandeHistorique {
  return {
    id: 9001, idV2: '702611', logiciel: 'ancien RIG', numero: 'D2607903046', libelle: 'MODIFICATION DIRIGEANT', etat: 'Q', etatLibelle: 'qualifiée',
    creeeLe: '2026-10-09T09:30:00', valideeLe: null, type: 'Modification', numeroArrivee: '2026A004512', bloquee: false, texteBlocage: null, liens: [], ...partiel,
  }
}
function affaire(partiel: Partial<AffaireHistorique> = {}): AffaireHistorique {
  return {
    id: 5495750, idV2: '5495750', logiciel: 'ancien RIG', marque: "traitée dans l'ancien RIG", etat: 'en cours', libelle: 'MODIFICATIONS 2026',
    creeeLe: '2026-10-09T09:30:00', client: 'BOULANGERIE DUPONT', clientCode: 'DUPONT01', dossiers: ['2004B00117'], demandes: [demande()], echanges: [], echangesNote: null,
    ...partiel,
  }
}
function vue(partiel: Partial<VueEcranHistorique> = {}): VueEcranHistorique {
  const nouvelle = affaire({
    id: 5502427, idV2: null, logiciel: 'V3', marque: 'traitée dans la V3', libelle: 'TRANSFERT DE SIEGE',
    demandes: [demande({ id: 9301, idV2: null, logiciel: 'V3', numero: 'V300000412', libelle: 'TRANSFERT DE SIEGE', bloquee: true,
                         texteBlocage: "Bloquée par la demande D2607903046 de l'ancien RIG",
                         liens: [{ lien: 'bloquée par', idDemande: 9001, numero: 'D2607903046', logiciel: 'ancien RIG', etat: 'Q', etatLibelle: 'qualifiée', leve: false, leveLe: null }] })],
  })
  const ancienne = affaire({
    echanges: [
      { sens: 'envoi', id: 41207, codeEdi: 'INPI_FORMALITE', etat: 7, etatLibelle: "Erreur d'intégration", enErreur: true, le: '2026-10-11T12:30:00',
        reference: 'Dépôt des comptes 2025', erreur: 'Pièce jointe absente', demande: 'D2607903047' },
      { sens: 'réception', id: 52119, codeEdi: 'INPI_RETOUR', etat: 2, etatLibelle: 'Intégré avec succès', enErreur: false, le: '2026-10-12T09:00:00',
        reference: 'Accusé de réception INPI', erreur: null, demande: 'D2607903047' },
    ],
  })
  return {
    mode: 'serveur', message: null, greffe: '8002', critere: 'siren', valeur: '512345678', arrivee: null, anciens: [], reprises: [],
    historique: { critere: 'siren', valeur: '512345678', titre: 'BOULANGERIE DUPONT — SIREN 512345678', affaires: [nouvelle, ancienne], anciennes: 1, nouvelles: 1, note: null },
    ...partiel,
  }
}

describe('EcranHistoriqueV2', () => {
  it('REC-010 CA01 : Entrée lance la recherche ; les affaires des deux logiciels, les anciennes marquées « traitée dans l\'ancien RIG »', async () => {
    const chercher = vi.fn(() => vue())
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': chercher })
    render(<EcranHistoriqueV2 />)
    const saisie = screen.getByPlaceholderText('512345678')
    fireEvent.change(saisie, { target: { value: '512345678' } })
    fireEvent.keyDown(saisie, { key: 'Enter' })
    expect((await screen.findByText(/ · /, { selector: '.rig-lm-synthese' })).textContent).toBe("2 affaires · 1 dans l'ancien RIG · 1 dans la V3")
    expect(chercher).toHaveBeenCalledWith('siren', '512345678')
    const lignes = within(screen.getByRole('table', { name: 'Affaires' })).getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('traitée dans la V3')
    expect(lignes[1]!.textContent).toContain("traitée dans l'ancien RIG")
    expect(lignes[1]!.textContent).toContain('BOULANGERIE DUPONT')
    // Le logiciel en pastille (V3 en vert, ancien RIG en orange), l'etat en point colore, le client avec son avatar et son code.
    expect(lignes.map((l) => l.querySelector('.rig-lm-pastille')!.className)).toEqual(['rig-lm-pastille rig-lm-t-vert', 'rig-lm-pastille rig-lm-t-orange'])
    expect(lignes[0]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-bleu')
    expect(lignes[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('BOULANGERIE DUPONT')
    expect(lignes[0]!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('DUPONT01')
    expect(document.body.querySelector('.rig-lm-pied')!.textContent).toBe('2 affaires affichées')
  })

  it('« Aller à une affaire… » filtre la liste affichee sans relancer la recherche ; Entree choisit l\'affaire restee seule', async () => {
    const chercher = vi.fn(() => vue())
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': chercher })
    render(<EcranHistoriqueV2 />)
    // Sans liste, ni champ ni Ctrl K.
    expect(screen.queryByLabelText('Aller à une affaire…')).toBeNull()
    fireEvent.click(screen.getByText('Rechercher'))
    const champ = await screen.findByLabelText('Aller à une affaire…')
    fireEvent.change(champ, { target: { value: 'modifications' } })
    const lignes = within(screen.getByRole('table', { name: 'Affaires' })).getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain('MODIFICATIONS 2026')
    expect(document.body.querySelector('.rig-lm-synthese')!.textContent).toBe("1 sur 2 affaires · 1 dans l'ancien RIG · 0 dans la V3")
    expect(chercher).toHaveBeenCalledTimes(1)
    // Le detail montre encore la premiere affaire (celle de la V3) tant qu'Entree n'a pas choisi.
    expect(screen.getByLabelText("Détail de l'affaire").textContent).not.toContain("Cette affaire se traite dans l'ancien RIG")
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(screen.getByLabelText("Détail de l'affaire").textContent).toContain("Cette affaire se traite dans l'ancien RIG")
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 2 affaires ne contient « zzz ».')).toBeTruthy()
  })

  it('REC-011 CA01, CA03 : une demande bloquée dit par quelle demande et dans quel logiciel ; le lien ouvre la demande liée en lecture', async () => {
    const lue = demande({ liens: [{ lien: 'bloque', idDemande: 9301, numero: 'V300000412', logiciel: 'V3', etat: 'Q', etatLibelle: 'qualifiée', leve: false, leveLe: null }] })
    const ouvrirDemande = vi.fn(() => lue)
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': () => vue(), 'EcranHistoriqueV2.Demande': ouvrirDemande })
    render(<EcranHistoriqueV2 />)
    fireEvent.click(screen.getByText('Rechercher'))
    const detail = await screen.findByLabelText("Détail de l'affaire")
    expect(detail.textContent).toContain("Bloquée par la demande D2607903046 de l'ancien RIG")
    fireEvent.click(within(detail).getByText('bloquée par D2607903046 (ancien RIG, qualifiée)'))
    const liee = await screen.findByLabelText('Demande liée')
    expect(ouvrirDemande).toHaveBeenCalledWith(9001)
    expect(liee.textContent).toContain('lecture seule')
    expect(liee.textContent).toContain('Ancien RIG')
    expect(liee.textContent).toContain('bloque V300000412 (V3, qualifiée)')
  })

  it('REC-010 CA03, CA04 : une ancienne affaire se dit en lecture seule et liste ses échanges avec leur état', async () => {
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': () => vue() })
    render(<EcranHistoriqueV2 />)
    fireEvent.click(screen.getByText('Rechercher'))
    const lignes = within(await screen.findByRole('table', { name: 'Affaires' })).getAllByRole('row').slice(1)
    fireEvent.click(lignes[1]!)
    const detail = screen.getByLabelText("Détail de l'affaire")
    expect(detail.textContent).toContain("Cette affaire se traite dans l'ancien RIG : la V3 la montre en lecture seule")
    const echanges = within(detail).getByRole('table', { name: 'Échanges' })
    expect(echanges.textContent).toContain('INPI_FORMALITE')
    // L'etat en clair, l'echec marque avec son message : « 7 » seul ne dit pas a l'agent que l'envoi a echoue (refus du 22/11/2026).
    const [envoi, reception] = within(echanges).getAllByRole('row').slice(1)
    const etatEnvoi = within(envoi!).getByText("Erreur d'intégration")
    expect(etatEnvoi.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(envoi!.textContent).toContain('Pièce jointe absente')
    expect(envoi!.textContent).not.toMatch(/(^|\D)7(\D|$)/)
    expect(within(reception!).getByText('Intégré avec succès').className).not.toContain('rig-lm-t-rouge')
    expect(document.body.querySelector('button.rig-bouton--fort')?.textContent).toBe('Rechercher')
  })

  it('REC-018 CA03 : une arrivée liste ses demandes des deux logiciels, celles de l\'ancien RIG marquées', async () => {
    installerFauxPont({
      'EcranHistoriqueV2.Chercher': () => vue({
        critere: 'arrivee', valeur: '2026V300042', historique: null,
        arrivee: { id: 7092482, idV2: '7192482', numero: '2026V300042', mode: 'C', le: '2026-11-21T08:47:13', nom: 'PLI MIXTE', references: null,
                   demandes: [demande({ id: 9301, idV2: null, logiciel: 'V3', numero: 'V300000412' }), demande()], dansAncienRig: 1 },
      }),
    })
    render(<EcranHistoriqueV2 />)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'arrivee' } })
    fireEvent.click(screen.getByText('Rechercher'))
    const arrivee = await screen.findByLabelText('Arrivée')
    expect(arrivee.textContent).toContain('2 demande(s), dont 1 dans l\'ancien RIG')
    const lignes = within(arrivee).getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('V3')
    expect(lignes[1]!.textContent).toContain('Ancien RIG')
    // Le logiciel en pastille, teintee comme dans la liste des affaires : la V3 en vert, l'ancien RIG en orange.
    expect([lignes[0]!, lignes[1]!].map((l) => l.querySelector('.rig-lm-pastille')!.className))
      .toEqual(['rig-lm-pastille rig-lm-t-vert', 'rig-lm-pastille rig-lm-t-orange'])
  })

  it('REC-009 CA01, CA02 : un ancien numéro mène à la fiche RIG, son numéro Infogreffe compris', async () => {
    const chercher = vi.fn(() => vue({
      critere: 'ancien', valeur: 'DED-2004-007', historique: null,
      anciens: [{ logiciel: 'migration', tableRig: 'DOSSIER_RCS', sorteFiche: 'dossier RCS', ancienId: 'NO-DED-2004-007', ancienNumero: 'DED-2004-007', idRig: 177021,
                  numeroRig: '2004B00117', numeroInfogreffe: 'IG2004B00117', tableV3: 'rec.DossierRcs', idV3: '177021' }],
    }))
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': chercher })
    render(<EcranHistoriqueV2 />)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'ancien' } })
    const saisie = screen.getByPlaceholderText('DED-2004-007')
    fireEvent.change(saisie, { target: { value: 'DED-2004-007' } })
    fireEvent.keyDown(saisie, { key: 'F7' })
    const table = await screen.findByRole('table', { name: 'Anciens numéros' })
    expect(chercher).toHaveBeenCalledWith('ancien', 'DED-2004-007')
    expect(table.textContent).toContain('dossier RCS')
    expect(table.textContent).toContain('2004B00117')
    expect(table.textContent).toContain('IG2004B00117')
  })

  it('un serveur indisponible se dit, sans tableau', async () => {
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': () => vue({ mode: 'erreur', historique: null, message: "La recopie entre l'ancien RIG et la V3 n'est pas en service" }) })
    render(<EcranHistoriqueV2 />)
    fireEvent.click(screen.getByText('Rechercher'))
    expect((await screen.findByRole('alert')).textContent).toContain('Indisponible')
    expect(screen.queryByRole('table', { name: 'Affaires' })).toBeNull()
  })

  // Un tableau à largeur fixe donne 0 px à sa seule colonne libre (sans largeur) dès que ses colonnes fixes remplissent la place :
  // en fenêtre étroite (1 080 px, critères au-dessus des résultats), le nom de l'affaire et les liens entre demandes disparaissaient
  // (capture du 22/11/2026). Chaque tableau réserve donc une largeur minimale qui laisse sa place à la colonne libre ; en deçà,
  // son cadre défile. jsdom ne mesure rien : on relit le contrat posé sur le tableau (colonnes et largeur minimale).
  function placeDeLaColonneLibre(table: HTMLElement): number {
    const colonnes = [...table.querySelectorAll('col')]
    expect(colonnes.filter((c) => !c.style.width)).toHaveLength(1)
    const fixes = colonnes.reduce((s, c) => s + (parseFloat(c.style.width) || 0), 0)
    const marges = colonnes.length * 16   // var(--rig-esp-2) de chaque côté de chaque cellule (rig-accueil.css, .rig-table)
    return (parseFloat(table.style.minWidth) || 0) - fixes - marges
  }

  it('fenêtre étroite : chaque tableau garde au moins 200 px à sa colonne libre (affaire, libellé et liens, ancien numéro…)', async () => {
    const reprise = { tableV2: 'CLIENT', idV2: '578727', tableV3: 'rec.Client', idV3: '578727', motif: 'client « Fake data » d\'une ancienne reprise', libelle: 'Fake data' }
    const ancien = { logiciel: 'migration', tableRig: 'DOSSIER_RCS', sorteFiche: 'dossier RCS', ancienId: 'NO-DED-2004-007', ancienNumero: 'DED-2004-007', idRig: 177021,
                     numeroRig: '2004B00117', numeroInfogreffe: 'IG2004B00117', tableV3: 'rec.DossierRcs', idV3: '177021' }
    const vues = [vue(), vue({ critere: 'ancien', historique: null, anciens: [ancien] }), vue({ critere: 'reprises', historique: null, reprises: [reprise] })]
    let rang = 0
    installerFauxPont({ 'EcranHistoriqueV2.Chercher': () => vues[Math.min(rang++, vues.length - 1)]! })
    render(<EcranHistoriqueV2 />)
    const vus: Record<string, number> = {}
    const relever = () => document.body.querySelectorAll('table').forEach((t) => { vus[t.getAttribute('aria-label') ?? '?'] = placeDeLaColonneLibre(t) })

    fireEvent.click(screen.getByText('Rechercher'))
    const lignes = within(await screen.findByRole('table', { name: 'Affaires' })).getAllByRole('row').slice(1)
    relever()
    fireEvent.click(lignes[1]!)                                   // l'ancienne affaire : ses échanges
    relever()
    fireEvent.click(screen.getByText('Rechercher'))
    await screen.findByRole('table', { name: 'Anciens numéros' })
    relever()
    fireEvent.click(screen.getByText('Rechercher'))
    await screen.findByRole('table', { name: 'Fiches à vérifier' })
    relever()

    expect(Object.keys(vus).sort()).toEqual(['Affaires', 'Anciens numéros', 'Demandes', 'Fiches à vérifier', 'Échanges'])
    for (const [nom, place] of Object.entries(vus)) expect(place, nom).toBeGreaterThanOrEqual(200)
  })

  it('le texte d\'un lien levé garde sa trace', () => {
    expect(texteLien({ lien: 'bloquée par', idDemande: 1, numero: 'D1', logiciel: 'ancien RIG', etat: 'V', etatLibelle: 'validée', leve: true, leveLe: null }))
      .toBe('bloquée par D1 (ancien RIG, validée, levé)')
  })
})
