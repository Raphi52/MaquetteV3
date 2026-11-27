import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranFactures } from './EcranFactures'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'
import type { ContexteDossierVue } from '../plan/types'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les montants attendus sont ceux de la capture de l'ecran Blazor (18/11/2026) :
// Total TTC 458,63 EUR, reste a encaisser 258,63 EUR pour 200 encaisses.
// Sans serveur, le pont rend null pour l'accueil (Pont/EcranFactures.cs, DemandeDeAsync) : la demonstration reste.
function ouvrir() {
  installerFauxPont({ 'EcranFactures.DateDuJour': () => '2026-09-09', 'EcranFactures.DemandeDe': () => null })
  return render(<EcranFactures />)
}

// La synthese de la tete de liste (socle des listes modernes, lot 4) : le compte et les totaux.
const compte = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent
const solde = (c: HTMLElement) => c.querySelector<HTMLElement>('section > .rig-criteres__bloc > .rig-etat')!
/** Les cellules de chaque ligne d'article affichee (hors lignes depliees) ; la cellule d'identite en « libelle · code ». */
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].map((tr) => [...tr.querySelectorAll('td')].map((td) =>
    [...td.querySelectorAll('.rig-lm-identite > *')].map((e) => e.textContent).join(' · ') || td.textContent))
/** Le code de chaque article affiche (en gris dans sa cellule) ; la ligne « Aucun article… » n'est pas un article. */
const codes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')].filter((tr) => !tr.querySelector('.rig-table__vide'))
    .map((tr) => tr.querySelector('.rig-lm-identite__objet')!.textContent)

describe('EcranFactures', () => {
  it('affiche le total et le reste comme l\'ecran Blazor', async () => {
    const { container } = ouvrir()
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
    expect(solde(container).textContent).toBe('Solde restant 258,63 EUR')
    expect(solde(container).className).toBe('rig-etat rig-etat--attente')
    // Dans la colonne flex du bloc d'encaissement, la pastille garde sa largeur (elle courait sur toute la carte).
    expect(solde(container).style.alignSelf).toBe('flex-start')
    expect(await screen.findByDisplayValue('2026-09-09')).toBeTruthy()
  })

  it('ecrit les colonnes de la grille a la francaise ; les secondaires sont dans la ligne depliee', () => {
    const { container } = ouvrir()
    // Chevron, Article (libelle et code reunis), Quantite, Etat, Montant TTC, Commentaire, menu.
    expect([...container.querySelectorAll('thead th')].map((th) => th.textContent))
      .toEqual(['', 'Article', 'Quantite', 'Etat', 'Montant TTC', 'Commentaire', ''])
    expect(lignes(container)[0]).toEqual(['', 'Immatriculation societe · IMM01', '1', 'Retenu', '194,85 €', '', ''])
    // Un article n'est ni un client ni un dossier : la cellule d'identite du socle SANS avatar (lot 8), libelle en
    // gras, code en gris, le tout entier au survol.
    expect(container.querySelector('tbody .rig-lm-avatar')).toBeNull()
    const article = container.querySelector('tbody tr')!.querySelectorAll('td')[1]!
    expect(article.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Immatriculation societe')
    expect(article.querySelector('.rig-lm-identite__objet')!.textContent).toBe('IMM01')
    expect(article.getAttribute('title')).toBe('Immatriculation societe · IMM01')
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

  // La regle du plan (« sans serveur : lignes d'exemple etiquetees ») : la facture de demonstration le dit, des l'ouverture.
  it('la facture de demonstration est marquee « exemple »', async () => {
    const { container } = ouvrir()
    const note = screen.getByRole('note')
    expect(note.querySelector('.rig-tcw__exemple')!.textContent).toBe('exemple')
    expect(note.textContent).toContain('Lignes de facture de démonstration')
    // Le pont a repondu « pas de serveur » (null) : la mention reste, la demonstration aussi.
    await waitFor(() => expect(screen.getByRole('note')).toBeTruthy())
    expect(compte(container)).toBe('5 articles · total TTC 458,63 € · reste a encaisser 258,63 €')
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
      ['', 'Immatriculation societe · IMM01', '1', 'Retenu', '194,85 €', '', ''],
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
      ['', 'Modification statutaire · MOD02', '1', 'Retenu', '162,72 €', '', ''],
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

  // Depuis l'accueil, le pont est interroge SANS affaire (null) : il dit seulement si l'ecran est sur la base.
  it('depuis l\'accueil, le pont n\'est pas interroge sur une affaire', async () => {
    const { pont } = ouvrirPour(null)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranFactures.DemandeDe', null))
    expect(pont.appels.mock.calls.filter(([nom]) => nom === 'EcranFactures.DemandeDe')).toEqual([['EcranFactures.DemandeDe', null]])
  })

  it('une facture d\'affaire tiree du tarif d\'exemple est marquee « exemple »', () => {
    ouvrirPour(AmiensBillards)
    expect(screen.getByRole('note').querySelector('.rig-tcw__exemple')).toBeTruthy()
  })
})

// La demande et le client d'une affaire lus dans la base (plan « RIG v3 100 % fonctionnel », lot 12, Pont/EcranFactures.cs) : la fiche
// du client de sa facture seule (aucune fiche de demonstration a cote) ; en panne, le motif et aucun client.
describe('EcranFactures : demande du dossier lue dans la base', () => {
  it('branche : numero de demande, client, adresse et solde du de la base ; Changer de client ne propose pas la demonstration', async () => {
    installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: 'D12FAC001', client: 'L12FAC', nom: 'CLIENT FACTURE BANC', adresse: '2 rue du Banc 80000 AMIENS',
                                          solde: 42.5, mode: 'serveur', message: null }),
    })
    render(<EcranFactures dossier={contexteDossier({ numero: 'L12DOS01', nom: 'CLIENT FACTURE BANC' })} />)
    await waitFor(() => expect(screen.getByDisplayValue('D12FAC001')).toBeTruthy())
    expect(screen.getByDisplayValue('2 rue du Banc 80000 AMIENS')).toBeTruthy()
    expect(screen.getByText('Solde client', { exact: false }).textContent).toContain('42,50')
    expect(screen.queryByText('SELARL LEROY & ASSOCIES')).toBeNull()
  })

  it('en panne, le motif, et ni demande ni client de demonstration', async () => {
    installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: '', client: '', mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }),
    })
    render(<EcranFactures dossier={contexteDossier({ numero: 'L12DOS01', nom: 'CLIENT FACTURE BANC' })} />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(screen.queryByText('SELARL LEROY & ASSOCIES')).toBeNull()
  })

  // Les articles de la facture lus dans v2.ARTICLE_UTILISE (LectureFactures.cs) : la grille les montre tels quels (montants de la
  // LIGNE, prix UHT d'une unite), aucun article du tarif d'exemple. Deux articles LIBRE : chacun se retire seul (cle = ARTUT).
  const ArticlesLus = [
    { id: 2201601, code: 'IMM01', libelle: 'Immatriculation lue', quantite: 1, prixUht: 37.45, tauxTva: 20, bodacc: 105.9, inpi: 0, autre: 0, ttc: 150.84, commentaire: '' },
    { id: 2201602, code: 'LIBRE', libelle: 'Article libre', quantite: 2, prixUht: 10, tauxTva: 20, bodacc: 0, inpi: 0, autre: 1.1, ttc: 25.1, commentaire: 'Copie' },
    { id: 2201603, code: 'LIBRE', libelle: 'Article libre', quantite: 1, prixUht: 5, tauxTva: 20, bodacc: 0, inpi: 0, autre: 0, ttc: 6, commentaire: 'Second' },
  ]

  it('branche : les articles de la facture viennent de la base, sans mention « exemple » ni tarif d\'exemple', async () => {
    installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: 'D12FAC001', client: 'L12FAC', nom: 'CLIENT FACTURE BANC', adresse: '', solde: 0, mode: 'serveur',
                                          message: null, facture: 'F12FAC01', articles: ArticlesLus }),
    })
    const { container } = render(<EcranFactures dossier={AmiensBillards} />)
    await waitFor(() => expect(compte(container)).toBe('3 articles · total TTC 181,94 € · reste a encaisser 181,94 €'))
    expect(screen.getByText('Articles de la facture F12FAC01')).toBeTruthy()
    expect(lignes(container)).toEqual([
      ['', 'Immatriculation lue · IMM01', '1', 'Retenu', '150,84 €', '', ''],
      ['', 'Article libre · LIBRE', '2', 'Retenu', '25,10 €', 'Copie', ''],
      ['', 'Article libre · LIBRE', '1', 'Retenu', '6,00 €', 'Second', ''],
    ])
    expect(container.textContent).not.toMatch(/194,85|Immatriculation societe|exemple|démonstration/)
    expect(screen.queryByRole('note')).toBeNull()
    fireEvent.click(screen.getByText('Second').closest('tr')!)
    expect(compte(container)).toBe('3 articles · 1 retire · total TTC 175,94 € · reste a encaisser 175,94 €')
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[1]!)
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent))
      .toEqual(['Prix UHT10,00', 'Taux TVA20,0 %', 'BODACC0,00', 'INPI0,00', 'Autre1,10'])
    sansRienDeLaDemo(container)
  })

  // Le jumeau de la demonstration de 004182 (IMM01 du tarif d'exemple) : sur la base, un dossier sans demande n'a rien a facturer.
  it('branche, dossier sans demande dans la base : aucun article, pas le tarif d\'exemple', async () => {
    installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: '', client: '', mode: 'serveur', message: null, articles: [] }),
    })
    const { container } = render(<EcranFactures dossier={AmiensBillards} />)
    await waitFor(() => expect(lignes(container)).toEqual([['Aucune demande pour ce dossier dans la base du greffe : rien a facturer.']]))
    sansFactureNiEncaissement(container)
    expect(screen.queryByRole('note')).toBeNull()
    sansRienDeLaDemo(container)
  })

  // Objection du juge (lot 12) : en panne, les articles du tarif d'exemple restaient affiches.
  it('en panne, aucun article du tarif d\'exemple ne reste', async () => {
    installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: '', client: '', mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', articles: [] }),
    })
    const { container } = render(<EcranFactures dossier={AmiensBillards} />)
    await screen.findByRole('alert')
    expect(lignes(container)).toEqual([['Aucun article : la base du greffe n\'a pas ete lue (motif ci-dessus).']])
    expect(container.textContent).not.toMatch(/IMM01|194,85/)
    sansFactureNiEncaissement(container)
  })

  // Objection du juge (lot 12) : depuis l'accueil, avec le serveur, la facture de demonstration s'affichait sans mention.
  it('depuis l\'accueil avec un serveur : la facture vide de RIG v2, rien de la demonstration', async () => {
    const pont = installerFauxPont({
      'EcranFactures.DateDuJour': () => '2026-11-27',
      'EcranFactures.DemandeDe': () => ({ numero: '', client: '', mode: 'serveur', message: null, articles: [] }),
    })
    const { container } = render(<EcranFactures />)
    await waitFor(() => expect(compte(container)).toBe('Aucun article'))
    expect(pont.appels).toHaveBeenCalledWith('EcranFactures.DemandeDe', null)
    expect(lignes(container)).toEqual([['Facture vide : ouverte hors d\'un dossier, elle ne reprend aucune demande (RIG v2 l\'ouvre vide).']])
    expect(valeur('N de demande')).toBe('')
    expect(valeur('Abrege')).toBe('')
    expect(screen.queryByRole('note')).toBeNull()
    sansFactureNiEncaissement(container)
    sansRienDeLaDemo(container)
  })
})
