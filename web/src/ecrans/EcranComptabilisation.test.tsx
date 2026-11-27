import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranComptabilisation, type Journee, type VueComptabilisation } from './EcranComptabilisation'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les journees que le pont rend (Pont/EcranComptabilisation.cs, Exemple) : celles de l'ecran Blazor, journee du 09/09/2026 en tete.
// Milliers : euros() (format.ts) les separe par U+202F comme .NET ; montant() (ListeModerne.tsx) par U+00A0.
const journees: Journee[] = [
  {
    date: '2026-09-09', nbFactures: 14, montantTotalTTC: 381240, nbAffairesEnCours: 11, nbSansDemande: 1,
    remis: [
      { numeroRemis: 4821, modePaiement: 'Cheque', typeMode: 'CH', nbEncaissement: 9, sommeEncaissement: 214000, dateEncaissement: '2026-09-09' },
      { numeroRemis: 4822, modePaiement: 'Virement', typeMode: 'VI', nbEncaissement: 4, sommeEncaissement: 145040, dateEncaissement: '2026-09-09' },
      { numeroRemis: 0, modePaiement: 'Carte bancaire', typeMode: 'CB', nbEncaissement: 2, sommeEncaissement: 22200, dateEncaissement: '2026-09-09' },
    ],
  },
  {
    date: '2026-09-08', nbFactures: 6, montantTotalTTC: 120480, nbAffairesEnCours: 6, nbSansDemande: 0,
    remis: [
      { numeroRemis: 4818, modePaiement: 'Cheque', typeMode: 'CH', nbEncaissement: 5, sommeEncaissement: 98080, dateEncaissement: '2026-09-08' },
      { numeroRemis: 4819, modePaiement: 'Especes', typeMode: 'ES', nbEncaissement: 1, sommeEncaissement: 22400, dateEncaissement: '2026-09-08' },
    ],
  },
  { date: '2026-09-07', nbFactures: 3, montantTotalTTC: 45600, nbAffairesEnCours: 2, nbSansDemande: 1, remis: [] },
]
const vue = (v: Partial<VueComptabilisation> = {}): VueComptabilisation =>
  ({ journees, total: 3, anomalie: null, encaissementsSeuls: true, mode: 'serveur', message: null, ...v })
const brancher = (v: Partial<VueComptabilisation> = {}) => installerFauxPont({ 'EcranComptabilisation.Journees': () => vue(v) })

const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeurs = (c: HTMLElement) => [...c.querySelectorAll('.rig-lm-carte__valeur')].map((v) => v.textContent)
const premiere = () => screen.findByRole('button', { name: '09/09/2026 — 14 facture(s), 15 encaissement(s)' })

describe('EcranComptabilisation', () => {
  it('affiche les journees, ce qui reste et les remis de la premiere journee', async () => {
    brancher()
    const { container } = render(<EcranComptabilisation />)
    expect((await premiere()).className).toBe('rig-bascule rig-bascule--actif')
    expect([...container.querySelectorAll('.rig-chip')].map((p) => p.textContent)).toEqual([
      '3 jour(s) a comptabiliser', '23 facture(s)', '5 017,20 € encaisses',
    ])
    expect(container.querySelector('.rig-lm-synthese')!.textContent)
      .toBe('14 facture(s) · 3 812,40 € TTC · 15 encaissement(s) · 3 812,40 € encaisses')
    // L'agregat des factures de la journee (v2 : FactureJour) est la premiere carte.
    expect(valeurs(container)).toEqual([
      '3 812,40 € TTC · 14 facture(s) · 11 affaire(s) en cours · 1 sans demande',
      '3 812,40 €',
      '222,00 € sur 3 812,40 €',
    ])
    expect(lignes(container)).toEqual([
      ['4821', 'Cheque', '9', '2 140,00 €', '09/09/2026'],
      ['4822', 'Virement', '4', '1 450,40 €', '09/09/2026'],
      ['non remis', 'Carte bancaire', '2', '222,00 €', '09/09/2026'],
    ])
    // Remis en point (vert, orange s'il n'est pas remis), mode en pastille.
    expect([...container.querySelectorAll('tbody .rig-lm-etat')].map((e) => e.className.split(' ')[1]))
      .toEqual(['rig-lm-t-vert', 'rig-lm-t-vert', 'rig-lm-t-orange'])
    expect(container.querySelector('tbody .rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    // Une pastille trop longue est coupee sans « … » (decision du plan, lot 3) : le mode entier est au survol de sa cellule.
    expect([...container.querySelectorAll('tbody .rig-lm-pastille')].map((p) => p.closest('td')!.getAttribute('title')))
      .toEqual(['Cheque', 'Virement', 'Carte bancaire'])
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 remis affiche(s)Encaissements 15Total 3 812,40 €Non remis 222,00 €')
  })

  it('choisir une autre journee change l\'en-tete et la grille', async () => {
    brancher()
    const { container } = render(<EcranComptabilisation />)
    fireEvent.click(await screen.findByRole('button', { name: '07/09/2026 — 3 facture(s), 0 encaissement(s)' }))
    expect(screen.getByText('Journee du 07/09/2026')).toBeTruthy()
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun encaissement pour cette journee.')
    // Les boutons de .rig-criteres__actions gardent le blanc que Razor laissait entre eux.
    expect(container.querySelector('.rig-criteres__actions')!.textContent)
      .toBe('Comptabiliser les factures Comptabiliser les encaissements Remettre en banque Lettrage des PV et REG')
  })

  it('les cartes filtrent les remis : un mode, ou les non remis ; une autre journee enleve le filtre', async () => {
    brancher()
    const { container } = render(<EcranComptabilisation />)
    await premiere()
    fireEvent.click(screen.getByRole('button', { name: 'Virement 1 450,40 €', pressed: false }))
    expect(lignes(container).map((l) => l[0])).toEqual(['4822'])
    fireEvent.click(screen.getByRole('button', { name: /^Encaissements par mode/ }))
    expect(lignes(container)).toHaveLength(3)

    const nonRemis = container.querySelectorAll<HTMLElement>('.rig-lm-carte')[2]!
    fireEvent.click(nonRemis)
    expect(nonRemis.getAttribute('aria-pressed')).toBe('true')
    expect(lignes(container).map((l) => l[0])).toEqual(['non remis'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('1 remis affiche(s)')

    // La journee du 08/09 n'a que des remis : le filtre ne la vide pas, il est enleve.
    fireEvent.click(screen.getByRole('button', { name: '08/09/2026 — 6 facture(s), 6 encaissement(s)' }))
    expect(lignes(container).map((l) => l[0])).toEqual(['4818', '4819'])
  })

  it('la teinte vient du TYPE du mode (libelle de la base, accentue) ; l\'observation de RIG v2 est dite sous le mode', async () => {
    brancher({ journees: [{ date: '2002-10-29', nbFactures: 0, montantTotalTTC: 0, nbAffairesEnCours: 0, nbSansDemande: 0, remis: [
      { numeroRemis: 12, modePaiement: 'Chèque', typeMode: 'CH', nbEncaissement: 1, sommeEncaissement: 1000, dateEncaissement: '2002-10-29' },
      { numeroRemis: 0, modePaiement: 'Chèque', typeMode: 'CH', nbEncaissement: 2, sommeEncaissement: 2000, dateEncaissement: '2002-10-29',
        observation: 'Attention des encaissements remis du même type sont déjà présents' },
    ] }], total: 1 })
    const { container } = render(<EcranComptabilisation />)
    await screen.findByText('Journee du 29/10/2002')
    expect([...container.querySelectorAll('tbody .rig-lm-pastille')].map((p) => p.className))
      .toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-bleu'])
    expect(screen.getByText('Attention des encaissements remis du même type sont déjà présents')).toBeTruthy()
  })
})

// D'ou viennent les journees (plan « RIG v3 100 % fonctionnel », lot 13) : la demonstration est marquee « exemple », la base ne
// l'est pas, une panne montre son motif et AUCUNE journee ; l'anomalie de RIG v2 vide l'arbre (Pont/EcranComptabilisationBase.cs).
describe('EcranComptabilisation : provenance des journees', () => {
  it('sans serveur, les journees de demonstration sont marquees « exemple »', async () => {
    brancher({ mode: 'demo' })
    const { container } = render(<EcranComptabilisation />)
    await premiere()
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Journées de démonstration')
  })

  it('branche au serveur, les journees de la base, sans mention', async () => {
    brancher()
    const { container } = render(<EcranComptabilisation />)
    await premiere()
    expect(container.querySelector('[role="note"]')).toBeNull()
  })

  it('en panne, le motif et aucune journee (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranComptabilisation.Journees': () => vue({ journees: [], total: 0, encaissementsSeuls: false, mode: 'erreur',
                                                                       message: 'La base du greffe 3801 ne répond pas.' }) })
    const { container } = render(<EcranComptabilisation />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(container.querySelectorAll('.rig-bascule')).toHaveLength(0)
    expect(screen.queryByText('Aucune journee a comptabiliser.')).toBeNull()
  })

  it('une facture sans demande : le message de RIG v2, aucune journee', async () => {
    brancher({ journees: [], total: 0, anomalie: 'ANOMALIE : 1 facture(s) ne sont rattachées à aucune demande. Merci de contacter Amitel.\n\nFacture N° F1 (affaire non identifiée)' })
    const { container } = render(<EcranComptabilisation />)
    expect((await screen.findByRole('alert')).textContent).toContain('Facture N° F1 (affaire non identifiée)')
    expect(container.querySelectorAll('.rig-bascule')).toHaveLength(0)
    // Le panneau de gauche (replie au depart) se rouvre par le bouton « Criteres » de la tete, meme sans journee.
    // fix-ok: mesure sur la copie de dev le 27/11/2026 (capture k4.png, arbre d'accessibilite) : l'anomalie seule, sans tete
    // de liste, laissait le panneau replie sans bouton pour le rouvrir (.rig-lm--criteres-replies, ListeModerne.tsx:140).
    const criteres = screen.getByRole('button', { name: /Criteres/ })
    fireEvent.click(criteres)
    expect(criteres.getAttribute('aria-expanded')).toBe('true')
    expect(container.querySelector('.rig-lm--criteres-replies')).toBeNull()
  })

  it('le libelle du parametre ne s\'affiche que si le greffe a EXPRESSO_FIGERFACTAUTO a « 0 »', async () => {
    brancher({ encaissementsSeuls: false })
    render(<EcranComptabilisation />)
    await premiere()
    expect(screen.queryByText('Parametre du greffe')).toBeNull()
    cleanup()
    brancher({ encaissementsSeuls: true })
    render(<EcranComptabilisation />)
    await premiere()
    expect(screen.getByText('Parametre du greffe')).toBeTruthy()
  })

  it('sans journee dans la base : la liste le dit', async () => {
    brancher({ journees: [], total: 0 })
    render(<EcranComptabilisation />)
    expect(await screen.findByText('Aucune journee a comptabiliser.')).toBeTruthy()
  })
})
