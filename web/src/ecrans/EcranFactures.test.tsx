import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranFactures } from './EcranFactures'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'
import type { ContexteDossierVue } from '../plan/types'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les montants attendus sont ceux de la capture de l'ecran Blazor (18/11/2026) :
// Total TTC 458,63 EUR, reste a encaisser 258,63 EUR pour 200 encaisses.
function ouvrir() {
  installerFauxPont({ 'EcranFactures.DateDuJour': () => '2026-09-09' })
  return render(<EcranFactures />)
}

// La synthese de la tete de liste (socle des listes modernes, lot 4) : le compte et les totaux.
const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const solde = (c: HTMLElement) => c.querySelector('section > .rig-criteres__bloc > .rig-etat')!
/** Les cellules de chaque ligne d'article affichee (hors lignes depliees). */
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
// fix-ok: decision du plan (lot 4) appliquee en plusieurs pas -- l'article en libelle gras + code gris, sans avatar ; pas un correctif.
/** Le code de chaque article affiche, en gris sous son libelle (la ligne « Aucun article… » n'en a pas). */
const codes = (c: HTMLElement) => [...c.querySelectorAll('tbody .rig-lm-identite__objet')].map((o) => o.textContent)

describe('EcranFactures', () => {
  it('affiche le total et le reste comme l\'ecran Blazor', async () => {
    const { container } = ouvrir()
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
    expect(solde(container).textContent).toBe('Solde restant 258,63 EUR')
    expect(solde(container).className).toBe('rig-etat rig-etat--attente')
    expect(await screen.findByDisplayValue('2026-09-09')).toBeTruthy()
  })

  it('ecrit les colonnes de la grille a la francaise ; les secondaires sont dans la ligne depliee', () => {
    const { container } = ouvrir()
    // Chevron, Article (libelle en gras, code en gris), Quantite, Etat, Montant TTC, Commentaire, menu.
    expect([...container.querySelectorAll('thead th')].map((th) => th.textContent))
      .toEqual(['', 'Article', 'Quantite', 'Etat', 'Montant TTC', 'Commentaire', ''])
    expect(lignes(container)[0]).toEqual(['', 'Immatriculation societeIMM01', '1', 'Retenu', '194,85 €', '', ''])
    const article = container.querySelector('tbody tr')!.querySelectorAll('td')[1]!
    expect(article.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Immatriculation societe')
    expect(article.querySelector('.rig-lm-identite__objet')!.textContent).toBe('IMM01')
    expect(article.getAttribute('title')).toBe('Immatriculation societe · IMM01')
    // Un article n'est ni un client ni un dossier : pas d'avatar d'initiales.
    expect(container.querySelector('tbody .rig-lm-avatar')).toBeNull()
    expect(lignes(container)[1]![5]).toBe('Changement de gerant')
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent))
      .toEqual(['Prix UHT37,45', 'Taux TVA20,0 %', 'BODACC105,90', 'INPI0,00', 'Autre0,00'])
  })

  it('un clic sur un article le retire du total, un second l\'y remet', () => {
    const { container } = ouvrir()
    const ligne = screen.getByText('IMM01').closest('tr')!
    fireEvent.click(ligne)
    expect(ligne.className).toBe('rig-table__ligne--inerte')
    expect(ligne.textContent).toContain('Retire')
    expect(compte(container)).toBe('5 articles · 1 retire · total TTC 263,78 € · reste a encaisser 63,78 €')
    fireEvent.click(ligne)
    expect(ligne.hasAttribute('class')).toBe(false)
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
  })

  it('le chevron deplie la ligne sans retirer l\'article ; le menu « … » le retire', () => {
    const { container } = ouvrir()
    const ligne = screen.getByText('IMM01').closest('tr')!
    fireEvent.click(ligne.querySelector('.rig-lm-chevron')!)
    expect(ligne.className).toBe('rig-lm-ouverte')
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'article IMM01' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Retirer du total' }))
    expect(ligne.className).toBe('rig-table__ligne--inerte rig-lm-ouverte')
    expect(compte(container)).toBe('5 articles · 1 retire · total TTC 263,78 € · reste a encaisser 63,78 €')
    // Le bouton de la ligne depliee fait le meme geste, dans l'autre sens.
    fireEvent.click(screen.getByRole('button', { name: 'Remettre dans le total' }))
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
  })

  it('les cartes chiffrent la facture ; un clic sur une repartition filtre les articles, un second les remet', () => {
    const { container } = ouvrir()
    const cartes = () => [...container.querySelectorAll('.rig-lm-carte')].map((c) => c.querySelector('.rig-lm-carte__valeur')!.textContent)
    expect(cartes()).toEqual(['458,63 €', '200,00 € sur 458,63 €', '236,60 €'])
    // BODACC : IMM01 et MOD02 seulement (105,90 + 108,00).
    fireEvent.click(screen.getByRole('button', { name: 'BODACC 213,90 €', pressed: false }))
    expect(codes(container)).toEqual(['IMM01', 'MOD02'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('2 articles affiches')
    // Le total ne bouge pas : le filtre montre, il ne retire rien.
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
    fireEvent.click(screen.getByRole('button', { name: 'BODACC 213,90 €', pressed: true }))
    expect(lignes(container)).toHaveLength(5)

    // Retires : l'article retire, seul ; la carte « Total TTC » enleve le filtre.
    fireEvent.click(screen.getByText('DIV05').closest('tr')!)
    fireEvent.click(screen.getByRole('button', { name: 'Retires 14,64 €', pressed: false }))
    expect(codes(container)).toEqual(['DIV05'])
    fireEvent.click(screen.getByRole('button', { name: /^Total TTC/ }))
    expect(lignes(container)).toHaveLength(5)
  })

  it('une facture couverte est soldee (calcul exact, pas en virgule flottante)', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '458.63' } })
    expect(solde(container).textContent).toBe('Facture soldee')
    expect(solde(container).className).toBe('rig-etat rig-etat--ok')
  })

  it('une saisie illisible garde le dernier montant lisible, comme le @bind d\'un decimal', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '' } })
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
  })

  it('« Changer de client » fait defiler les fiches et met la fiche a jour', () => {
    ouvrir()
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('LEROY')
    expect(screen.getByText(/Solde client/).textContent).toBe('Solde client 812,40 EUR')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('BLANC')
    expect((screen.getByLabelText('Nom du client') as HTMLSelectElement).value).toBe('CABINET BLANC')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('DIVERS')
    expect(screen.getByText(/Solde client/).className).toBe('rig-etat rig-etat--ok')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('LEROY')
  })

  it('Facturer / Tarifer : un choix exclusif qui dit ce qu\'il fera', () => {
    ouvrir()
    const facturer = screen.getByRole('button', { name: 'Facturer' })
    const tarifer = screen.getByRole('button', { name: 'Tarifer' })
    expect(facturer.className).toBe('rig-onglet rig-onglet--actif')
    expect(screen.getByText('Facturer : une facture numerotee sera emise.')).toBeTruthy()

    fireEvent.click(tarifer)
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')
    expect(facturer.className).toBe('rig-onglet')
    expect(screen.getByText('Tarifer : le montant est calcule sans emettre de facture.')).toBeTruthy()
  })

  it('garde l\'espace entre « Changer de client » et la pastille (bloc en ligne)', () => {
    const { container } = ouvrir()
    const actions = container.querySelector('.rig-criteres__actions')!
    expect(actions.childNodes[1]?.textContent).toBe(' ')
  })
})

// Ouvert depuis la page d'une affaire : la facture est pour elle, deja choisie
// (bloc « Pour le dossier » ; plan « Vue du dossier », lot 2) -- et c'est une facture
// DE cette affaire (lot 8, 20/11/2026).
// fix-ok: constate sur 004182 le 20/11 -- l'ecran partait toujours de la facture de
// demonstration (client LEROY, demande D2026-04118, 5 articles), quel que soit le dossier.

/** Les demandes du jeu de demonstration (Metier/JeuDemo.cs, DemandesBase), par dossier,
 *  telles que Pont/EcranFactures.cs les rend : une demande RCS porte le numero de son dossier. */
const DemandesDemo: Record<string, { numero: string; client: string }> = {
  '004182': { numero: '004182', client: 'AMIENS BILL.' },
  '004151': { numero: '004151', client: 'TRANSNORD' },
  '004176': { numero: '004176', client: 'SCI DU PARC' },
  '004179': { numero: '004179', client: 'MARTIN EURL' },
}

function ouvrirPour(dossier: ContexteDossierVue | null) {
  const pont = installerFauxPont({
    'EcranFactures.DateDuJour': () => '2026-09-09',
    'EcranFactures.DemandeDe': (n) => DemandesDemo[String(n)] ?? null,
  })
  return { pont, ...render(<EcranFactures dossier={dossier} />) }
}


const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

// Rien de la facture de demonstration (client LEROY de Reims, demande D2026-04118,
// « Changement de gerant ») ne doit etre emprunte par une facture d'affaire.
function sansRienDeLaDemo(c: HTMLElement) {
  for (const champ of c.querySelectorAll<HTMLInputElement | HTMLSelectElement>('input, select')) {
    expect(champ.value, champ.outerHTML).not.toMatch(/LEROY|D2026-04118|Reims|V-2026-8801/)
  }
  expect(c.textContent).not.toMatch(/Changement de gerant|Refacturation INPI|812,40/)
}

// Une facture SANS article n'est ni a emettre ni a encaisser : pas de « Facturer »
// (une facture numerotee a 0 EUR), pas de total, pas de « Facture soldee ».
function sansFactureNiEncaissement(c: HTMLElement) {
  expect(compte(c)).toBe('Aucun article')
  expect(c.textContent).not.toMatch(/Total TTC|Facture soldee|une facture numerotee sera emise/)
  expect(c.querySelector('.rig-lm-cartes')).toBeNull()
  expect(c.querySelector('.rig-lm-pied')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Facturer' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Tarifer' })).toBeNull()
  expect(screen.getByText('Aucune facture ne sera emise.')).toBeTruthy()
  expect(screen.queryByLabelText('Montant encaisse')).toBeNull()
}

const AmiensBillards = contexteDossier({ numero: '004182', siren: '109 678 235', objet: 'Immatriculation - AMIENS BILLARDS', nom: 'AMIENS BILLARDS' })
const Transnord = contexteDossier({ numero: '004151', siren: '318 552 004', objet: 'Depot de bilan - TRANSNORD SA', nom: 'TRANSNORD SA' })
const MartinEurl = contexteDossier({ numero: '004179', siren: '521 004 332', objet: 'Radiation - MARTIN EURL', nom: 'MARTIN EURL' })

describe('EcranFactures ouvert depuis une affaire', () => {
  it('montre l\'affaire choisie en tete ; sans affaire, l\'ecran d\'avant', async () => {
    const avec = ouvrirPour(contexteDossier())
    expect(screen.getByText('Pour le dossier')).toBeTruthy()
    expect(valeur('N de dossier')).toBe('004176')
    expect(valeur('Siren')).toBe('812 445 901')
    expect(valeur('Denomination')).toBe('SCI DU PARC')
    await waitFor(() => expect(valeur('N de demande')).toBe('004176'))
    avec.unmount()

    const sans = ouvrirPour(null)
    expect(screen.queryByText('Pour le dossier')).toBeNull()
    expect(screen.queryByLabelText('N de dossier')).toBeNull()
    expect(sans.container.querySelector('.rig-criteres > .rig-kicker')!.textContent).toBe('Informations client')
  })

  it('004182, une immatriculation : l\'article d\'immatriculation du tarif, sa demande, son client', async () => {
    const { container, pont } = ouvrirPour(AmiensBillards)
    expect(lignes(container)).toEqual([
      ['', 'Immatriculation societeIMM01', '1', 'Retenu', '194,85 €', '', ''],
    ])
    await waitFor(() => expect(valeur('N de demande')).toBe('004182'))
    expect(pont.appels).toHaveBeenCalledWith('EcranFactures.DemandeDe', '004182')
    // Le client de la demande (son abrege, JeuDemo.Demandes) : la societe de l'affaire.
    expect(valeur('Abrege')).toBe('AMIENS BILL.')
    expect(valeur('Nom du client')).toBe('AMIENS BILLARDS')
    // Ni type, ni adresse, ni solde : l'affaire ne les dit pas.
    expect(valeur('Type de client')).toBe('')
    expect(valeur('Adresse')).toBe('')
    expect(screen.queryByText(/Solde client/)).toBeNull()
    // Rien n'est encaisse sur cette facture : tout reste a encaisser.
    expect(compte(container)).toBe('1 article · total TTC 194,85 € · reste a encaisser 194,85 €')
    expect(valeur('Montant encaisse')).toBe('')
    expect(solde(container).textContent).toBe('Solde restant 194,85 EUR')
    // Le jumeau de 004151 : une facture qui a un article s'emet, comme depuis l'accueil.
    expect(screen.getByRole('button', { name: 'Facturer' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(screen.getByText('Facturer : une facture numerotee sera emise.')).toBeTruthy()
    sansRienDeLaDemo(container)
  })

  // Un « depot de bilan » est la declaration de cessation des paiements (procedure
  // collective ; glossaire du Brain, 50-metier-judiciaire), PAS le depot des comptes
  // annuels : DEP03 ne le facture pas. Et son depot ne se facture pas au greffe : rien
  // a facturer, donc ni facture numerotee a 0 EUR ni « Facture soldee ».
  // fix-ok: refuse par le chef le 20/11 -- 004151 etait facture DEP03 « Depot des comptes annuels »,
  // puis montrait une facture vide « soldee », emissible, comme si le tarif avait un trou.
  it('004151, un depot de bilan : rien a facturer, depot gratuit -- mais sa demande et son client', async () => {
    const { container, pont } = ouvrirPour(Transnord)
    expect(lignes(container)).toEqual([[
      'Rien a facturer : Depot de bilan, depot gratuit au greffe.',
    ]])
    expect(container.textContent).not.toMatch(/DEP03|Depot des comptes annuels|Aucun article du tarif/)
    sansFactureNiEncaissement(container)
    await waitFor(() => expect(valeur('N de demande')).toBe('004151'))
    expect(pont.appels).toHaveBeenCalledWith('EcranFactures.DemandeDe', '004151')
    expect(valeur('Abrege')).toBe('TRANSNORD')
    expect(valeur('Nom du client')).toBe('TRANSNORD SA')
    sansRienDeLaDemo(container)
  })

  it('une modification : l\'article de modification, sans le commentaire de la demo', async () => {
    const { container } = ouvrirPour(contexteDossier())
    expect(lignes(container)).toEqual([
      ['', 'Modification statutaireMOD02', '1', 'Retenu', '162,72 €', '', ''],
    ])
    fireEvent.click(screen.getByRole('button', { name: 'Deplier la ligne' }))
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent))
      .toEqual(['Prix UHT45,60', 'Taux TVA20,0 %', 'BODACC108,00', 'INPI0,00', 'Autre0,00'])
    await waitFor(() => expect(valeur('N de demande')).toBe('004176'))
    sansRienDeLaDemo(container)
  })

  // Le jumeau de 004151 : meme nature, sans demande RCS (Pont/EcranFactures.cs, DemandeDe
  // rend null) -- ni numero de demande ni client, rien n'est emprunte a la demo.
  it('J-00931, un depot de bilan judiciaire sans demande : rien d\'emprunte, tout reste vide', async () => {
    const { container, pont } = ouvrirPour(contexteDossier({
      numero: 'J-00931', siren: '318 552 004', objet: 'Depot de bilan - TRANSNORD SA', type: 'JUD', nom: 'TRANSNORD SA',
    }))
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranFactures.DemandeDe', 'J-00931'))
    expect(lignes(container)).toEqual([[
      'Rien a facturer : Depot de bilan, depot gratuit au greffe.',
    ]])
    sansFactureNiEncaissement(container)
    expect(valeur('N de demande')).toBe('')
    expect(valeur('Abrege')).toBe('')
    expect(valeur('Nom du client')).toBe('')
    sansRienDeLaDemo(container)
  })

  // Le jumeau de la gratuite : une radiation n'a pas d'article dans le tarif de l'ecran,
  // mais rien ne dit qu'elle est gratuite -- l'ecran le dit tel quel, sans facture a emettre.
  it('004179, une radiation absente du tarif : aucun article, sans pretendre le depot gratuit', async () => {
    const { container } = ouvrirPour(MartinEurl)
    expect(lignes(container)).toEqual([[
      'Aucun article du tarif pour cette affaire (Radiation).',
    ]])
    expect(container.textContent).not.toMatch(/gratuit/)
    sansFactureNiEncaissement(container)
    await waitFor(() => expect(valeur('N de demande')).toBe('004179'))
    sansRienDeLaDemo(container)
  })

  it('« Changer de client » propose ensuite les fiches connues, comme depuis l\'accueil', async () => {
    ouvrirPour(AmiensBillards)
    await waitFor(() => expect(valeur('Abrege')).toBe('AMIENS BILL.'))
    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect(valeur('Abrege')).toBe('LEROY')
    expect(screen.getByText(/Solde client/).textContent).toBe('Solde client 812,40 EUR')
  })

  it('depuis l\'accueil, le pont n\'est pas interroge sur une affaire', () => {
    const { pont } = ouvrirPour(null)
    expect(pont.appels.mock.calls.map(([nom]) => nom)).toEqual(['EcranFactures.DateDuJour'])
  })
})
