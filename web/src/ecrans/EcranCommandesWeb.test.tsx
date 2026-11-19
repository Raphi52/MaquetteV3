import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranCommandesWeb, type PanneauCw, type VueCommandesWeb } from './EcranCommandesWeb'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Les regles (onglets, filtre, groupes, rapprochement, delivrance, transitions)
// sont dans Metier/TraitementCommandes.cs, jouees par Pont/EcranCommandesWeb.cs
// et EXECUTEES par tests/CommandesWebCheck. Ici : l'ecran affiche la vue que
// la session lui rend, et lui remonte le bon geste.

function vueDeBase(): VueCommandesWeb {
  return {
    session: 'S',
    onglets: [
      { etat: 'ATraiter', libelle: 'À traiter', nombre: 6, actif: true },
      { etat: 'EnAttente', libelle: 'En attente', nombre: 2, actif: false },
    ],
    rechercheOuverte: false, rechercheAffinee: false, resumeCriteres: 'Tous les types',
    recherche: { famille: '', familles: [{ valeur: 'Faillite', libelle: 'Faillite' }], du: '', au: '', siren: '',
                 reference: '', enAttenteEnvoi: false, dateTravail: '18/11/2026' },
    message: null, compteur: '6 commandes', directsEnTete: false,
    lignes: [
      { id: 3, rang: 1, active: false, nom: 'TRANSNORD SA', precisions: 'IG-12345 · 12/11', type: 'État d\'endettement', direct: true },
      { id: 7, rang: 5, active: false, nom: 'SCI DU PARC', precisions: 'IG-12399 · 13/11', type: 'Faillite', direct: false },
    ],
    panneau: null, coordonnees: null,
  }
}

function panneau(): PanneauCw {
  return {
    id: 7, titre: 'Faillite — SCI DU PARC', rang: 5, type: 'Faillite', etat: null, nom: 'SCI DU PARC',
    reference: 'IG-12399 · reçue le 13/11/2026', enveloppe: null, saisie: null,
    donneesRecues: [{ libelle: 'Raison sociale', valeur: 'SCI DU PARC' }], traitee: null, motifAttente: null,
    rapprochement: null, types: null, nonOpposition: null, certificats: null,
    pied: { bande: null, barre: '1 document à éditer', documents: [{ document: 'Certificat', bac: 'Bac 1', exemplaires: '1', origine: 'Paramètre' }],
            envoiMail: false, adresseMail: '', attenteRefusee: false, blocage: 'Retenez un dossier avant de valider.' },
  }
}

function session(depart: VueCommandesWeb, reagir: (vue: VueCommandesWeb, geste: string, valeur: unknown) => VueCommandesWeb = (v) => v) {
  const gestes: Array<[string, unknown]> = []
  let vue = depart
  const dotnet = installerFauxDotNet({
    'EcranCommandesWeb.Ouvrir': () => vue,
    'EcranCommandesWeb.Agir': (_s, geste, valeur) => {
      gestes.push([geste as string, valeur])
      vue = reagir(structuredClone(vue), geste as string, valeur)
      return vue
    },
    'EcranCommandesWeb.Fermer': () => undefined,
  })
  return { gestes, dotnet }
}

describe('EcranCommandesWeb', () => {
  it('onglets d\'etat avec leur compte, liste a plat, invite tant qu\'aucune commande n\'est choisie', async () => {
    session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    const actif = await screen.findByRole('button', { name: 'À traiter6' })
    expect(actif.className).toBe('rig-tcw__onglet rig-tcw__onglet--actif')
    expect(actif.getAttribute('aria-pressed')).toBe('true')
    expect(actif.querySelector('b.rig-tcw__onglet-compte')!.textContent).toBe('6')
    expect(screen.getByText('Sélectionnez une commande dans la liste pour la traiter.')).toBeTruthy()
    const lignes = container.querySelectorAll('.rig-tcw__lignes > button')
    expect(lignes).toHaveLength(2)
    expect(lignes[0]!.className).toBe('rig-tcw__cmd rig-tcw__rang-1 ')
    expect(lignes[0]!.querySelector('.rig-tcw__direct')!.textContent).toBe('Rapprochement direct')
    expect(lignes[1]!.querySelector('.rig-tcw__direct')).toBeNull()
    expect(lignes[0]!.hasAttribute('aria-current')).toBe(false)
  })

  it('choisir un onglet puis une commande remonte l\'etat et l\'IDENTIFIANT ; le panneau s\'ouvre par-dessus la liste', async () => {
    const { gestes } = session(vueDeBase(), (v, geste) => {
      if (geste === 'Choisir') { v.panneau = panneau(); v.lignes[1]!.active = true }
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    fireEvent.click(await screen.findByRole('button', { name: 'En attente2' }))
    fireEvent.click(screen.getByText('SCI DU PARC'))
    await waitFor(() => expect(container.querySelector('.rig-tcw__scene > .rig-tcw__panneau')).not.toBeNull())
    expect(gestes).toEqual([['ChoisirOnglet', 'EnAttente'], ['Choisir', 7]])
    expect(container.querySelector('.rig-tcw__panneau')!.getAttribute('aria-label')).toBe('Faillite — SCI DU PARC')
    expect(container.querySelector('.rig-tcw__cmd--active')!.getAttribute('aria-current')).toBe('true')
    expect(screen.queryByText('Sélectionnez une commande dans la liste pour la traiter.')).toBeNull()
  })

  it('decisions en pied dans l\'ordre de la maquette ; Valider bloque porte son motif', async () => {
    session({ ...vueDeBase(), panneau: panneau() })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('1 document à éditer')
    const decisions = [...container.querySelectorAll('.rig-tcw__decisions button')]
    expect(decisions.map((b) => b.textContent)).toEqual(['Rejeter…', 'Mettre en attente…', 'Valider'])
    const valider = decisions[2] as HTMLButtonElement
    expect(valider.disabled).toBe(true)
    expect(valider.getAttribute('title')).toBe('Retenez un dossier avant de valider.')
    expect(container.querySelector('.rig-tcw__blocage')!.textContent).toBe('Retenez un dossier avant de valider.')
  })

  it('« Rejeter… » ouvre la bande ; le motif remonte a chaque frappe ; Confirmer suit le refus de la session', async () => {
    const { gestes } = session({ ...vueDeBase(), panneau: panneau() }, (v, geste, valeur) => {
      const pied = v.panneau!.pied!
      if (geste === 'OuvrirBande') pied.bande = { rejet: true, motifSaisi: '', confirmerRefuse: true, longueurMax: 250 }
      if (geste === 'Texte') pied.bande = { ...pied.bande!, motifSaisi: (valeur as { valeur: string }).valeur, confirmerRefuse: false }
      return v
    })
    render(<EcranCommandesWeb />)
    fireEvent.click(await screen.findByText('Rejeter…'))
    const motif = await screen.findByPlaceholderText('Saisir le motif…') as HTMLInputElement
    expect(motif.maxLength).toBe(250)
    const confirmer = screen.getByText('Confirmer le rejet') as HTMLButtonElement
    expect(confirmer.disabled).toBe(true)
    expect(confirmer.getAttribute('title')).toBe('Obligatoire pour un rejet')

    fireEvent.input(motif, { target: { value: 'Siren inconnu' } })
    await waitFor(() => expect((screen.getByText('Confirmer le rejet') as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(screen.getByText('Confirmer le rejet'))
    expect(gestes).toEqual([
      ['OuvrirBande', 'Rejeter'],
      ['Texte', { champ: 'MotifSaisi', valeur: 'Siren inconnu' }],
      ['Decider', 'Rejeter'],
    ])
  })

  it('la recherche s\'ouvre derriere la pastille ; une saisie remonte, Entree lance la recherche', async () => {
    const { gestes } = session(vueDeBase(), (v, geste) => {
      if (geste === 'Basculer') v.rechercheOuverte = !v.rechercheOuverte
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('À traiter')
    const pastille = container.querySelector('.rig-tcw__chercher')!
    expect(pastille.getAttribute('aria-expanded')).toBe('false')
    expect(pastille.getAttribute('title')).toBe('Tous les types')
    fireEvent.click(pastille)
    const siren = await screen.findByPlaceholderText('123456789')
    expect(pastille.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText('18/11/2026').className).toBe('rig-tcw__date-valeur')

    fireEvent.input(siren, { target: { value: '412' } })
    fireEvent.change(container.querySelector('.rig-tcw__criteres select')!, { target: { value: 'Faillite' } })
    fireEvent.keyDown(siren, { key: 'Enter' })
    expect(gestes).toEqual([
      ['Basculer', 'RechercheOuverte'],
      ['Texte', { champ: 'SirenCherche', valeur: '412' }],
      ['Famille', 'Faillite'],
      ['Rechercher', null],
    ])
  })

  it('un message se ferme par sa croix ; sa severite donne sa classe', async () => {
    const { gestes } = session({ ...vueDeBase(), message: { texte: 'Commande validée.', severite: 'succes' } },
      (v, geste) => { if (geste === 'FermerMessage') v.message = null; return v })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('Commande validée.')
    expect(container.querySelector('.rig-tcw__message')!.className).toBe('rig-tcw__message rig-tcw__message--succes')
    fireEvent.click(container.querySelector('.rig-tcw__message .rig-tcw__fermer')!)
    await waitFor(() => expect(screen.queryByText('Commande validée.')).toBeNull())
    expect(gestes).toEqual([['FermerMessage', null]])
  })

  it('coordonnees : un clic sur le voile ferme, un clic dans la boite ne ferme pas', async () => {
    const { gestes } = session({
      ...vueDeBase(), panneau: panneau(),
      coordonnees: { lectureSeule: false, champs: [{ cle: 'Nom', libelle: 'Nom', large: true, valeur: 'SCI DU PARC' }],
                     lignes: ['SCI DU PARC'], alertes: [], adresseCorrigee: false },
    })
    const { container } = render(<EcranCommandesWeb />)
    const nom = (await screen.findByRole('dialog', { name: 'Coordonnées du destinataire' })).querySelector('input')!
    fireEvent.click(nom)
    fireEvent.input(nom, { target: { value: 'SCI DU PARC 2' } })
    fireEvent.click(container.querySelector('.rig-voile')!)
    expect(gestes).toEqual([['Adresse', { cle: 'Nom', valeur: 'SCI DU PARC 2' }], ['FermerCoordonnees', null]])
  })

  it('appelle SON pont, et lui seul, et ferme la session au demontage', async () => {
    const { dotnet } = session(vueDeBase())
    const { unmount } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    unmount()
    expect(dotnet.invokeMethodAsync.mock.calls.map((c) => c[1])).toEqual(['EcranCommandesWeb.Ouvrir', 'EcranCommandesWeb.Fermer'])
  })
})
