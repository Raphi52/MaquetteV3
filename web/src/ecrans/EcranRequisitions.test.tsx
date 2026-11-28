import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { EcranRequisitions, type ArticlesRequi, type LigneCommandeWebRequi, type VueRequisitions } from './EcranRequisitions'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : tete, vues, chevron, pastilles, filtre Ctrl K, pied -- pas un correctif.

// Les regles (filtre, tri, panier, quantites d'inscription) sont dans
// Pont/EcranRequisitions.cs et EXECUTEES par tests/RequiCheck. Ici : l'ecran
// affiche la vue que la session lui rend, et lui remonte le bon geste.

const web = (rang: number, numero: string, recue: string, etat = 'A traiter'): LigneCommandeWebRequi => ({
  rang, classe: '', origine: 'Infogreffe', numero, recue, demandeur: 'ETUDE MARTIN', famille: 'Endettement',
  codeEdi: 'ET-CREQ', societe: 'TRANSNORD SA', siren: '412 345 678', etat,
})

function vueDeBase(): VueRequisitions {
  return {
    session: 'S', nbATraiter: 2, masquerTraitees: false, cleTri: null, triAscendant: true,
    commandesWeb: [web(0, 'IG-2', '12/11/2026 10:00'), web(1, 'IG-1', '10/11/2026 09:00')],
    commandeWebChoisie: false,
    criteres: { raisonSociale: '', siren: '', dirigeant: '', activite: '', numVoie: '', adresse: '', codePostal: '',
                ville: '', phonetique: false, etendre: false, tousEtb: false, supprCompris: false, guichet: false,
                limiteDossiers: 500 },
    raisonSocialeInvalide: false, volet: 'resultats', nbPanier: 0, compteur: 'recherche non lancee', message: '',
    trouvees: [], aRecherche: false, interrompue: false, limiteRetenue: 500, panier: [], articles: null,
  }
}

/** Une fausse session : chaque geste est note, la vue suivante vient de `reagir`. */
function session(reagir: (vue: VueRequisitions, geste: string, valeur: unknown) => VueRequisitions = (v) => v) {
  const gestes: Array<[string, unknown]> = []
  let vue = vueDeBase()
  const pont = installerFauxPont({
    'EcranRequisitions.Ouvrir': () => vue,
    'EcranRequisitions.Agir': (_s, geste, valeur) => {
      gestes.push([geste as string, valeur])
      vue = reagir(structuredClone(vue), geste as string, valeur)
      return vue
    },
    'EcranRequisitions.Fermer': () => undefined,
  })
  return { gestes, pont }
}

describe('EcranRequisitions', () => {
  it('rien de faux avant la reponse du pont, puis la zone haute et le compteur', async () => {
    let repondre: (v: VueRequisitions) => void = () => {}
    installerFauxPont({ 'EcranRequisitions.Ouvrir': () => new Promise<VueRequisitions>((r) => { repondre = r }) })
    const { container } = render(<EcranRequisitions />)
    expect(container.querySelector('.rig-proc__corps.rig-proc__corps--cmdweb')).not.toBeNull()
    expect(screen.queryByText('Saisissez un critere puis lancez la recherche.')).toBeNull()

    repondre(vueDeBase())
    expect(await screen.findByText('Commandes web a traiter')).toBeTruthy()
    expect(screen.getByText('Saisissez un critere puis lancez la recherche.')).toBeTruthy()
    expect(container.querySelector('.rig-cmdweb .rig-lm-synthese')!.textContent).toBe('2 a traiter · 2 affichee(s)')
    expect(container.querySelector('.rig-resultats-proc .rig-lm-synthese')!.textContent).toBe('recherche non lancee')
    // Neuf colonnes d'avant : origine, demandeur, EDI et SIREN sont dans la ligne depliee (et dans « Trier »).
    const entetes = [...container.querySelectorAll('.rig-cmdweb thead th')].map((th) => th.textContent?.trim())
    expect(entetes).toEqual(['', 'Societe visee', 'N commande', 'Recue le', 'Famille', 'Etat'])
    const tri = within(container.querySelector('.rig-cmdweb') as HTMLElement).getByLabelText('Trier') as HTMLSelectElement
    expect([...tri.options].map((o) => o.textContent))
      .toEqual(['Ordre d\'arrivee', 'Origine', 'N commande', 'Recue le', 'Demandeur', 'Famille', 'EDI', 'Societe visee', 'Siren', 'Etat'])
  })

  // Le REQUI de RIG v2 n'a pas de zone des commandes web (RCS\Exe\PROC_REQUI, branche 275) : branchee a la base, la session ne la
  // montre pas (zoneCommandesWeb: false), ni la note « exemple » de ses commandes de demonstration. L'ENTREE qui rend ce test rouge si
  // l'ecran etait faux : zoneCommandesWeb: false -- un ecran qui l'ignore afficherait la bande et sa note. Jumeau : le test d'avant.
  it('branchee a la base, pas de zone des commandes web ni de note « exemple » : les criteres et les resultats seuls', async () => {
    installerFauxPont({ 'EcranRequisitions.Ouvrir': () => ({ ...vueDeBase(), commandesWeb: [], nbATraiter: 0, zoneCommandesWeb: false }) })
    const { container } = render(<EcranRequisitions />)
    expect(await screen.findByText('Saisissez un critere puis lancez la recherche.')).toBeTruthy()
    expect(screen.queryByText('Commandes web a traiter')).toBeNull()
    expect(screen.queryByText(/Commandes web de démonstration/)).toBeNull()
    expect(container.querySelector('.rig-cmdweb')).toBeNull()
    expect(container.querySelector('.rig-proc__corps--cmdweb')).toBeNull()
    expect(container.querySelector('.rig-proc__corps .rig-criteres')).not.toBeNull()
  })

  it('un clic sur l\'en-tete « Recue le » remonte le tri sur la cle Recue, la session rend le nouvel ordre', async () => {
    const { gestes } = session((v, geste, cle) => {
      if (geste !== 'BasculerTri') return v
      v.cleTri = cle as string
      v.commandesWeb = [...v.commandesWeb].reverse()
      return v
    })
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('IG-2')

    // L'en-tete de colonne (« Recue le » est aussi une option de « Trier »).
    fireEvent.click(within(container.querySelector('.rig-cmdweb thead') as HTMLElement).getByText('Recue le'))
    await waitFor(() => expect(container.querySelector('.rig-cmdweb tbody tr td:nth-child(3)')!.textContent).toBe('IG-1'))
    expect(gestes).toEqual([['BasculerTri', 'Recue']])
  })

  it('« Trier » remonte la meme bascule que l\'en-tete, pour une colonne passee dans la ligne depliee ; le sens la rejoue', async () => {
    const { gestes } = session((v, geste, cle) => {
      if (geste === 'BasculerTri') v.cleTri = cle as string
      return v
    })
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('IG-2')
    const zone = within(container.querySelector('.rig-cmdweb') as HTMLElement)
    fireEvent.change(zone.getByLabelText('Trier'), { target: { value: 'Demandeur' } })
    await waitFor(() => expect((zone.getByLabelText('Trier') as HTMLSelectElement).value).toBe('Demandeur'))
    fireEvent.click(zone.getByRole('button', { name: 'Ordre croissant, inverser' }))
    expect(gestes).toEqual([['BasculerTri', 'Demandeur'], ['BasculerTri', 'Demandeur']])
  })

  it('les vues « Toutes » et « A traiter » sont la bascule « Masquer les traitees » ; « Marquer traitee » est dans la barre', async () => {
    const { gestes } = session((v, geste) => {
      if (geste === 'MasquerTraitees') v.masquerTraitees = !v.masquerTraitees
      if (geste === 'ChoisirCommandeWeb') v.commandeWebChoisie = true
      return v
    })
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('IG-2')
    const zone = within(container.querySelector('.rig-cmdweb') as HTMLElement)
    expect(zone.getByRole('button', { name: 'Toutes' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(zone.getByRole('button', { name: 'Toutes' }))
    expect(gestes).toEqual([])
    fireEvent.click(zone.getByRole('button', { name: 'A traiter 2' }))
    await waitFor(() => expect(zone.getByRole('button', { name: 'A traiter 2' }).getAttribute('aria-pressed')).toBe('true'))
    const marquer = zone.getByRole('button', { name: 'Marquer traitee' }) as HTMLButtonElement
    expect(marquer.disabled).toBe(true)
    fireEvent.click(screen.getByText('IG-1').closest('tr')!)
    await waitFor(() => expect(marquer.disabled).toBe(false))
    fireEvent.click(marquer)
    expect(gestes).toEqual([['MasquerTraitees', null], ['ChoisirCommandeWeb', 1], ['MarquerTraitee', null]])
  })

  it('la ligne depliee d\'une commande web montre origine, demandeur, EDI et SIREN, et rapproche par son rang', async () => {
    const { gestes } = session()
    const { container } = render(<EcranRequisitions />)
    const ligne = (await screen.findByText('IG-1')).closest('tr')!
    expect(ligne.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-orange')
    expect(ligne.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(ligne.querySelector('.rig-lm-identite')!.textContent).toBe('TTRANSNORD SA412 345 678')
    fireEvent.click(within(ligne).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = container.querySelector('.rig-cmdweb tr.rig-lm-detail')!
    expect(detail.textContent).toContain('OrigineInfogreffe')
    expect(detail.textContent).toContain('DemandeurETUDE MARTIN')
    expect(detail.textContent).toContain('EDIET-CREQ')
    expect(gestes).toEqual([])
    fireEvent.click(within(detail as HTMLElement).getByRole('button', { name: 'Rapprocher d\'un dossier' }))
    expect(gestes).toEqual([['RapprocherCommandeWeb', 1]])
  })

  it('choisir puis double-cliquer une commande web remonte son RANG (rapprochement)', async () => {
    const { gestes } = session()
    render(<EcranRequisitions />)
    const ligne = (await screen.findByText('IG-1')).closest('tr')!
    expect(ligne.getAttribute('title')).toBe('Double-cliquez pour rapprocher cette commande d un dossier.')
    // fireEvent.doubleClick n'envoie que « dblclick » (un vrai double-clic envoie aussi deux « click »).
    fireEvent.click(ligne)
    fireEvent.doubleClick(ligne)
    expect(gestes).toEqual([['ChoisirCommandeWeb', 1], ['RapprocherCommandeWeb', 1]])
  })

  it('une frappe dans un critere remonte a chaque touche ; Entree dans les criteres lance la recherche', async () => {
    const { gestes } = session()
    render(<EcranRequisitions />)
    const raison = await screen.findByPlaceholderText('nom de la societe')

    fireEvent.input(raison, { target: { value: 'TRANS' } })
    fireEvent.keyDown(raison, { key: 'Enter' })
    expect(gestes).toEqual([['Critere', { champ: 'CritRaisonSociale', valeur: 'TRANS' }], ['Rechercher', null]])
  })

  it('F7 lance la recherche et F5 efface les criteres, comme dans l\'ancien RIG (frmREQUI.cs:421-425) ; la page ne les garde pas', async () => {
    const { gestes } = session()
    render(<EcranRequisitions />)
    const raison = await screen.findByPlaceholderText('nom de la societe')

    // fireEvent rend faux quand la touche est marquee traitee (preventDefault) : le navigateur n'en fait rien.
    expect(fireEvent.keyDown(raison, { key: 'F7' })).toBe(false)
    expect(fireEvent.keyDown(raison, { key: 'F5' })).toBe(false)
    expect(gestes).toEqual([['Rechercher', null], ['Effacer', null]])
  })

  it('la limite de dossiers (un @bind d\'entier) ne remonte qu\'a la sortie du champ', async () => {
    const { gestes } = session()
    render(<EcranRequisitions />)
    const limite = (await screen.findByText('Limite max dossiers (50 a 500)')).parentElement!.querySelector('input')!
    expect(limite.value).toBe('500')
    fireEvent.input(limite, { target: { value: '120' } })
    expect(gestes).toEqual([])
    fireEvent.change(limite, { target: { value: '120' } })
    expect(gestes).toEqual([['LimiteDossiers', '120']])
  })

  it('les deux boutons de .rig-criteres__actions gardent le blanc qui les separait sous Razor', async () => {
    session()
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('Rechercher')
    const actions = container.querySelector('.rig-criteres .rig-criteres__actions')!
    expect(actions.textContent).toBe('Rechercher Reinitialiser')
  })

  it('le resultat double-clique part au panier par son rang', async () => {
    const gestes: Array<[string, unknown]> = []
    installerFauxPont({
      'EcranRequisitions.Ouvrir': () => ({
        ...vueDeBase(), aRecherche: true, compteur: '1 ligne(s) / 1 dossier',
        trouvees: [{ rang: 0, radie: true, infobulle: 'TRANSNORD SA\nDossier radie', id: '7', siren: '412 345 678',
                     gestion: '2001B00042', designation: 'TRANSNORD SA', typeAdresse: 'Siege', adresse: '1 rue du Port',
                     type: 'PM', nonInscrit: false, nbInscriptions: 3 }],
      }),
      'EcranRequisitions.Agir': (_s, geste, valeur) => { gestes.push([geste as string, valeur]); return vueDeBase() },
    })
    render(<EcranRequisitions />)
    const ligne = (await screen.findByText('2001B00042')).closest('tr')!
    expect(ligne.className).toBe('rig-table__ligne--inerte')
    expect(ligne.getAttribute('title')).toBe('TRANSNORD SA\nDossier radie')
    // La societe en avatar + nom, gestion et SIREN en gris ; le type en pastille, l'etat en point colore.
    expect(ligne.querySelector('.rig-lm-identite')!.textContent).toBe('TTRANSNORD SA2001B00042 · 412 345 678')
    expect(ligne.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect(ligne.querySelector('.rig-lm-etat')!.textContent).toBe('Radiee')
    expect(document.querySelector('.rig-resultats-proc .rig-lm-pied')!.textContent)
      .toBe('1 societe(s) affichee(s)Radiees 1Double-clic sur une ligne pour l\'ajouter au panier.')
    fireEvent.doubleClick(ligne)
    expect(gestes).toEqual([['AjouterAuPanier', 0]])
    // La ligne depliee : Id, gestion, adresse, et « Ajouter au panier », le geste du double-clic.
    fireEvent.click(within(ligne).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = document.querySelector('.rig-resultats-proc tr.rig-lm-detail')!
    expect(detail.textContent).toContain('Id7')
    expect(detail.textContent).toContain('Type AdresseSiege')
    fireEvent.click(within(detail as HTMLElement).getByRole('button', { name: 'Ajouter au panier' }))
    expect(gestes).toEqual([['AjouterAuPanier', 0], ['AjouterAuPanier', 0]])
  })

  it('« Aller a une societe… » (Ctrl K) filtre les resultats affiches, sans geste ; les puces changent de volet', async () => {
    const gestes: Array<[string, unknown]> = []
    const societe = (rang: number, designation: string, gestion: string) => ({
      rang, radie: false, infobulle: designation, id: String(rang), siren: '412 345 678', gestion, designation,
      typeAdresse: 'Siege', adresse: '1 rue du Port', type: 'PM', nonInscrit: false, nbInscriptions: 1,
    })
    installerFauxPont({
      'EcranRequisitions.Ouvrir': () => ({
        ...vueDeBase(), aRecherche: true, compteur: '2 ligne(s) / 2 dossiers',
        trouvees: [societe(0, 'TRANSNORD SA', '2001B00042'), societe(1, 'SCI DU PARC', '2019B00412')],
      }),
      'EcranRequisitions.Agir': (_s, geste, valeur) => { gestes.push([geste as string, valeur]); return vueDeBase() },
    })
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('2001B00042')
    const champ = screen.getByLabelText('Aller a une societe…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'parc' } })
    expect([...container.querySelectorAll('.rig-resultats-proc tbody tr')].map((tr) => tr.querySelector('.rig-lm-identite__nom')!.textContent))
      .toEqual(['SCI DU PARC'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 2 societes ne contient « zzz ».')).toBeTruthy()
    expect(gestes).toEqual([])
    const vues = within(container.querySelector('.rig-resultats-proc .rig-lm-vues') as HTMLElement)
    expect(vues.getByRole('button', { name: 'Resultats 2' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(vues.getByRole('button', { name: 'Panier 0' }))
    expect(gestes).toEqual([['Volet', 'panier']])
  })

  it('volet Articles : refus KBIS, travail perime, et la quantite d\'une inscription cochee', async () => {
    const articles: ArticlesRequi = {
      numCommande: 1, designation: 'TRANSNORD SA', kBisPossible: false, nbKBis: 0, nbHistorique: 0, nbDAS: 0,
      cnfPossible: true, nbCertifNonFaillite: 0, dateCnf: '2026-11-18', nbCertifNonRcs: 0, dateCnRcs: '2026-11-18',
      nbCertifNonRsac: 0, dateCnRsac: '2026-11-18', nbCertifNonOppo: 0, dateCnOppo: '2026-11-18', nbDjCopie: 0,
      nbDjExpedition: 0, nbNavire: 0, nbBeInterne: 0, nbBeExterne: 0, nomSci: '', nomRequi: '',
      dateTravail: '2026-09-01', travailFrais: false, ageTravailJours: 78, joursFraicheur: 30, toutCoche: false,
      cases: [{ libelle: 'NJ', titre: 'NJ', cochee: false, qte: 0 }, { libelle: 'Privilege', titre: 'Types d inscription : PRIV', cochee: true, qte: 2 }],
      nbInscr: 2,
    }
    const gestes: Array<[string, unknown]> = []
    installerFauxPont({
      'EcranRequisitions.Ouvrir': () => ({ ...vueDeBase(), volet: 'articles', compteur: 'commande 1', articles }),
      'EcranRequisitions.Agir': (_s, geste, valeur) => { gestes.push([geste as string, valeur]); return { ...vueDeBase(), volet: 'articles', articles } },
    })
    const { container } = render(<EcranRequisitions />)
    expect(await screen.findByText('Commande 1 - TRANSNORD SA')).toBeTruthy()
    expect(screen.getByText('KBIS interdit : cette ligne n est pas inscrite au registre.')).toBeTruthy()
    expect(screen.getByText('KBIS').parentElement!.querySelector('input')!.disabled).toBe(true)
    expect(screen.getByText('Travail perime : 78 jour(s), au-dela des 30 jours. Pieces a refaire.')).toBeTruthy()

    const qte = screen.getByText('Qte Privilege').parentElement!.querySelector('input')!
    expect(qte.value).toBe('2')
    fireEvent.change(qte, { target: { value: '5' } })
    fireEvent.click(within(container.querySelector('.rig-inscriptions') as HTMLElement).getByText('NJ'))
    fireEvent.change(screen.getByText('Date de travail').parentElement!.querySelector('input')!, { target: { value: '2026-11-17' } })
    expect(gestes).toEqual([
      ['ChangerQteInscription', { libelle: 'Privilege', saisie: '5' }],
      ['BasculerInscription', 'NJ'],
      ['Article', { champ: 'DateTravail', valeur: '2026-11-17' }],
    ])
    expect(screen.getByText('Privilege').closest('button')!.getAttribute('title')).toBe('Types d inscription : PRIV')
  })

  it('appelle SON pont, et lui seul, et ferme la session au demontage', async () => {
    const { pont } = session()
    const { unmount } = render(<EcranRequisitions />)
    await screen.findByText('IG-1')
    unmount()
    const appels = pont.appels.mock.calls.map((c) => c[0])
    expect(appels).toEqual(['EcranRequisitions.Ouvrir', 'EcranRequisitions.Fermer'])
    expect(pont.appels).toHaveBeenLastCalledWith('EcranRequisitions.Fermer', 'S')
  })
})

// Ouvert depuis la page d'une affaire : des que la session repond, son
// entreprise est cherchee par son SIREN -- les gestes de l'agent, une seule fois
// (plan « Vue du dossier », lot 2).
describe('EcranRequisitions ouvert depuis une affaire', () => {
  it('pose son SIREN et lance la recherche, une fois', async () => {
    const { gestes } = session()
    const { rerender } = render(<EcranRequisitions dossier={contexteDossier()} />)
    await waitFor(() => expect(gestes).toEqual([['Critere', { champ: 'CritSiren', valeur: '812 445 901' }], ['Rechercher', null]]))
    rerender(<EcranRequisitions dossier={contexteDossier()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Reinitialiser' }))
    await waitFor(() => expect(gestes.map(([g]) => g)).toEqual(['Critere', 'Rechercher', 'Effacer']))
  })

  it('sans affaire, ou une affaire sans SIREN : aucun geste a l\'ouverture', async () => {
    const { gestes } = session()
    const sans = render(<EcranRequisitions dossier={null} />)
    await screen.findByText('Commandes web a traiter')
    sans.unmount()
    render(<EcranRequisitions dossier={contexteDossier({ siren: '' })} />)
    await screen.findByText('Commandes web a traiter')
    fireEvent.click(screen.getByRole('button', { name: 'Rechercher' }))
    await waitFor(() => expect(gestes).toEqual([['Rechercher', null]]))
  })
})

// Une societe non inscrite n'a pas de gestion : son objet gris ne garde pas de « · » orphelin (capture du lot 6).
describe('EcranRequisitions, identite d\'une societe', () => {
  it('sans gestion, le SIREN seul ; avec, « gestion · SIREN »', async () => {
    const societe = (rang: number, gestion: string, siren: string) => ({
      rang, radie: false, infobulle: '', id: String(rang), siren, gestion, designation: `SOCIETE ${rang}`,
      typeAdresse: 'Siege', adresse: '1 rue du Port', type: 'PP', nonInscrit: gestion === '', nbInscriptions: 0,
    })
    installerFauxPont({
      'EcranRequisitions.Ouvrir': () => ({ ...vueDeBase(), aRecherche: true, compteur: '3 ligne(s)',
        trouvees: [societe(0, '', '412 345 678'), societe(1, '2001B00042', '412 345 679'), societe(2, '', '')] }),
    })
    const { container } = render(<EcranRequisitions />)
    await screen.findByText('2001B00042')
    expect([...container.querySelectorAll('.rig-resultats-proc .rig-lm-identite__objet')].map((o) => o.textContent))
      .toEqual(['412 345 678', '2001B00042 · 412 345 679', ''])
    expect(container.querySelector('.rig-resultats-proc tbody .rig-lm-etat')!.textContent).toBe('Non inscrite')
  })
})
