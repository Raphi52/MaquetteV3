import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { EcranRequisitions, type ArticlesRequi, type LigneCommandeWebRequi, type VueRequisitions } from './EcranRequisitions'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

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
    expect(await screen.findByText('Commandes web a traiter (2)')).toBeTruthy()
    expect(screen.getByText('Saisissez un critere puis lancez la recherche.')).toBeTruthy()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('recherche non lancee')
    const entetes = [...container.querySelectorAll('.rig-cmdweb thead th')].map((th) => th.textContent?.trim())
    expect(entetes).toEqual(['Origine', 'N commande', 'Recue le', 'Demandeur', 'Famille', 'EDI', 'Societe visee', 'Siren', 'Etat'])
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

    fireEvent.click(screen.getByText('Recue le'))
    await waitFor(() => expect(container.querySelector('.rig-cmdweb tbody tr td:nth-child(2)')!.textContent).toBe('IG-1'))
    expect(gestes).toEqual([['BasculerTri', 'Recue']])
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
    fireEvent.doubleClick(ligne)
    expect(gestes).toEqual([['AjouterAuPanier', 0]])
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
