import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { DureeBascule, EcranCommandesWeb, type PanneauCw, type VueCommandesWeb } from './EcranCommandesWeb'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les regles (tuiles, filtre, groupes, rapprochement, delivrance, transitions)
// sont dans Metier/TraitementCommandes.cs, jouees par Pont/EcranCommandesWeb.cs
// et EXECUTEES par tests/CommandesWebCheck. Ici : l'ecran affiche la vue que
// la session lui rend, et lui remonte le bon geste.

function vueDeBase(): VueCommandesWeb {
  return {
    session: 'S',
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
    groupes: [{ rang: 1, titre: 'État d\'endettement', nombre: 1 }, { rang: 5, titre: 'Non-opposition', nombre: 1 }],
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
  const pont = installerFauxPont({
    'EcranCommandesWeb.Ouvrir': () => vue,
    'EcranCommandesWeb.Agir': (_s, geste, valeur) => {
      gestes.push([geste as string, valeur])
      vue = reagir(structuredClone(vue), geste as string, valeur)
      return vue
    },
    'EcranCommandesWeb.Fermer': () => undefined,
  })
  return { gestes, pont }
}

describe('EcranCommandesWeb', () => {
  it('la recherche en pilule ouvre l\'ecran, sans en-tete au-dessus (ni date, ni compte « à traiter »)', async () => {
    session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    // L'ordre : pilule, tuiles, puis liste et detail. L'en-tete date + compte a ete
    // retire (conv-2010) : il prenait de la place, les tuiles donnent ces chiffres.
    expect([...container.querySelector('.rig-tcw')!.children].map((e) => e.className.trim()))
      .toEqual(['rig-tcw__cherche', 'rig-tcw__tuiles', 'rig-tcw__scene'])
    expect(container.querySelector('.rig-proue')).toBeNull()
    expect(container.textContent).not.toContain('commandes à traiter')
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

    // Le compte de NeoRIG sur deux etages, le nombre en grand ; le libelle entier en infobulle.
    const compteur = container.querySelector('.rig-tcw__compteur')!
    expect(compteur.querySelector('.rig-tcw__compteur-nombre')!.textContent).toBe('2')
    expect(compteur.querySelector('.rig-tcw__compteur-detail')!.textContent).toBe('commande(s)2 groupe(s)')
    expect(compteur.getAttribute('title')).toBe('2 commande(s) · 2 groupe(s)')
    const lignes = [...container.querySelectorAll('.rig-tcw__scene > .rig-tcw__liste .rig-tcw__lignes .rig-tcw__cmd')]
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
    expect(container.querySelector('.rig-tcw__scene')!.className).toBe('rig-tcw__scene')
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
    // L'ouverture est une bascule animee (fondu sur place, conv-2021), qui retombe d'elle-meme.
    expect(container.querySelector('.rig-tcw__scene')!.className).toBe('rig-tcw__scene rig-tcw__scene--ouverte rig-tcw__scene--bascule')
    await waitFor(() => expect(container.querySelector('.rig-tcw__scene')!.className).toBe('rig-tcw__scene rig-tcw__scene--ouverte'))
    expect(container.querySelector('.rig-tcw__panneau')!.getAttribute('aria-label')).toBe('Faillite — SCI DU PARC')
    expect(container.querySelector('.rig-tcw__cmd--active')!.getAttribute('aria-current')).toBe('true')
    expect(container.querySelector('.rig-tcw__invite')).toBeNull()
  })

  it('fermer une commande la fait RESSORTIR : le meme panneau reste, inerte, le temps de la bascule, puis s\'en va', async () => {
    const { gestes } = session({ ...vueDeBase(), panneau: panneau() }, (v, geste) => {
      if (geste === 'FermerPanneau') v.panneau = null
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    const scene = () => container.querySelector('.rig-tcw__scene') as HTMLElement
    await screen.findByText('1 document à éditer')
    // La duree de la bascule est posee par l'ecran : la CSS la lit dans --tcw-duree.
    expect(scene().style.getPropertyValue('--tcw-duree')).toBe(`${DureeBascule}ms`)
    await waitFor(() => expect(scene().className).toBe('rig-tcw__scene rig-tcw__scene--ouverte'))
    const ouvert = container.querySelector('.rig-tcw__scene > .rig-tcw__panneau')!
    expect(ouvert.hasAttribute('inert')).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: 'Fermer' }))
    await waitFor(() => expect(scene().className).toBe('rig-tcw__scene rig-tcw__scene--bascule'))
    expect(gestes.map(([g]) => g)).toEqual(['FermerPanneau'])
    // Le MEME element (rien n'est remonte), inerte : ni clic ni focus, la session ne le connait plus.
    expect(container.querySelector('.rig-tcw__scene > .rig-tcw__panneau')).toBe(ouvert)
    expect(ouvert.hasAttribute('inert')).toBe(true)
    // La liste, elle, est deja celle d'une commande fermee : plus de poignee, l'invite revient.
    expect(container.querySelector('.rig-tcw__poignee')).toBeNull()
    expect(container.querySelector('.rig-tcw__invite')).not.toBeNull()

    await waitFor(() => expect(container.querySelector('.rig-tcw__panneau')).toBeNull())
    expect(scene().className).toBe('rig-tcw__scene')
  })

  it('commande ouverte : une poignee ENTRE la liste et le panneau regle la largeur de la liste, sans rien remonter a la session', async () => {
    const { gestes } = session({ ...vueDeBase(), panneau: panneau() })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('SCI DU PARC', { selector: '.rig-tcw__cmd-nom' })
    const scene = container.querySelector('.rig-tcw__scene') as HTMLElement
    expect([...scene.children].map((e) => e.className)).toEqual(['rig-tcw__liste', 'rig-tcw__poignee', 'rig-tcw__panneau'])
    // jsdom ne mesure rien : la scene fait 1200 px, la liste 300, la poignee 8.
    const mesure = (el: Element, width: number) => { el.getBoundingClientRect = () => ({ width }) as DOMRect }
    mesure(scene, 1200)
    mesure(container.querySelector('.rig-tcw__liste')!, 300)
    const poignee = screen.getByRole('separator', { name: 'Largeur de la liste des commandes' })
    mesure(poignee, 8)
    // Au repos : aucune largeur posee, la CSS garde ses 26 %.
    expect(scene.style.getPropertyValue('--tcw-liste')).toBe('')

    fireEvent.pointerDown(poignee, { clientX: 310, button: 0, pointerId: 1 })
    fireEvent.pointerMove(poignee, { clientX: 450 })
    fireEvent.pointerUp(poignee)
    expect(scene.style.getPropertyValue('--tcw-liste')).toBe('440px')
    // Jamais au point d'ecraser le panneau : 1200 - 8 - 420 = 772 px au plus.
    fireEvent.pointerDown(poignee, { clientX: 450, button: 0, pointerId: 1 })
    fireEvent.pointerMove(poignee, { clientX: 1500 })
    fireEvent.pointerUp(poignee)
    expect(scene.style.getPropertyValue('--tcw-liste')).toBe('772px')
    // Double-clic : la largeur d'origine revient.
    fireEvent.doubleClick(poignee)
    expect(scene.style.getPropertyValue('--tcw-liste')).toBe('')
    expect(gestes).toEqual([])
  })

  it('accordeon : un titre par groupe, dans l\'ordre de la session ; un clic le replie, et il resume ses noms', async () => {
    const { gestes } = session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    const groupes = () => [...container.querySelectorAll('.rig-tcw__lignes > .rig-tcw__groupe')]
    expect(groupes().map((g) => g.className.trim())).toEqual(['rig-tcw__groupe rig-tcw__rang-1', 'rig-tcw__groupe rig-tcw__rang-5'])
    expect(groupes().map((g) => [g.querySelector('.rig-tcw__groupe-titre')!.textContent, g.querySelector('.rig-tcw__groupe-nombre')!.textContent]))
      .toEqual([['État d\'endettement', '1'], ['Non-opposition', '1']])
    expect(groupes().map((g) => [...g.querySelectorAll('.rig-tcw__cmd-nom')].map((n) => n.textContent)))
      .toEqual([['TRANSNORD SA'], ['SCI DU PARC']])
    // Sans commande ouverte, tout est deplie, sans resume.
    const tete = screen.getByRole('button', { name: /^Non-opposition/ })
    expect(tete.getAttribute('aria-expanded')).toBe('true')
    expect(container.querySelector('.rig-tcw__groupe-resume')).toBeNull()

    fireEvent.click(tete)
    const parc = groupes()[1]!
    expect(parc.className).toBe('rig-tcw__groupe rig-tcw__rang-5 rig-tcw__groupe--plie')
    expect(tete.getAttribute('aria-expanded')).toBe('false')
    expect(parc.querySelector('.rig-tcw__groupe-resume')!.textContent).toBe('SCI DU PARC')
    // Les lignes refermees ne prennent ni clic ni Tab.
    expect(parc.querySelector('.rig-tcw__groupe-corps')!.hasAttribute('inert')).toBe(true)
    fireEvent.click(tete)
    expect(tete.getAttribute('aria-expanded')).toBe('true')
    // Replier est un reglage d'affichage : rien ne remonte a la session.
    expect(gestes).toEqual([])
  })

  it('accordeon : une commande qui s\'ouvre replie les AUTRES groupes ; le sien reste deplie, meme au clic', async () => {
    session(vueDeBase(), (v, geste) => {
      if (geste === 'Choisir') { v.panneau = panneau(); v.lignes[1]!.active = true }
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    fireEvent.click(await screen.findByText('SCI DU PARC'))
    await waitFor(() => expect(container.querySelector('.rig-tcw__panneau')).not.toBeNull())
    const endettement = screen.getByRole('button', { name: /^État d'endettement/ })
    const opposition = screen.getByRole('button', { name: /^Non-opposition/ })
    expect(endettement.getAttribute('aria-expanded')).toBe('false')
    expect(container.querySelector('.rig-tcw__rang-1 .rig-tcw__groupe-resume')!.textContent).toBe('TRANSNORD SA')
    expect(opposition.getAttribute('aria-expanded')).toBe('true')
    expect(opposition.className).toBe('rig-tcw__groupe-tete rig-tcw__groupe-tete--fixe')
    expect(opposition.getAttribute('aria-disabled')).toBe('true')
    fireEvent.click(opposition)
    expect(opposition.getAttribute('aria-expanded')).toBe('true')
    // Un autre groupe se deplie a la demande.
    fireEvent.click(endettement)
    expect(endettement.getAttribute('aria-expanded')).toBe('true')
  })

  it('sans commande ouverte, pas de poignee : la liste occupe toute la largeur', async () => {
    session(vueDeBase())
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    expect(container.querySelector('.rig-tcw__poignee')).toBeNull()
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('une tuile sans commande : la liste le dit', async () => {
    session({ ...vueDeBase(), groupes: [], lignes: [], compteur: '0 commande(s) · 0 groupe(s)' })
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

  it('non-opposition : sans controle, le cadre « Controle de delivrance » dit la raison du pont (en base, type pourtant coche)', async () => {
    // Lot 20 : il disait toujours « Cochez un type de certificat », meme un type coche, quand le pont (en base) ne joue pas le controle.
    const raison = 'Contrôle de délivrance non joué : il cherche l\'entreprise du SIREN de la commande.'
    session({ ...vueDeBase(), panneau: { ...panneau(), type: 'Non-opposition', certificats: null,
      nonOpposition: { sousTypes: [{ valeur: 'ReductionCapital', libelle: 'CNO-RC — Réduction de capital', inerte: false, coche: true }],
                       quantite: 1, quantiteActive: true, certificatDOpposition: false, controle: null, sansControle: raison },
      etapes: [
        { nom: 'Dossier concerné', faite: false, resume: '', manque: 'Sélectionnez le dossier concerné pour rattacher le certificat.' },
        { nom: 'Type de certificat de non-opposition', faite: false, resume: '', manque: raison },
        { nom: 'Édition', faite: true, resume: '2 document(s)', manque: null },
      ] } })
    render(<EcranCommandesWeb />)
    expect((await screen.findByText(raison)).className).toBe('rig-tcw__ligne-statut rig-tcw__ligne-statut--attente')
    expect(screen.queryByText('Cochez un type de certificat pour lancer le contrôle de délivrance.')).toBeNull()
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

  // « Correspondance » (/draft du 19/11/2026, proposition 5) : la commande et le
  // dossier trouve cote a cote, le verdict, « Retenir » ; la recherche repliee.
  it('debiteur : la commande et chaque dossier se comparent, la recherche se replie derriere un lien', async () => {
    const entite = (cle: string, designation: string, siren: string, ecarts: string[]) => ({
      cle, nonInscrit: false, gestion: cle, designation, siren, type: 'PM', radie: false, retenue: false,
      lignes: [{ nature: 'SIÈGE', adresse: '12 rue des Trois Cailloux, 80000 AMIENS', nbInscriptions: 14, distingue: false }], ecarts,
      nbAdresses: 2, nbInscriptions: 17, sirenConcorde: ecarts.length === 0, raisonConcorde: true, gestionConcorde: null, distinctions: [],
    })
    const rapprochement = {
      debiteur: true, faillite: false, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '402118447', rechRaison: 'AMIENS BILLARDS', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '2 ligne(s) · 2 PM · 0 NI',
      commandeSiren: '402 118 447', commandeRaison: 'AMIENS BILLARDS', commandeGestion: '—', rechercheOuverte: false,
      entites: [entite('2026B01067', 'AMIENS BILLARDS', '402 118 447', []),
                entite('004151', 'AMIENS BILLARDS', '334 771 018', ['SIREN reçu : 402 118 447 ≠ 334 771 018'])],
    }
    const { gestes } = session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } }, (v, geste) => {
      if (geste === 'Basculer') v.panneau!.rapprochement!.rechercheOuverte = !v.panneau!.rapprochement!.rechercheOuverte
      return v
    })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('Débiteur — rapprochement')

    // Une carte par dossier, l'ancien tableau a disparu ; le verdict suit les ecarts.
    expect(container.querySelector('.rig-tcw__rappro')).toBeNull()
    const cartes = [...container.querySelectorAll('.rig-tcw__corresp')]
    expect(cartes).toHaveLength(2)
    const [conforme, ecart] = cartes as [Element, Element]
    expect(conforme.querySelector('.rig-tcw__verdict')!.className).toContain('rig-tcw__ligne-statut--ok')
    expect(conforme.textContent).toContain('Aucun écart avec la commande')
    expect(conforme.querySelectorAll('.rig-tcw__comparaison .rig-tcw__coche')).toHaveLength(2)
    expect(conforme.textContent).toContain('17· PM · 2 adresse(s)')
    // Le n° de gestion ne se compare que pour le certificat de non-faillite : ici
    // il reste dans l'entete du dossier.
    expect(conforme.textContent).not.toContain('N° de gestion')
    expect(conforme.querySelector('.rig-tcw__comp-code')!.textContent).toBe('2026B01067')
    expect(ecart.querySelector('.rig-tcw__verdict')!.className).toContain('rig-tcw__ligne-statut--attente')
    expect(ecart.textContent).toContain('≠ SIREN reçu : 402 118 447 ≠ 334 771 018')
    expect(ecart.querySelector('.rig-tcw__difference')).not.toBeNull()

    // Recherche repliee : ni champ ni interrupteur tant que le lien n'est pas clique.
    expect(screen.queryByPlaceholderText('SIREN')).toBeNull()
    expect(screen.queryByText('Tous les débiteurs')).toBeNull()
    const lien = screen.getByText('Chercher un autre dossier')
    expect(lien.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(lien)
    await screen.findByPlaceholderText('SIREN')
    expect(screen.getByText('Masquer la recherche').getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(ecart.querySelector('.rig-tcw__verdict button')!)
    expect(gestes).toEqual([['Basculer', 'RechercheRapprochementOuverte'], ['Retenir', '004151']])
  })

  // Meme « Correspondance » pour le dossier concerne (non-opposition, faillite) :
  // son ancien tableau disparait, sa recherche se replie, sans les interrupteurs du debiteur.
  it('dossier concerne : meme Correspondance que le debiteur, recherche repliee sans interrupteurs', async () => {
    const rapprochement = {
      debiteur: false, faillite: true, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '512004331', rechRaison: 'SCI DU PARC', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '1 ligne(s) · 1 PM · 0 NI',
      commandeSiren: '512 004 331', commandeRaison: 'SCI DU PARC', commandeGestion: '004176', rechercheOuverte: false,
      entites: [{
        cle: '004176', nonInscrit: false, gestion: '004176', designation: 'SCI DU PARC', siren: '512 004 331', type: 'PM',
        radie: false, retenue: false,
        lignes: [{ nature: 'SIÈGE', adresse: '18 rue du Parc, 80480 DURY', nbInscriptions: 6, distingue: false }], ecarts: [],
        nbAdresses: 1, nbInscriptions: 6, sirenConcorde: true, raisonConcorde: true, gestionConcorde: true, distinctions: [],
      }],
    }
    const { gestes } = session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } }, (v, geste) => {
      if (geste === 'Basculer') v.panneau!.rapprochement!.rechercheOuverte = !v.panneau!.rapprochement!.rechercheOuverte
      return v
    })
    render(<EcranCommandesWeb />)
    const zone = (await screen.findByText('Dossier concerné', { selector: '.rig-tcw__section' })).closest('.rig-tcw__zone')!

    expect(zone.querySelector('table')).toBeNull()
    const cartes = [...zone.querySelectorAll('.rig-tcw__corresp')]
    expect(cartes).toHaveLength(1)
    // Certificat de non-faillite : le n° de gestion reçu face a celui du dossier, en
    // premiere ligne (c'est lui qui resout le dossier), puis SIREN et raison sociale.
    const champs = [...cartes[0]!.querySelectorAll('.rig-tcw__comparaison > .rig-tcw__comp-champ:not(.rig-tcw__comp-tete)')]
    expect(champs.map((c) => c.textContent)).toEqual(['N° de gestion', 'SIREN', 'Raison sociale', 'SIÈGE', 'Inscriptions'])
    // Cette ligne le montre deja : l'entete ne garde que la pastille.
    const tete = cartes[0]!.querySelectorAll('.rig-tcw__comp-tete')[2]!
    expect(tete.querySelector('.rig-tcw__comp-code')).toBeNull()
    expect(tete.textContent).toBe('DOSSIER RCS')
    const gestion = champs[0]!
    expect(gestion.nextElementSibling!.textContent).toBe('004176')
    expect(gestion.nextElementSibling!.nextElementSibling!.querySelector('.rig-tcw__coche')).not.toBeNull()
    expect(cartes[0]!.querySelectorAll('.rig-tcw__comparaison .rig-tcw__coche')).toHaveLength(3)
    expect(cartes[0]!.querySelector('.rig-tcw__verdict')!.textContent).toBe('Aucun écart avec la commandeRetenir')
    // Les textes de NeoRIG propres au dossier concerne restent : sa consigne en tete, l'invite au-dessus des cartes.
    expect(zone.textContent).toContain('Résolution directe par le n° de gestion reçu')
    expect(zone.querySelector('.rig-tcw__ligne-statut--info')!.textContent).toBe('Sélectionnez le dossier concerné pour rattacher le certificat.')
    expect(zone.querySelector('.rig-tcw__resume')!.textContent).toBe('1 ligne(s) · 1 PM · 0 NI')

    expect(screen.queryByPlaceholderText('SIREN')).toBeNull()
    fireEvent.click(screen.getByText('Chercher un autre dossier'))
    await screen.findByPlaceholderText('SIREN')
    expect(screen.queryByText('Tous les débiteurs')).toBeNull()

    fireEvent.click(zone.querySelector('.rig-tcw__corresp .rig-tcw__verdict button')!)
    expect(gestes).toEqual([['Basculer', 'RechercheRapprochementOuverte'], ['Retenir', '004176']])
  })

  it('certificat de non-faillite : un n° de gestion different porte « ≠ » et son ecart au verdict', async () => {
    const ecart = 'N° de gestion reçu : 004176 ≠ 2026B01067'
    const rapprochement = {
      debiteur: false, faillite: true, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '', rechRaison: 'AMIENS BILLARDS', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '1 ligne(s) · 1 PM · 0 NI',
      commandeSiren: '512 004 331', commandeRaison: 'SCI DU PARC', commandeGestion: '004176', rechercheOuverte: true,
      entites: [{
        cle: '2026B01067', nonInscrit: false, gestion: '2026B01067', designation: 'AMIENS BILLARDS', siren: '402 118 447',
        type: 'PM', radie: false, retenue: false,
        lignes: [{ nature: 'SIÈGE', adresse: '12 rue des Trois Cailloux, 80000 AMIENS', nbInscriptions: 14, distingue: false }], ecarts: [ecart],
        nbAdresses: 2, nbInscriptions: 17, sirenConcorde: false, raisonConcorde: false, gestionConcorde: false, distinctions: [],
      }],
    }
    session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('2026B01067', { selector: '.rig-tcw__comp-mono' })
    const gestion = [...container.querySelectorAll('.rig-tcw__comp-champ')].find((c) => c.textContent === 'N° de gestion')!
    const dossier = gestion.nextElementSibling!.nextElementSibling!
    expect(dossier.querySelector('.rig-tcw__difference')!.textContent).toBe('≠')
    expect(dossier.querySelector('.rig-tcw__coche')).toBeNull()
    expect(container.querySelector('.rig-tcw__verdict-ecarts')!.textContent).toBe(`≠ ${ecart}`)
  })

  it('plusieurs dossiers concordants : chaque carte met en avant ce qui la distingue des autres', async () => {
    // Certificat de non-faillite TRANSNORD SA : deux dossiers sans ecart avec la
    // commande, que seuls le n° de gestion et l'adresse departagent.
    const carte = (cle: string, nature: string, adresse: string, nbInscriptions: number) => ({
      cle, nonInscrit: false, gestion: cle, designation: 'TRANSNORD SA', siren: '334 771 018', type: 'PM',
      radie: false, retenue: false, lignes: [{ nature, adresse, nbInscriptions, distingue: true }], ecarts: [],
      nbAdresses: 1, nbInscriptions, sirenConcorde: true, raisonConcorde: true, gestionConcorde: null,
      distinctions: ['gestion', 'adresse'],
    })
    const rapprochement = {
      debiteur: false, faillite: true, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '334771018', rechRaison: 'TRANSNORD SA', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '2 ligne(s) · 2 PM · 0 NI',
      commandeSiren: '334 771 018', commandeRaison: 'TRANSNORD SA', commandeGestion: '—', rechercheOuverte: false,
      entites: [carte('004151', 'SIÈGE', 'ZI Nord, 80440 BOVES', 21),
                carte('004155', 'ÉTABLISSEMENT', 'Quai de Somme, 80100 ABBEVILLE', 2)],
    }
    session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('004155', { selector: '.rig-tcw__comp-mono > .rig-tcw__distingue' })
    const cartes = [...container.querySelectorAll('.rig-tcw__corresp')]
    expect(cartes).toHaveLength(2)

    for (const [i, c] of cartes.entries()) {
      // Ce qui departage est surligne (la valeur du dossier et son libelle) ; ce qui
      // est commun (SIREN, raison sociale) ne l'est pas.
      const surlignes = [...c.querySelectorAll('.rig-tcw__distingue')].map((s) => s.textContent)
      expect(surlignes).toEqual(i === 0
        ? ['N° de gestion', '004151', 'SIÈGE', 'ZI Nord, 80440 BOVES']
        : ['N° de gestion', '004155', 'ÉTABLISSEMENT', 'Quai de Somme, 80100 ABBEVILLE'])
      expect(c.querySelector('.rig-tcw__verdict')!.textContent)
        .toBe('Aucun écart avec la commandeSe distingue par : n° de gestion, adresseRetenir')
    }
  })

  it('un seul dossier, ou des dossiers en ecart : rien n\'est surligne', async () => {
    const rapprochement = {
      debiteur: true, faillite: false, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '', rechRaison: '', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '1 ligne(s) · 1 PM · 0 NI',
      commandeSiren: '402 118 447', commandeRaison: 'AMIENS BILLARDS', commandeGestion: '—', rechercheOuverte: false,
      entites: [{
        cle: '2026B01067', nonInscrit: false, gestion: '2026B01067', designation: 'AMIENS BILLARDS', siren: '402 118 447',
        type: 'PM', radie: false, retenue: false,
        lignes: [{ nature: 'SIÈGE', adresse: '12 rue des Trois Cailloux, 80000 AMIENS', nbInscriptions: 14, distingue: false }],
        ecarts: [], nbAdresses: 2, nbInscriptions: 17, sirenConcorde: true, raisonConcorde: true, gestionConcorde: null,
        distinctions: [],
      }],
    }
    session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('2026B01067', { selector: '.rig-tcw__comp-code' })
    expect(container.querySelector('.rig-tcw__distingue')).toBeNull()
    expect(container.querySelector('.rig-tcw__verdict')!.textContent).toBe('Aucun écart avec la commandeRetenir')
  })

  it('debiteur : le n° de gestion qui departage est surligne dans l\'entete de la carte', async () => {
    const carte = (cle: string) => ({
      cle, nonInscrit: false, gestion: cle, designation: 'TRANSNORD SA', siren: '334 771 018', type: 'PM',
      radie: false, retenue: false, lignes: [{ nature: 'SIÈGE', adresse: 'ZI Nord, 80440 BOVES', nbInscriptions: 21, distingue: false }],
      ecarts: [], nbAdresses: 1, nbInscriptions: 21, sirenConcorde: true, raisonConcorde: true, gestionConcorde: null,
      distinctions: ['gestion'],
    })
    const rapprochement = {
      debiteur: true, faillite: false, aRetenue: false, deplie: false, traitee: false, retenue: null,
      rechSiren: '', rechRaison: '', tousDebiteurs: false, tousEtablissements: false,
      message: null, resumeRapprochement: '2 ligne(s) · 2 PM · 0 NI',
      commandeSiren: '334 771 018', commandeRaison: 'TRANSNORD SA', commandeGestion: '—', rechercheOuverte: false,
      entites: [carte('004151'), carte('004155')],
    }
    session({ ...vueDeBase(), panneau: { ...panneau(), rapprochement } })
    const { container } = render(<EcranCommandesWeb />)
    await screen.findByText('004155', { selector: '.rig-tcw__comp-code' })
    const surlignes = [...container.querySelectorAll('.rig-tcw__distingue')].map((s) => s.textContent)
    expect(surlignes).toEqual(['004151', '004155'])
    expect(container.querySelector('.rig-tcw__comp-code.rig-tcw__distingue')).not.toBeNull()
    expect(container.querySelector('.rig-tcw__verdict')!.textContent)
      .toBe('Aucun écart avec la commandeSe distingue par : n° de gestionRetenir')
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
    const { pont } = session(vueDeBase())
    const { unmount } = render(<EcranCommandesWeb />)
    await screen.findByText('TRANSNORD SA')
    unmount()
    expect(pont.appels.mock.calls.map((c) => c[0])).toEqual(['EcranCommandesWeb.Ouvrir', 'EcranCommandesWeb.Fermer'])
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

// Ouvert depuis la page d'une affaire : des que la session repond, ses commandes
// sont cherchees par son SIREN (critere « SIREN » puis « Rechercher »), une
// seule fois (plan « Vue du dossier », lot 2).
describe('EcranCommandesWeb ouvert depuis une affaire', () => {
  it('pose son SIREN et lance la recherche, une fois', async () => {
    const { gestes } = session(vueDeBase())
    const { rerender } = render(<EcranCommandesWeb dossier={contexteDossier()} />)
    await waitFor(() => expect(gestes).toEqual([['Texte', { champ: 'SirenCherche', valeur: '812 445 901' }], ['Rechercher', null]]))
    rerender(<EcranCommandesWeb dossier={contexteDossier()} />)
    await new Promise((r) => setTimeout(r, 20))
    expect(gestes).toHaveLength(2)
  })

  it('sans affaire, ou une affaire sans SIREN : aucun geste a l\'ouverture', async () => {
    const { gestes } = session(vueDeBase())
    const sans = render(<EcranCommandesWeb dossier={null} />)
    await waitFor(() => expect(sans.container.querySelector('.rig-tcw__cherche')).not.toBeNull())
    sans.unmount()
    const vide = render(<EcranCommandesWeb dossier={contexteDossier({ siren: '' })} />)
    await waitFor(() => expect(vide.container.querySelector('.rig-tcw__cherche')).not.toBeNull())
    await new Promise((r) => setTimeout(r, 20))
    expect(gestes).toEqual([])
  })
})
