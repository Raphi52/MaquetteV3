import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranCommandesWeb, type PanneauCw, type VueCommandesWeb } from './EcranCommandesWeb'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Les regles (tuiles, filtre, groupes, rapprochement, delivrance, transitions)
// sont dans Metier/TraitementCommandes.cs, jouees par Pont/EcranCommandesWeb.cs
// et EXECUTEES par tests/CommandesWebCheck. Ici : l'ecran affiche la vue que
// la session lui rend, et lui remonte le bon geste.

function vueDeBase(): VueCommandesWeb {
  return {
    session: 'S',
    proue: {
      date: 'jeudi 19 novembre',
      titre: { nombre: 6, texte: 'commandes à traiter', alerte: false },
      phrase: [{ nombre: 2, texte: 'groupes', alerte: false }, { nombre: 2, texte: 'en attente', alerte: false },
               { nombre: 1, texte: 'rejetée', alerte: true }],
    },
    tuiles: [
      { etat: 'ATraiter', libelle: 'À traiter', nombre: 6, actif: true, part: 67, detail: '2 groupe(s)' },
      { etat: 'EnAttente', libelle: 'Mise en attente', nombre: 2, actif: false, part: 22, detail: '1 groupe(s)' },
      { etat: 'Validee', libelle: 'Validée', nombre: 0, actif: false, part: 0, detail: '0 groupe(s)' },
      { etat: 'Rejetee', libelle: 'Rejetée', nombre: 1, actif: false, part: 11, detail: '1 groupe(s)' },
    ],
    rapide: '',
    rechercheOuverte: false, rechercheAffinee: false, resumeCriteres: 'Tous les types',
    recherche: { famille: '', familles: [{ valeur: 'Faillite', libelle: 'Faillite' }], du: '', au: '', siren: '',
                 reference: '', enAttenteEnvoi: false, dateTravail: '18/11/2026' },
    message: null, compteur: '2 commande(s) · 2 groupe(s)', directsEnTete: false,
    lignes: [
      { id: 3, rang: 1, active: false, nom: 'TRANSNORD SA', precisions: 'IG-12345 · 12/11', type: 'État d\'endettement', direct: true,
        avancement: '2/3', prete: false },
      { id: 7, rang: 5, active: false, nom: 'SCI DU PARC', precisions: 'IG-12399 · 13/11', type: 'Faillite', direct: false,
        avancement: '3/3', prete: true },
    ],
    panneau: null, coordonnees: null,
  }
}

function panneau(): PanneauCw {
  return {
    id: 7, titre: 'Faillite — SCI DU PARC', rang: 5, type: 'Faillite', etat: null, nom: 'SCI DU PARC',
    reference: 'IG-12399 · reçue le 13/11/2026', enveloppe: null, saisie: null,
    donneesRecues: [{ libelle: 'Raison sociale', valeur: 'SCI DU PARC' }], traitee: null, motifAttente: null,
    rapprochement: null, types: null, nonOpposition: null,
    certificats: { dateTravail: '18/11/2026', basculeFaillite: false, certificatDeFaillite: false,
                   lignes: [{ valeur: 'NonFaillite', libelle: 'Certificat de non-faillite', quantite: 1, actif: true, bloque: false, motif: null }] },
    pied: { bande: null, barre: '1 document à éditer', documents: [{ document: 'Certificat', bac: 'Bac 1', exemplaires: '1', origine: 'Paramètre' }],
            envoiMail: false, adresseMail: '', attenteRefusee: false, blocage: 'Retenez un dossier avant de valider.', avancement: '2 / 3' },
    etapes: [
      { nom: 'Dossier concerné', faite: false, resume: '', manque: 'Retenez un dossier avant de valider.' },
      { nom: 'Certificats', faite: true, resume: 'Certificat de non-faillite × 1', manque: null },
      { nom: 'Édition', faite: true, resume: '1 document à éditer', manque: null },
    ],
    choixDeplie: false,
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
  it('proue de l\'accueil : date, compte « à traiter » en grand, phrase des autres etats (rejets en alerte)', async () => {
    session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    const proue = container.querySelector('.rig-tcw__barre > .rig-proue')!
    expect(proue.querySelector('.rig-proue__date')!.textContent).toBe('jeudi 19 novembre·Affaires')
    expect(proue.querySelector('.rig-tcw__proue-titre')!.textContent).toBe('6 commandes à traiter')
    expect(proue.querySelector('.rig-proue__phrase')!.textContent).toBe('2 groupes·2 en attente·1 rejetée')
    const chiffres = [...proue.querySelectorAll('.rig-proue__phrase b')]
    expect(chiffres.map((b) => b.className)).toEqual(['', '', 'rig-proue__alerte'])
    // L'ordre de la maquette : proue, pilule, tuiles, puis liste et detail.
    expect([...container.querySelector('.rig-tcw')!.children].map((e) => e.className.trim()))
      .toEqual(['rig-tcw__barre', 'rig-tcw__cherche', 'rig-tcw__tuiles', 'rig-tcw__scene'])
  })

  it('une tuile de l\'accueil par statut (anneau rempli de sa part), puis la liste de la tuile active', async () => {
    session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    const tuiles = [...container.querySelectorAll('.rig-tcw__tuiles > .rig-chiffre')]
    expect(tuiles.map((t) => t.className)).toEqual([
      'rig-chiffre rig-chiffre--1 rig-tcw__tuile rig-chiffre--deplie', 'rig-chiffre rig-chiffre--4 rig-tcw__tuile ',
      'rig-chiffre rig-chiffre--2 rig-tcw__tuile ', 'rig-chiffre rig-chiffre--3 rig-tcw__tuile ',
    ])
    expect(tuiles.map((t) => t.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false', 'false'])
    expect(tuiles.map((t) => t.querySelector('.rig-chiffre__libelle')!.textContent)).toEqual(['À traiter', 'Mise en attente', 'Validée', 'Rejetée'])
    expect(tuiles[0]!.querySelector('.rig-chiffre__valeur')!.textContent).toBe('6')
    expect(tuiles[0]!.querySelector('.rig-chiffre__detail')!.textContent).toBe('2 groupe(s)')
    expect((tuiles[0]!.querySelector('.rig-chiffre__cercle--jauge') as HTMLElement).style.getPropertyValue('--rig-anneau-cible')).toBe('67')
    expect(tuiles.every((t) => t.querySelector('.rig-chiffre__rond svg') !== null)).toBe(true)

    expect(container.querySelector('.rig-tcw__compteur')!.textContent).toBe('2 commande(s) · 2 groupe(s)')
    const lignes = [...container.querySelectorAll('.rig-tcw__scene > .rig-tcw__liste .rig-tcw__lignes > .rig-tcw__cmd')]
    expect(lignes.map((b) => b.querySelector('.rig-tcw__cmd-nom')!.textContent)).toEqual(['TRANSNORD SA', 'SCI DU PARC'])
    const [transnord, parc] = lignes
    expect(transnord!.className).toBe('rig-tcw__cmd rig-tcw__rang-1 ')
    expect((transnord as HTMLElement).style.getPropertyValue('--tcw-ordre')).toBe('0')
    expect((parc as HTMLElement).style.getPropertyValue('--tcw-ordre')).toBe('1')
    expect(transnord!.querySelector('.rig-tcw__cmd-type')!.textContent).toBe('État d\'endettement')
    expect(transnord!.querySelector('.rig-tcw__cmd-precisions')!.textContent).toBe('IG-12345 · 12/11')
    expect(transnord!.querySelector('.rig-tcw__direct')!.textContent).toBe('Rapprochement direct')
    expect(parc!.querySelector('.rig-tcw__direct')).toBeNull()
    expect(transnord!.hasAttribute('aria-current')).toBe(false)
    // Escalier : chaque ligne dit ses marches faites ; une commande prete passe au vert.
    expect(transnord!.querySelector('.rig-tcw__avancement')!.className).toBe('rig-tcw__avancement ')
    expect(transnord!.querySelector('.rig-tcw__avancement')!.textContent).toBe('2/3')
    expect(parc!.querySelector('.rig-tcw__avancement')!.className).toBe('rig-tcw__avancement rig-tcw__avancement--prete')
    // Sans commande ouverte : la liste seule, et l'invite.
    expect(container.querySelector('.rig-tcw__scene')!.className).toBe('rig-tcw__scene ')
    expect(container.querySelector('.rig-tcw__liste > .rig-tcw__invite')!.textContent)
      .toBe('Sélectionnez une commande dans la liste pour la traiter.')
  })

  it('cliquer une tuile remonte son etat, une ligne son IDENTIFIANT ; la commande s\'ouvre a cote de la liste', async () => {
    const { gestes } = session(vueDeBase(), (v, geste) => {
      if (geste === 'Choisir') { v.panneau = panneau(); v.lignes[1]!.active = true }
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    fireEvent.click(await screen.findByRole('button', { name: /^Mise en attente/ }))
    fireEvent.click(screen.getByText('SCI DU PARC'))
    await waitFor(() => expect(container.querySelector('.rig-tcw__scene > .rig-tcw__panneau')).not.toBeNull())
    expect(gestes).toEqual([['ChoisirOnglet', 'EnAttente'], ['Choisir', 7]])
    expect(container.querySelector('.rig-tcw__scene')!.className).toBe('rig-tcw__scene rig-tcw__scene--ouverte')
    expect(container.querySelector('.rig-tcw__panneau')!.getAttribute('aria-label')).toBe('Faillite — SCI DU PARC')
    expect(container.querySelector('.rig-tcw__cmd--active')!.getAttribute('aria-current')).toBe('true')
    expect(container.querySelector('.rig-tcw__invite')).toBeNull()
  })

  it('une tuile sans commande : la liste le dit', async () => {
    session({ ...vueDeBase(), lignes: [], compteur: '0 commande(s) · 0 groupe(s)' })
    const { container } = render(<EcranCommandesWeb />)
    expect((await screen.findByText('Aucune commande ne correspond aux critères.')).className).toBe('rig-tcw__vide')
    expect(container.querySelector('.rig-tcw__invite')).toBeNull()
  })

  it('la pilule filtre a chaque frappe et montre le texte de la session ; Ctrl K y pose le curseur', async () => {
    const { gestes } = session({ ...vueDeBase(), rapide: 'tra' })
    const { container } = render(<EcranCommandesWeb />)
    const champ = await screen.findByPlaceholderText('Rechercher une commande, un SIREN, un n° IG-…') as HTMLInputElement
    expect(champ.closest('.rig-recherche')!.className).toBe('rig-recherche rig-recherche--fixe rig-tcw__pilule')
    expect(champ.value).toBe('tra')
    expect(container.querySelector('.rig-tcw__pilule .rig-touche')!.textContent).toBe('Ctrl K')

    fireEvent.input(champ, { target: { value: 'parc' } })
    expect(gestes).toEqual([['FiltrerRapide', 'parc']])

    expect(document.activeElement).not.toBe(champ)
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    expect(document.activeElement).toBe(champ)
    // Une autre touche avec Ctrl ne prend pas le curseur.
    champ.blur()
    fireEvent.keyDown(window, { key: 'j', ctrlKey: true })
    expect(document.activeElement).not.toBe(champ)
  })

  it('decisions en pied dans l\'ordre de la maquette ; Valider bloque porte son motif et compte les marches', async () => {
    session({ ...vueDeBase(), panneau: panneau() })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('1 document à éditer')
    const decisions = [...container.querySelectorAll('.rig-tcw__decisions button')]
    expect(decisions.map((b) => b.firstChild!.textContent)).toEqual(['Rejeter…', 'Mettre en attente…', 'Valider'])
    const valider = decisions[2] as HTMLButtonElement
    expect(valider.disabled).toBe(true)
    expect(valider.getAttribute('title')).toBe('Retenez un dossier avant de valider.')
    expect(valider.querySelector('.rig-tcw__valider-compte')!.textContent).toBe('2 / 3')
    expect(container.querySelector('.rig-tcw__blocage')!.textContent).toBe('Retenez un dossier avant de valider.')
    expect(container.querySelector('.rig-tcw__pied-ligne > .rig-tcw__voyant')!.className).toBe('rig-tcw__voyant rig-tcw__voyant--fin ')
  })

  it('escalier : trois marches dans l\'ordre, voyant par marche ; le choix fait se replie, « Modifier » le rouvre', async () => {
    const { gestes } = session({ ...vueDeBase(), panneau: panneau() }, (v, geste) => {
      if (geste === 'Basculer') v.panneau!.choixDeplie = !v.panneau!.choixDeplie
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('1 document à éditer')
    const marches = [...container.querySelectorAll('.rig-tcw__escalier > .rig-tcw__marche')]
    expect(marches.map((m) => m.className)).toEqual([
      'rig-tcw__marche rig-tcw__marche--a-faire', 'rig-tcw__marche rig-tcw__marche--faite', 'rig-tcw__marche rig-tcw__marche--faite',
    ])
    const voyants = marches.map((m) => m.querySelector(':scope > .rig-tcw__voyant')!)
    expect(voyants.map((v) => v.getAttribute('aria-label'))).toEqual(['Dossier concerné : à faire', 'Certificats : fait', 'Édition : fait'])
    expect(voyants[0]!.getAttribute('title')).toBe('Retenez un dossier avant de valider.')
    expect(voyants[0]!.querySelector('svg')).toBeNull()
    expect(voyants[1]!.querySelector('svg')).not.toBeNull()
    // L'edition est la troisieme marche (elle n'est plus dans le pied).
    expect(marches[2]!.querySelector('details.rig-tcw__edition > summary')!.textContent).toBe('1 document à éditer')
    expect(container.querySelector('.rig-tcw__pied .rig-tcw__edition')).toBeNull()

    // Le choix fait : une ligne (nom, resume), la zone des certificats n'est pas montree.
    const ligne = marches[1]!.querySelector('.rig-tcw__marche-ligne')!
    expect(ligne.querySelector('.rig-tcw__section')!.textContent).toBe('Certificats')
    expect(ligne.querySelector('.rig-tcw__marche-resume')!.textContent).toBe('Certificat de non-faillite × 1')
    expect(screen.queryByText('Travail 18/11/2026')).toBeNull()
    fireEvent.click(screen.getByText('Modifier'))
    await screen.findByText('Travail 18/11/2026')
    expect(container.querySelector('.rig-tcw__marche-ligne')).toBeNull()
    fireEvent.click(screen.getByText('Replier'))
    await waitFor(() => expect(container.querySelector('.rig-tcw__marche-ligne')).not.toBeNull())
    expect(gestes).toEqual([['Basculer', 'ChoixDeplie'], ['Basculer', 'ChoixDeplie']])
  })

  it('escalier : la marche du choix a faire montre sa zone ouverte, sans « Replier »', async () => {
    const p = panneau()
    p.etapes[1] = { nom: 'Certificats', faite: false, resume: '', manque: 'Aucun certificat n\'est délivrable : la commande doit être rejetée.' }
    session({ ...vueDeBase(), panneau: p })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('Travail 18/11/2026')
    expect(container.querySelector('.rig-tcw__marche-ligne')).toBeNull()
    expect(screen.queryByText('Replier')).toBeNull()
  })

  it('escalier : Valider ouvert — le bout du trait passe au vert, le compte disparait', async () => {
    const p = panneau()
    p.pied = { ...p.pied!, blocage: null, avancement: '' }
    p.etapes = p.etapes.map((e) => ({ ...e, faite: true, manque: null }))
    session({ ...vueDeBase(), panneau: p })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('1 document à éditer')
    const valider = container.querySelector('.rig-tcw__valider') as HTMLButtonElement
    expect(valider.disabled).toBe(false)
    expect(valider.querySelector('.rig-tcw__valider-compte')!.textContent).toBe('')
    expect(container.querySelector('.rig-tcw__pied-ligne > .rig-tcw__voyant')!.className)
      .toBe('rig-tcw__voyant rig-tcw__voyant--fin rig-tcw__voyant--pret')
    expect(container.querySelector('.rig-tcw__blocage')).toBeNull()
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
    await screen.findByText('TRANSNORD SA')
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

  it('enveloppe : un blanc avant et apres les membres, comme le .razor (« avec » texte, puis @foreach, puis « · »)', async () => {
    // Capture du 19/11/2026 (IG-90812) : le .razor rendait « — avec [membre] · 2 a
    // traiter… ». Razor ne retire que les blancs SEULS autour d'un @foreach ; ici
    // ils terminent « — avec » et ouvrent « · », il les gardait.
    const vue = vueDeBase()
    vue.panneau = { ...panneau(), enveloppe: {
      reference: 'IG-90812', etat: '2 à traiter avant l\'envoi.', envoiEnAttente: false,
      membres: [{ id: 6, titre: 'Certificat de non-faillite : À traiter', icone: 'a-traiter', libelle: 'Certificat de non-faillite' }],
    } }
    session(vue)
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('Enveloppe · IG-90812')
    expect(container.querySelector('.rig-tcw__enveloppe-texte')!.textContent)
      .toBe('Enveloppe · IG-90812 — avec Certificat de non-faillite · 2 à traiter avant l\'envoi.')
  })
})
