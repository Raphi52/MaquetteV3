import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import {
  EcranMessagerie, type ApercuModele, type BoitesDuGreffe, type ListeDiffusion, type ModeleMessage, type ResultatMessagerie, type SaisieListe,
  type SaisieModele, type VueEcranMessagerie, type VueModeleMessagerie,
} from './EcranMessagerie'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

// jsdom n'a pas les commandes d'edition du navigateur (document.execCommand) : un test de l'editeur les remplace par un espion.
afterEach(() => { cleanup(); retirerFauxPont(); delete (document as { execCommand?: unknown }).execCommand })

// Ce que le serveur rend est prouve par tests/SocleCourrielsCheck (CA05 : un agent du greffe ne recoit jamais un modele
// reserve a Amitel, ni par la liste ni par son nom ; GESTEMAIL exige pour ecrire ; regles de saisie de la V2). Ici : l'ecran
// montre ce que le pont lui donne, SEULEMENT a un agent d'Amitel la bascule « Amitel » (OPE_EDIT_EMAILMODEL.cs:333), et
// SEULEMENT a un agent GESTEMAIL ses boutons d'ecriture.
function modele(partiel: Partial<ModeleMessage> = {}): ModeleMessage {
  return {
    type: 'CA', libelleEcran: "Certificat d'audience", sujet: "Certificat d'audience", corpsHtml: false,
    expediteur: '[ADRESSE_MAIL_JUDICIAIRE]', reponse: '[ADRESSE_MAIL_NO_REPLY]', copie: null, destinataireDefaut: null,
    reserveAmitel: false, envoiAutomatique: false, utilisableEnSaisie: true, lot1: false, lot2: true,
    inscription: false, comptabilite: false, ...partiel,
  }
}

function liste(partiel: Partial<ListeDiffusion> = {}): ListeDiffusion {
  return { id: 7, code: 'MANDATAIRES', libelle: 'Mandataires du ressort', adresses: 'a@etude.example;b@etude.example',
           rcs: false, judiciaire: true, proprietaire: '*', actif: true, ...partiel }
}

const modelesDuGreffe = [
  modele(),
  modele({ type: 'REFUS_RCS', libelleEcran: 'Refus de formalité', sujet: 'Refus', lot1: true, lot2: false, envoiAutomatique: true }),
  modele({ type: 'RELANCE', libelleEcran: 'Relance de facture', sujet: 'Relance', lot2: false, comptabilite: true }),
  modele({ type: 'ANCIEN', libelleEcran: 'Ancien modèle', sujet: 'Ancien', utilisableEnSaisie: false }),
]

const listesDuGreffe = [liste(), liste({ id: 8, code: 'COMPTA_PERSO', libelle: 'Ma liste', proprietaire: 'banc.courriel', judiciaire: false }),
                        liste({ id: 9, code: 'VIEILLE', libelle: 'Liste inactive', actif: false })]

/** Les boites du greffe 3801 telles que le serveur les lit dans INFO_GREFFE (Contrats/Socle/Courriels.cs, BoitesDuGreffe). */
function boitesDuGreffe(partiel: Partial<BoitesDuGreffe> = {}): BoitesDuGreffe {
  return {
    boites: [
      { etiquette: 'ADRESSE_MAIL_RCS', adresse: 'rcs@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_JUDICIAIRE', adresse: 'judiciaire@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_NANTISSEMENTS', adresse: '' },
      { etiquette: 'ADRESSE_MAIL_COMPTABILITE', adresse: 'compta@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_NO_REPLY', adresse: 'ne-pas-repondre@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_ACCUSE_ENVOI_JUD', adresse: 'accuse-jud@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_ACCUSE_ENVOI_RCS', adresse: 'accuse-rcs@greffe-banc.example' },
      { etiquette: 'ADRESSE_MAIL_ACCUSE_ENVOI_NANT', adresse: '' },
      { etiquette: 'ADRESSE_MAIL_ACCUSE_ENVOI_CPT', adresse: '' },
    ],
    habillageRegle: false, habillagePresent: false, ...partiel,
  }
}

function vue(partiel: Partial<VueEcranMessagerie> = {}): VueEcranMessagerie {
  return {
    mode: 'serveur', message: null, greffe: '3801', voitReservesAmitel: false, modeles: modelesDuGreffe, listes: listesDuGreffe,
    peutModifier: false, estAmitel: false, compte: 'banc.courriel', dictionnaire: null, boites: boitesDuGreffe(), ...partiel,
  }
}

/** Un agent du greffe qui a la fonction GESTEMAIL. */
const gestionnaire = (partiel: Partial<VueEcranMessagerie> = {}) => vue({ peutModifier: true, ...partiel })

function apercu(type: string, partiel: Partial<ApercuModele> = {}): VueModeleMessagerie {
  const m = [...modelesDuGreffe, modele({ type: 'AMI_INTERNE', reserveAmitel: true })].find((x) => x.type === type)!
  const detail: ApercuModele = {
    modele: m, corps: 'Maître,|Le certificat.|$PieceJointe$',
    expediteur: 'judiciaire@greffe-banc.example', reponse: 'ne-pas-repondre@greffe-banc.example', copie: '', destinataireDefaut: '',
    rendu: '<html><body><h1>Tribunal de commerce de GRENOBLE</h1>Maître,<br/>Le certificat.</body></html>', habille: true,
    valeursManquantes: [], listesParDefaut: [liste()], signatureActive: false, signature: null, ...partiel,
  }
  return { mode: 'serveur', message: null, apercu: detail }
}

const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value
const saisir = (libelle: string, texte: string) => fireEvent.change(screen.getByLabelText(libelle), { target: { value: texte } })
const bouton = (nom: string) => screen.getByRole('button', { name: nom })
const resultats = () => within(screen.getByRole('list', { name: 'Modèles' })).getAllByRole('button').map((b) => b.textContent)
const BoutonsDEcriture = ['Nouveau modèle', 'Enregistrer', 'Créer le modèle', 'Annuler', 'Désactiver', 'Réactiver', 'Nouvelle liste']
/** La question de la V2 avant Désactiver / Activer (DialogBox WarningYesNo), et ses deux reponses. */
const question = () => screen.getByRole('alertdialog')
const repondre = (r: 'Oui' | 'Non') => fireEvent.click(within(question()).getByRole('button', { name: r }))
/** Le choix d'une boite du greffe : son nom accessible est son ADRESSE et son nom court (l'etiquette est en infobulle). */
const choix = (nom: string) => screen.getByRole('radio', { name: nom })
const coche = (e: HTMLElement) => e.getAttribute('aria-checked') ?? e.getAttribute('aria-pressed')
const dansGroupe = (groupe: string) => within(screen.getByRole('group', { name: groupe })).getAllByRole('button')
const boiteDe = (groupe: string, nom: string) => within(screen.getByRole('group', { name: groupe })).getByRole('button', { name: nom })
const saisieEnvoyee = (pont: ReturnType<typeof installerFauxPont>) =>
  pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')![1] as SaisieModele

describe('EcranMessagerie', () => {
  it('SOC-006 CA05 : un agent du greffe voit les modeles de SON greffe, lus par le pont, sans la bascule « Amitel »', async () => {
    const pont = installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    expect(await screen.findByText('3 modèle(s) — greffe 3801')).toBeTruthy()
    // Les elements inactifs (EMAILMOD_UTILISABLE_EN_SAISIE = 0) sont caches, comme la V2 (OPE_RECHERCHE_EMAILMODEL.cs:139).
    expect(resultats()).toEqual(["CA — Certificat d'audience", 'REFUS_RCS — Refus de formalité', 'RELANCE — Relance de facture'])
    for (const d of ['RCS', 'Judiciaire', 'Privilège', 'Comptabilité']) expect(screen.getByRole('button', { name: d })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Amitel' })).toBeNull()
    expect(screen.queryByText(/Démonstration/)).toBeNull()
    // Le premier modele s'ouvre, son apercu vient du serveur (habillage et valeurs du greffe).
    expect(await screen.findByTitle('Aperçu du courriel')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.Modele', 'CA')
    expect((screen.getByTitle('Aperçu du courriel') as HTMLIFrameElement).getAttribute('srcdoc')).toContain('Tribunal de commerce de GRENOBLE')
    expect((screen.getByTitle('Aperçu du courriel') as HTMLIFrameElement).getAttribute('sandbox')).toBe('')
    expect(valeur('Nom du modèle')).toBe('CA')
    expect(valeur('Expéditeur')).toBe('judiciaire@greffe-banc.example')
    expect(valeur('Répondre à')).toBe('ne-pas-repondre@greffe-banc.example')
    expect((screen.getByLabelText('Texte du modèle') as HTMLTextAreaElement).value).toBe('Maître,\nLe certificat.\n$PieceJointe$')
    expect(screen.getByLabelText('Listes par défaut').textContent).toContain('MANDATAIRES')
  })

  it('SOC-006 CA05 (jumeau) : un agent d\'Amitel a la bascule « Amitel » et voit les modeles qui lui sont reserves', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => vue({ voitReservesAmitel: true, modeles: [...modelesDuGreffe, modele({ type: 'AMI_INTERNE', reserveAmitel: true })] }),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
    })
    render(<EcranMessagerie />)
    expect(await screen.findByText('4 modèle(s) — greffe 3801')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Amitel' }))
    expect(resultats()).toEqual(['AMI_INTERNE — Certificat d\'audience'])
  })

  it('filtre par nom et par domaine (RCS = EMAILMOD_LOT_1, Comptabilité = EMAILMOD_COMPTA) ; « Afficher les éléments inactifs »', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByText('3 modèle(s) — greffe 3801')
    fireEvent.click(screen.getByRole('button', { name: 'RCS' }))
    expect(resultats()).toEqual(['REFUS_RCS — Refus de formalité'])
    fireEvent.click(screen.getByRole('button', { name: 'RCS' }))
    fireEvent.click(screen.getByRole('button', { name: 'Comptabilité' }))
    expect(resultats()).toEqual(['RELANCE — Relance de facture'])
    fireEvent.click(screen.getByRole('button', { name: 'Comptabilité' }))
    fireEvent.change(screen.getByLabelText('Nom ou libellé'), { target: { value: 'refus' } })
    expect(resultats()).toEqual(['REFUS_RCS — Refus de formalité'])
    fireEvent.change(screen.getByLabelText('Nom ou libellé'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Afficher les éléments inactifs' }))
    expect(resultats()).toHaveLength(4)
    expect(resultats()).toContain('ANCIEN — Ancien modèle (désactivé)')
    expect(screen.getByRole('button', { name: 'Masquer les éléments inactifs' })).toBeTruthy()
  })

  it('choisir un modele charge son apercu ; l\'envoi automatique ne se montre que pour un modele RCS (V2 _SetVisibilityChkEnvoiAuto)', async () => {
    const pont = installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(screen.queryByText(/Mail envoyé automatiquement/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'REFUS_RCS — Refus de formalité' }))
    expect(await screen.findByText('Mail envoyé automatiquement (sans affichage de l\'écran de mail)')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
  })

  it('les listes de distribution du greffe : actives par defaut, propriétaire en clair', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    const { container } = render(<EcranMessagerie />)
    await screen.findByText('3 modèle(s) — greffe 3801')
    fireEvent.click(screen.getByRole('button', { name: 'Listes de distribution' }))
    // Une ligne : libelle (avatar) et code, domaines en pastilles, proprietaire en clair, etat en point colore, adresses.
    const lignes = () => [...container.querySelectorAll('tbody tr')].map((tr) => [
      tr.querySelector('.rig-lm-identite__nom')!.textContent, tr.querySelector('.rig-lm-identite__objet')!.textContent,
      ...[...tr.querySelectorAll('td')].slice(1).map((td) => td.textContent)])
    expect(lignes()).toEqual([
      ['Mandataires du ressort', 'MANDATAIRES', 'Judiciaire', 'Tous les agents', 'Active', 'a@etude.example ; b@etude.example'],
      ['Ma liste', 'COMPTA_PERSO', '—', 'banc.courriel', 'Active', 'a@etude.example ; b@etude.example'],
    ])
    expect(container.querySelector('tbody tr .rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-ardoise')
    expect(container.querySelector('tbody tr .rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('2 listes · 1 publique')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('2 listes affichées')
    fireEvent.click(screen.getByRole('button', { name: 'Afficher les éléments inactifs' }))
    expect(lignes()).toHaveLength(3)
    expect(lignes()[2]!.slice(0, 5)).toEqual(['Liste inactive', 'VIEILLE', 'Judiciaire', 'Tous les agents', 'Désactivée'])
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('3 listes · 2 publiques · 1 désactivée')
  })

  it('les listes de distribution : les vues sont le critere « éléments inactifs » ; « Aller à une liste… » filtre la liste affichee', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    const { container } = render(<EcranMessagerie />)
    await screen.findByText('3 modèle(s) — greffe 3801')
    fireEvent.click(screen.getByRole('button', { name: 'Listes de distribution' }))
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent)).toEqual(['Actives 2', 'Toutes 3'])
    expect(screen.getByRole('button', { name: 'Actives 2' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Toutes 3' }))
    // La puce pose le critere du volet de gauche, qui vaut aussi pour les modeles.
    expect(screen.getByRole('button', { name: 'Masquer les éléments inactifs' })).toBeTruthy()
    expect(container.querySelectorAll('tbody tr')).toHaveLength(3)
    expect(screen.getByRole('button', { name: 'Toutes 3' }).getAttribute('aria-pressed')).toBe('true')

    const champ = screen.getByLabelText('Aller à une liste…')
    fireEvent.change(champ, { target: { value: 'compta' } })
    expect([...container.querySelectorAll('tbody tr .rig-lm-identite__objet')].map((o) => o.textContent)).toEqual(['COMPTA_PERSO'])
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('1 liste · 0 publique')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 3 listes ne contient « zzz ».')).toBeTruthy()
  })

  it('sans serveur : la demonstration, avec son bandeau, et sans bascule « Amitel »', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => vue({ mode: 'demo', greffe: null }),
      'EcranMessagerie.Modele': (t) => ({ ...apercu(t as string), mode: 'demo' }),
    })
    render(<EcranMessagerie />)
    expect(await screen.findByText(/Sans serveur, des modèles d'exemple/)).toBeTruthy()
    expect(screen.getAllByText('Démonstration').length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Amitel' })).toBeNull()
  })

  it('un refus ou une panne du serveur : une page d\'alerte, aucun modele', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue({ mode: 'erreur', modeles: [], listes: [], message: 'La base du greffe 3801 ne répond pas.' }) })
    render(<EcranMessagerie />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(screen.queryByRole('list', { name: 'Modèles' })).toBeNull()
  })

  it('un modele que le serveur ne rend pas (inconnu ou reserve a Amitel) : la raison, pas d\'apercu', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => vue(),
      'EcranMessagerie.Modele': () => ({ mode: 'erreur', message: 'Le modèle « CA » n\'est pas dans le greffe 3801.', apercu: null }),
    })
    render(<EcranMessagerie />)
    expect((await screen.findByRole('alert')).textContent).toContain('n\'est pas dans le greffe 3801')
    expect(screen.queryByTitle('Aperçu du courriel')).toBeNull()
  })

  // ------------------------------------------------------------------------------------------------------------------
  // L'ecriture (lot 15) : GESTEMAIL, Nouveau / Enregistrer / Annuler / Désactiver, motif du serveur, apercu recalcule.
  // ------------------------------------------------------------------------------------------------------------------

  it('sans GESTEMAIL : la lecture seule, et AUCUN bouton d\'ecriture, ni sur les modeles ni sur les listes', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => vue(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    const { container } = render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(screen.getByText('Lecture seule')).toBeTruthy()
    for (const nom of BoutonsDEcriture) expect(screen.queryByRole('button', { name: nom })).toBeNull()
    // Chaque champ de la fiche est en lecture (le volet de gauche garde sa recherche).
    const champs = [...container.querySelector('section.rig-resultats-proc')!.querySelectorAll('input, textarea')] as HTMLInputElement[]
    expect(champs.length).toBeGreaterThan(5)
    expect(champs.every((c) => c.readOnly)).toBe(true)
    fireEvent.click(bouton('Listes de distribution'))
    fireEvent.click(screen.getByText('Mandataires du ressort'))
    for (const nom of BoutonsDEcriture) expect(screen.queryByRole('button', { name: nom })).toBeNull()
    expect(screen.queryByLabelText('Liste de distribution')).toBeNull()
  })

  it('GESTEMAIL : modifier un modele -- la saisie part au pont (« | » pour les sauts de ligne), le message du serveur s\'affiche, l\'apercu se recalcule', async () => {
    const recalcule = apercu('CA', { corps: 'Maître,|Le certificat corrigé.|$PieceJointe$', rendu: '<html><body>RENDU RECALCULE</body></html>' })
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: true, message: 'Modèle « CA » enregistré.', vue: gestionnaire(), fiche: recalcule }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(screen.queryByText('Lecture seule')).toBeNull()
    // En modification, la boite du greffe se choisit par son ADRESSE (lot 16), la base garde l'etiquette ; le nom ne change plus.
    // fix-ok: lot 16 -- l'expediteur n'est plus une zone ou l'on tape l'etiquette (ce test lisait '[ADRESSE_MAIL_JUDICIAIRE]').
    expect(coche(choix('judiciaire@greffe-banc.example (judiciaire)'))).toBe('true')
    expect((screen.getByLabelText('Nom du modèle') as HTMLInputElement).readOnly).toBe(true)
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)
    saisir('Texte du modèle', 'Maître,\nLe certificat corrigé.\n$PieceJointe$')
    saisir('Sujet', 'Certificat (corrigé)')
    fireEvent.click(bouton('Utilisable comptabilité'))
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « CA » enregistré.')
    const [nom, saisie, type] = pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')! as [string, SaisieModele, string]
    expect(nom).toBe('EcranMessagerie.EnregistrerModele')
    expect(type).toBe('CA')
    expect(saisie).toMatchObject({ type: 'CA', sujet: 'Certificat (corrigé)', corps: 'Maître,|Le certificat corrigé.|$PieceJointe$',
                                   expediteur: '[ADRESSE_MAIL_JUDICIAIRE]', comptabilite: true, lot2: true, listesParDefaut: [7] })
    expect((screen.getByTitle('Aperçu du courriel') as HTMLIFrameElement).getAttribute('srcdoc')).toContain('RENDU RECALCULE')
    expect((screen.getByLabelText('Texte du modèle') as HTMLTextAreaElement).value).toBe('Maître,\nLe certificat corrigé.\n$PieceJointe$')
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)
  })

  it('« requis » marque les champs que le serveur exige, ceux de la V2 (MetierObligatoire, OPE_EDIT_EMAILMODEL.Designer.cs:103, :115, :172, :191)', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    const requis = [...document.querySelectorAll('.rig-saisie-champ__requis')]
      .map((m) => (m.parentElement?.textContent ?? '').replace(/requis$/, ''))
    expect(requis).toEqual(['Nom du modèle', 'Libellé écran', 'Sujet', 'Expéditeur', 'Texte du modèle'])
  })

  it('un refus du serveur : son motif s\'affiche tel quel, la saisie reste a l\'ecran', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({
        ok: false, message: 'Expéditeur : « pas une adresse » n\'est ni une adresse de courriel ni une boîte du greffe ([ADRESSE_MAIL_…]).', vue: null, fiche: null,
      }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    fireEvent.click(choix('Saisie libre'))
    saisir('Expéditeur du courrier (adresse du mail)', 'pas une adresse')
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('alert')).textContent).toContain('« pas une adresse » n\'est ni une adresse de courriel')
    expect(valeur('Expéditeur du courrier (adresse du mail)')).toBe('pas une adresse')
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(false)
  })

  it('Nouveau : un formulaire vide, le nom se saisit, « Créer le modèle » envoie la saisie sans nom d\'origine ; Annuler le referme', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (s): ResultatMessagerie => ({
        ok: true, message: `Modèle « ${(s as SaisieModele).type} » créé.`,
        vue: gestionnaire({ modeles: [...modelesDuGreffe, modele({ type: 'NOUVEAU', libelleEcran: 'Nouveau courrier' })] }),
        fiche: { mode: 'serveur', message: null, apercu: { ...apercu('CA').apercu!, modele: modele({ type: 'NOUVEAU', libelleEcran: 'Nouveau courrier' }) } },
      }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    fireEvent.click(bouton('Nouveau modèle'))
    expect(screen.getByText('Ajout d\'un modèle de mail')).toBeTruthy()
    expect(valeur('Nom du modèle')).toBe('')
    expect((screen.getByLabelText('Nom du modèle') as HTMLInputElement).readOnly).toBe(false)
    expect(screen.queryByRole('button', { name: 'Désactiver' })).toBeNull()
    fireEvent.click(bouton('Annuler'))
    expect(screen.getByText('Choisissez un modèle dans la liste de gauche.')).toBeTruthy()

    fireEvent.click(bouton('Nouveau modèle'))
    saisir('Nom du modèle', 'NOUVEAU')
    saisir('Sujet', 'Un courrier')
    // Un modele neuf : l'expediteur en saisie libre, l'accuse d'envoi judiciaire coche en copie cachee (ajout de la V2).
    expect(coche(choix('Saisie libre'))).toBe('true')
    expect(coche(bouton('accuse-jud@greffe-banc.example (accusé d\'envoi judiciaire)'))).toBe('true')
    saisir('Expéditeur du courrier (libellé du mail)', 'Greffe')
    fireEvent.click(choix('rcs@greffe-banc.example (RCS)'))
    saisir('Texte du modèle', 'Bonjour,\nLe greffe')
    fireEvent.click(bouton('Créer le modèle'))
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « NOUVEAU » créé.')
    const appel = pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')!
    expect(appel[2]).toBeNull()
    expect(appel[1]).toMatchObject({ type: 'NOUVEAU', corps: 'Bonjour,|Le greffe', expediteur: 'Greffe <[ADRESSE_MAIL_RCS]>', lot1: true, lot2: true,
                                     copie: '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]' })
    expect(resultats()).toContain('NOUVEAU — Nouveau courrier')
    expect(screen.getByText('Modèle NOUVEAU')).toBeTruthy()
  })

  it('Annuler remet le modele tel qu\'il etait', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => gestionnaire(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'Sujet change')
    fireEvent.click(bouton('Annuler'))
    expect(valeur('Sujet')).toBe("Certificat d'audience")
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)
  })

  // ------------------------------------------------------------------------------------------------------------------
  // Lot 16 : les gestes de l'ecran V2 « Modele de mail » -- question au changement de modele, editeur mis en forme,
  // boites du greffe par leur adresse, « Ajouter le logo ».
  // ------------------------------------------------------------------------------------------------------------------

  it('changer de modele avec une saisie en cours : la question de la V2 (OPE_RECHERCHE_EMAILMODEL.LoadEmailModel) ; « Non » abandonne la saisie et ouvre l\'autre', async () => {
    const pont = installerFauxPont({ 'EcranMessagerie.Ouvrir': () => gestionnaire(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    // Sans saisie en cours, pas de question : l'autre modele s'ouvre.
    fireEvent.click(bouton('RELANCE — Relance de facture'))
    expect(await screen.findByText('Modèle RELANCE')).toBeTruthy()
    expect(screen.queryByRole('alertdialog')).toBeNull()

    saisir('Sujet', 'Sujet change')
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    // La question, dans la forme de celle de « Désactiver » : la pastille « Attention », Oui / Non ; rien n'est encore ouvert.
    expect(question().textContent).toContain('Des changements ont été saisis, voulez-vous les enregistrer ?')
    expect(within(question()).getByText('Attention')).toBeTruthy()
    expect(pont.appels).not.toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
    repondre('Non')
    expect(await screen.findByText('Modèle REFUS_RCS')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
    expect(pont.appels.mock.calls.some((c) => c[0] === 'EcranMessagerie.EnregistrerModele')).toBe(false)
    expect(valeur('Sujet')).toBe('Refus')
    // Revenir au modele quitte : sa saisie est bien abandonnee.
    fireEvent.click(bouton("CA — Certificat d'audience"))
    expect(await screen.findByText('Modèle CA')).toBeTruthy()
    expect(valeur('Sujet')).toBe("Certificat d'audience")
  })

  it('changer de modele avec une saisie en cours : « Oui » l\'enregistre puis ouvre l\'autre ; refusee par le serveur, la saisie reste et rien ne s\'ouvre', async () => {
    let refuser = true
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => (refuser
        ? { ok: false, message: 'Le sujet tient en 200 caractères (201 saisis).', vue: null, fiche: null }
        : { ok: true, message: 'Modèle « CA » enregistré.', vue: gestionnaire(), fiche: apercu('CA', { modele: modele({ sujet: 'Sujet enregistré' }) }) }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'Sujet enregistré')
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    repondre('Oui')
    expect((await screen.findByRole('alert')).textContent).toContain('Le sujet tient en 200 caractères')
    expect(pont.appels).not.toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
    expect(valeur('Sujet')).toBe('Sujet enregistré')
    expect(screen.getByText('Modèle CA')).toBeTruthy()

    refuser = false
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    repondre('Oui')
    expect(await screen.findByText('Modèle REFUS_RCS')).toBeTruthy()
    const enregistrements = pont.appels.mock.calls.filter((c) => c[0] === 'EcranMessagerie.EnregistrerModele')
    expect(enregistrements).toHaveLength(2)
    expect(enregistrements[1]![2]).toBe('CA')
    expect((enregistrements[1]![1] as SaisieModele).sujet).toBe('Sujet enregistré')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
    // Le message de l'enregistrement reste a l'ecran sur le modele ouvert ensuite.
    expect(screen.getByRole('status').textContent).toContain('Modèle « CA » enregistré.')
  })

  it('« Nouveau modèle » avec une saisie en cours pose la meme question', async () => {
    const pont = installerFauxPont({ 'EcranMessagerie.Ouvrir': () => gestionnaire(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'Sujet change')
    fireEvent.click(bouton('Nouveau modèle'))
    expect(question().textContent).toContain('Des changements ont été saisis')
    repondre('Non')
    expect(screen.getByText('Ajout d\'un modèle de mail')).toBeTruthy()
    expect(pont.appels.mock.calls.some((c) => c[0] === 'EcranMessagerie.EnregistrerModele')).toBe(false)
  })

  it('un modele HTML sous un habillage regle s\'edite dans l\'editeur mis en forme : les boutons de RigHtmlEditor, le HTML saisi part au pont', async () => {
    const html = modele({ type: 'AVIS_HTML', libelleEcran: 'Avis HTML', sujet: 'Avis', corpsHtml: true })
    const corps = '<p>Maître,</p><p>Le <b>certificat</b>.</p><p>$PieceJointe$</p>'
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ modeles: [html, ...modelesDuGreffe], boites: boitesDuGreffe({ habillageRegle: true }) }),
      'EcranMessagerie.Modele': (t) => (t === 'AVIS_HTML' ? apercu('CA', { modele: html, corps }) : apercu(t as string)),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    const execCommand = vi.fn(() => true)
    Object.defineProperty(document, 'execCommand', { value: execCommand, configurable: true, writable: true })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle AVIS_HTML')
    // La barre de RigHtmlEditor, dans son ordre (AjouterBouton : G, I, S, listes, lien, effacer).
    const barre = within(screen.getByRole('toolbar', { name: 'Mise en forme du texte' })).getAllByRole('button')
    expect(barre.map((b) => b.getAttribute('aria-label'))).toEqual(
      ['Gras', 'Italique', 'Souligné', 'Liste à puces', 'Liste numérotée', 'Insérer un lien', 'Effacer mise en forme'])
    expect(barre.map((b) => b.textContent)).toEqual(['G', 'I', 'S', '• Liste', '1. Liste', 'Lien', 'Effacer mise en forme'])
    // Le texte est mis en forme (pas le HTML brut d'une zone de texte) ; l'ouvrir ne le « modifie » pas.
    const zone = screen.getByRole('textbox', { name: 'Texte du modèle' })
    expect(zone.tagName).toBe('DIV')
    expect(zone.getAttribute('contenteditable')).toBe('true')
    expect(zone.innerHTML).toBe(corps)
    expect(zone.querySelector('b')?.textContent).toBe('certificat')
    expect(screen.getByLabelText('Format du texte').textContent).toContain('HTML — éditeur mis en forme')
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)

    // Chaque bouton passe sa commande au navigateur, comme ExecCommand de la V2.
    const commandes: [string, string][] = [['Gras', 'bold'], ['Italique', 'italic'], ['Souligné', 'underline'],
      ['Liste à puces', 'insertUnorderedList'], ['Liste numérotée', 'insertOrderedList'], ['Effacer mise en forme', 'removeFormat']]
    for (const [nom, commande] of commandes) {
      fireEvent.click(bouton(nom))
      expect(execCommand).toHaveBeenLastCalledWith(commande, false, undefined)
    }
    // « Insérer un lien » demande l'adresse (« https:// » d'abord, comme DemanderUrl), puis cree le lien.
    fireEvent.click(bouton('Insérer un lien'))
    const lien = screen.getByRole('dialog', { name: 'Insérer un lien' })
    expect((within(lien).getByLabelText('Adresse du lien') as HTMLInputElement).value).toBe('https://')
    fireEvent.change(within(lien).getByLabelText('Adresse du lien'), { target: { value: 'https://greffe.example/doc' } })
    fireEvent.click(within(lien).getByRole('button', { name: 'OK' }))
    expect(execCommand).toHaveBeenLastCalledWith('createLink', false, 'https://greffe.example/doc')
    expect(screen.queryByRole('dialog', { name: 'Insérer un lien' })).toBeNull()

    // La saisie de l'agent : le HTML de la zone part au pont tel quel (le serveur l'assainit).
    zone.innerHTML = '<p>Maître,</p><p>Le <b>certificat</b> <i>corrigé</i>.</p><p>$PieceJointe$</p>'
    fireEvent.input(zone)
    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    expect(saisieEnvoyee(pont).corps).toBe('<p>Maître,</p><p>Le <b>certificat</b> <i>corrigé</i>.</p><p>$PieceJointe$</p>')
  })

  it('l\'editeur : un modele HTML ecrit en lignes y entre avec ses sauts de ligne, et repart TEL QUEL tant que l\'agent ne touche pas au texte', async () => {
    const lignes = modele({ type: 'COPIE_CEP', libelleEcran: 'Copie CEP', sujet: 'Duplicata', corpsHtml: true })
    const corps = 'Cher Maître,\r\nVeuillez trouver ci-joint la demande de règlement & provision.\r\n$PieceJointe$'
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ modeles: [lignes], boites: boitesDuGreffe({ habillageRegle: true }) }),
      'EcranMessagerie.Modele': () => apercu('CA', { modele: lignes, corps }),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle COPIE_CEP')
    // Ult_HtmlEditor._CorpsToHtml de la V2 : un texte sans balise y entre echappe, ses sauts de ligne en <br />.
    const zone = screen.getByRole('textbox', { name: 'Texte du modèle' })
    expect(zone.innerHTML).toBe('Cher Maître,<br>Veuillez trouver ci-joint la demande de règlement &amp; provision.<br>$PieceJointe$')
    saisir('Sujet', 'Duplicata (corrigé)')
    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    expect(saisieEnvoyee(pont)).toMatchObject({ sujet: 'Duplicata (corrigé)', corps })
  })

  it('jumeaux : un modele HTML SANS habillage regle, ou un modele texte sous habillage, garde la zone de texte (condition de la V2)', async () => {
    const html = modele({ type: 'AVIS_HTML', libelleEcran: 'Avis HTML', sujet: 'Avis', corpsHtml: true })
    const corps = '<p>Le <b>certificat</b>.</p>$PieceJointe$'
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ modeles: [html, ...modelesDuGreffe], boites: boitesDuGreffe({ habillageRegle: false }) }),
      'EcranMessagerie.Modele': (t) => (t === 'AVIS_HTML' ? apercu('CA', { modele: html, corps }) : apercu(t as string)),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle AVIS_HTML')
    expect(screen.getByLabelText('Texte du modèle').tagName).toBe('TEXTAREA')
    expect((screen.getByLabelText('Texte du modèle') as HTMLTextAreaElement).value).toBe(corps)
    expect(screen.queryByRole('toolbar', { name: 'Mise en forme du texte' })).toBeNull()
    cleanup()
    retirerFauxPont()

    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ boites: boitesDuGreffe({ habillageRegle: true }) }),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle CA')
    expect(screen.getByLabelText('Texte du modèle').tagName).toBe('TEXTAREA')
    expect(screen.queryByRole('toolbar', { name: 'Mise en forme du texte' })).toBeNull()
  })

  it('expediteur, « Répondre à », « Copie cachée » : les boites du greffe par leur ADRESSE, la saisie libre ; la base garde l\'etiquette [ADRESSE_MAIL_X]', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle CA')
    // OPE_EDIT_EMAILMODEL_ONE_CHOICE.ConstructGroupRadioButton : « Saisie libre » puis les cinq boites, par leur adresse.
    const expediteur = within(screen.getByRole('radiogroup', { name: 'Expéditeur' })).getAllByRole('radio')
    expect(expediteur.map((r) => r.textContent)).toEqual(['Saisie libre', 'rcs@greffe-banc.example (RCS)',
      'judiciaire@greffe-banc.example (judiciaire)', 'aucune adresse dans ce greffe (nantissements)',
      'compta@greffe-banc.example (comptabilité)', 'ne-pas-repondre@greffe-banc.example (ne pas répondre)'])
    expect(expediteur.map((r) => r.getAttribute('title'))).toEqual([null, '[ADRESSE_MAIL_RCS]', '[ADRESSE_MAIL_JUDICIAIRE]',
      '[ADRESSE_MAIL_NANTISSEMENTS]', '[ADRESSE_MAIL_COMPTABILITE]', '[ADRESSE_MAIL_NO_REPLY]'])
    // L'etiquette en base ([ADRESSE_MAIL_JUDICIAIRE]) se montre par son adresse, en lecture (TxtExpediteurCourrierAdr).
    expect(coche(choix('judiciaire@greffe-banc.example (judiciaire)'))).toBe('true')
    const adresse = screen.getByLabelText('Expéditeur du courrier (adresse du mail)') as HTMLInputElement
    expect(adresse.value).toBe('judiciaire@greffe-banc.example')
    expect(adresse.readOnly).toBe(true)
    fireEvent.click(choix('rcs@greffe-banc.example (RCS)'))
    saisir('Expéditeur du courrier (libellé du mail)', 'Greffe du tribunal de commerce')

    // MANY_CHOICES « RA » : « ne pas répondre » exclut les autres boites et la saisie libre, et inversement.
    expect(dansGroupe('Répondre à').map((b) => b.textContent)).toEqual(expediteur.slice(1).map((r) => r.textContent))
    expect(coche(bouton('ne-pas-repondre@greffe-banc.example (ne pas répondre)'))).toBe('true')
    expect((boiteDe('Répondre à', 'rcs@greffe-banc.example (RCS)') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('Répondre à (saisie libre)') as HTMLInputElement).readOnly).toBe(true)
    fireEvent.click(bouton('ne-pas-repondre@greffe-banc.example (ne pas répondre)'))
    fireEvent.click(boiteDe('Répondre à', 'judiciaire@greffe-banc.example (judiciaire)'))
    expect((bouton('ne-pas-repondre@greffe-banc.example (ne pas répondre)') as HTMLButtonElement).disabled).toBe(true)
    saisir('Répondre à (saisie libre)', 'secretariat@etude.example')

    // MANY_CHOICES « CC » : sans « ne pas répondre », avec les quatre accuses d'envoi.
    expect(dansGroupe('Copie cachée').map((b) => b.getAttribute('title'))).toEqual(['[ADRESSE_MAIL_RCS]', '[ADRESSE_MAIL_JUDICIAIRE]',
      '[ADRESSE_MAIL_NANTISSEMENTS]', '[ADRESSE_MAIL_COMPTABILITE]', '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]', '[ADRESSE_MAIL_ACCUSE_ENVOI_RCS]',
      '[ADRESSE_MAIL_ACCUSE_ENVOI_NANT]', '[ADRESSE_MAIL_ACCUSE_ENVOI_CPT]'])
    fireEvent.click(bouton('accuse-jud@greffe-banc.example (accusé d\'envoi judiciaire)'))
    // V2 CheckUtilisableEn : cocher « Utilisable RCS » coche l'accuse d'envoi RCS en copie cachee.
    fireEvent.click(bouton('Utilisable RCS'))
    expect(coche(bouton('accuse-rcs@greffe-banc.example (accusé d\'envoi RCS)'))).toBe('true')

    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    const s = saisieEnvoyee(pont)
    expect(s).toMatchObject({
      expediteur: 'Greffe du tribunal de commerce <[ADRESSE_MAIL_RCS]>',
      reponse: 'secretariat@etude.example;[ADRESSE_MAIL_JUDICIAIRE]',
      copie: '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD];[ADRESSE_MAIL_ACCUSE_ENVOI_RCS]',
    })
    // Aucune adresse du greffe ne part en base : les etiquettes seules.
    expect(JSON.stringify(s)).not.toContain('greffe-banc.example')
  })

  it('« Saisie libre » : l\'adresse de l\'expediteur se tape (une adresse ou une etiquette non proposee reste telle quelle)', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string, {
        modele: modele({ expediteur: 'Greffe <greffe@autre.example>', reponse: '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD];x@etude.example' }),
      }),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle CA')
    expect(coche(choix('Saisie libre'))).toBe('true')
    expect(valeur('Expéditeur du courrier (libellé du mail)')).toBe('Greffe')
    expect((screen.getByLabelText('Expéditeur du courrier (adresse du mail)') as HTMLInputElement).readOnly).toBe(false)
    expect(valeur('Expéditeur du courrier (adresse du mail)')).toBe('greffe@autre.example')
    // La V2 laissait tomber une etiquette qu'elle ne propose pas en « Répondre à » : ici elle reste, en saisie libre.
    expect(valeur('Répondre à (saisie libre)')).toBe('[ADRESSE_MAIL_ACCUSE_ENVOI_JUD];x@etude.example')
    saisir('Expéditeur du courrier (adresse du mail)', 'accueil@autre.example')
    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    expect(saisieEnvoyee(pont)).toMatchObject({ expediteur: 'Greffe <accueil@autre.example>', reponse: '[ADRESSE_MAIL_ACCUSE_ENVOI_JUD];x@etude.example' })
  })

  it('« Ajouter le logo » : $LOGO$ a la fin du texte, le modele passe en HTML ; sous l\'habillage du greffe, refuse avec la phrase du serveur', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle CA')
    expect(screen.getByLabelText('Format du texte').textContent).toBe('Format : texte')
    fireEvent.click(bouton('Ajouter le logo'))
    expect((screen.getByLabelText('Texte du modèle') as HTMLTextAreaElement).value).toBe('Maître,\nLe certificat.\n$PieceJointe$\n$LOGO$')
    expect(screen.getByLabelText('Format du texte').textContent).toBe('Format : HTML')
    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    expect(saisieEnvoyee(pont).corps).toBe('Maître,|Le certificat.|$PieceJointe$|$LOGO$')
    cleanup()
    retirerFauxPont()

    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ boites: boitesDuGreffe({ habillageRegle: true, habillagePresent: true }) }),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
    })
    render(<EcranMessagerie />)
    await screen.findByText('Modèle CA')
    fireEvent.click(bouton('Ajouter le logo'))
    expect(screen.getByRole('alert').textContent).toContain(
      'Le tag $LOGO$ est interdit quand un template est actif (le logo est positionné dans le template).')
    expect((screen.getByLabelText('Texte du modèle') as HTMLTextAreaElement).value).toBe('Maître,\nLe certificat.\n$PieceJointe$')
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)
  })

  it('Désactiver puis Réactiver : la question de la V2 d\'abord (OPE_EDIT_EMAILMODEL.cs:63, :68), puis ActiverModele(type, faux / vrai) ; rien pendant une saisie en cours', async () => {
    let actifEnBase = true
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.ActiverModele': (t, a): ResultatMessagerie => {
        actifEnBase = a as boolean
        const modeles = modelesDuGreffe.map((m) => (m.type === t ? { ...m, utilisableEnSaisie: actifEnBase } : m))
        return { ok: true, message: `Modèle « ${t as string} » ${actifEnBase ? 'réactivé' : 'désactivé'}.`, vue: gestionnaire({ modeles }),
                 fiche: apercu(t as string, { modele: modeles[0]! }) }
      },
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'en cours')
    expect((bouton('Désactiver') as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(bouton('Annuler'))
    // « Non » : rien ne part au pont, la question se referme.
    fireEvent.click(bouton('Désactiver'))
    expect(question().textContent).toContain('Voulez-vous vraiment désactiver ce modèle ?')
    repondre('Non')
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(pont.appels).not.toHaveBeenCalledWith('EcranMessagerie.ActiverModele', 'CA', false)
    fireEvent.click(bouton('Désactiver'))
    repondre('Oui')
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « CA » désactivé.')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.ActiverModele', 'CA', false)
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(screen.getAllByText('Désactivé').length).toBeGreaterThan(0)
    fireEvent.click(bouton('Réactiver'))
    expect(question().textContent).toContain('Voulez-vous vraiment activer ce modèle ?')
    repondre('Oui')
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « CA » réactivé.')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.ActiverModele', 'CA', true)
  })

  it('listes par defaut : seules les listes ACTIVES se cochent, et le modele ne garde que les cochees (OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:58-61, :99)', async () => {
    const vieille = listesDuGreffe.find((l) => l.code === 'VIEILLE')!
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string, { listesParDefaut: [liste(), vieille] }),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: true, message: 'Modèle « CA » enregistré.', vue: gestionnaire(), fiche: apercu('CA') }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    const aCocher = within(screen.getByRole('group', { name: 'Listes par défaut' })).getAllByRole('button').map((b) => b.textContent)
    expect(aCocher).toEqual(['MANDATAIRES — Mandataires du ressort', 'COMPTA_PERSO — Ma liste'])
    expect(bouton('MANDATAIRES — Mandataires du ressort').getAttribute('aria-pressed')).toBe('true')
    // Rien ne change tant que l'agent ne touche a rien ; la fiche enregistree ne garde que ses listes cochees, sans la desactivee.
    expect((bouton('Enregistrer') as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(bouton('COMPTA_PERSO — Ma liste'))
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « CA » enregistré.')
    const [, saisie] = pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')! as [string, SaisieModele, string]
    expect(saisie.listesParDefaut).toEqual([7, 8])
  })

  it('l\'envoi automatique n\'est propose qu\'a un modele RCS, et decocher RCS le remet a faux (V2 _SetVisibilityChkEnvoiAuto)', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({ ok: false, message: 'refus du banc', vue: null, fiche: null }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(screen.queryByRole('button', { name: 'Mail envoyé automatiquement (sans affichage de l\'écran de mail)' })).toBeNull()
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    const envoiAuto = await screen.findByRole('button', { name: 'Mail envoyé automatiquement (sans affichage de l\'écran de mail)' })
    expect(envoiAuto.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(bouton('Utilisable RCS'))
    expect(screen.queryByRole('button', { name: 'Mail envoyé automatiquement (sans affichage de l\'écran de mail)' })).toBeNull()
    fireEvent.click(bouton('Enregistrer'))
    await screen.findByRole('alert')
    const saisie = pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')![1] as SaisieModele
    expect(saisie).toMatchObject({ type: 'REFUS_RCS', lot1: false, envoiAutomatique: false })
  })

  it('Amitel seul : la case « Modèle modifiable uniquement par Amitel » et le dictionnaire des tags (OPE_EDIT_EMAILMODEL.cs:192-193, :333)', async () => {
    installerFauxPont({ 'EcranMessagerie.Ouvrir': () => gestionnaire(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(screen.queryByRole('button', { name: 'Modèle modifiable uniquement par Amitel' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Dictionnaire de tags' })).toBeNull()
    cleanup()
    retirerFauxPont()

    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire({ estAmitel: true, voitReservesAmitel: true, dictionnaire: 'TOKENS DU TEMPLATE\n  {{NOM_GREFFIER}}' }),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    expect(bouton('Modèle modifiable uniquement par Amitel').getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(bouton('Dictionnaire de tags'))
    expect(screen.getByLabelText('Dictionnaire de tags').textContent).toContain('{{NOM_GREFFIER}}')
  })

  it('les listes : Nouvelle liste, Enregistrer (adresses une par ligne), une liste publique ne redevient pas privee, Désactiver', async () => {
    const pont = installerFauxPont({
      'EcranMessagerie.Ouvrir': () => gestionnaire(),
      'EcranMessagerie.Modele': (t) => apercu(t as string),
      'EcranMessagerie.EnregistrerListe': (s, id): ResultatMessagerie => {
        const d = s as SaisieListe
        const enregistree = liste({ id: (id as number | null) ?? 12, code: d.code, libelle: d.libelle, adresses: d.adresses.split('\n').join(';'),
                                    proprietaire: d.publique ? '*' : 'banc.courriel' })
        return { ok: true, message: `Liste « ${d.code} » ${id === null ? 'créée' : 'enregistrée'}.`, vue: gestionnaire({ listes: [...listesDuGreffe, enregistree] }), fiche: null }
      },
      'EcranMessagerie.ActiverListe': (id, a): ResultatMessagerie => ({
        ok: true, message: `Liste « MANDATAIRES » ${a ? 'réactivée' : 'désactivée'}.`, fiche: null,
        vue: gestionnaire({ listes: listesDuGreffe.map((l) => (l.id === id ? { ...l, actif: a as boolean } : l)) }),
      }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    fireEvent.click(bouton('Listes de distribution'))

    fireEvent.click(bouton('Nouvelle liste'))
    expect(screen.getByText('Ajout d\'une liste de distribution')).toBeTruthy()
    saisir('Code', 'EXPERTS')
    saisir('Libellé', 'Experts du ressort')
    saisir('Adresses (une par ligne)', 'expert.un@cabinet.example\nexpert.deux@cabinet.example')
    expect(bouton('Privée (banc.courriel)').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('status')).textContent).toContain('Liste « EXPERTS » créée.')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.EnregistrerListe',
      { code: 'EXPERTS', libelle: 'Experts du ressort', adresses: 'expert.un@cabinet.example\nexpert.deux@cabinet.example', rcs: false,
        judiciaire: false, publique: false, ecran: null }, null)
    expect(screen.getByText('Liste EXPERTS')).toBeTruthy()

    // GroupRadioButtonPrive de la V2 : une liste deja publique ne repasse pas en privee.
    fireEvent.click(screen.getByText('Mandataires du ressort'))
    expect(screen.getByText('Liste MANDATAIRES')).toBeTruthy()
    expect((bouton('Privée (banc.courriel)') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('Code') as HTMLInputElement).readOnly).toBe(true)
    // La question de la V2 (OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:39, :44) : « Non » ne fait rien, « Oui » desactive.
    fireEvent.click(bouton('Désactiver'))
    expect(question().textContent).toContain('Voulez-vous vraiment désactiver cette liste ?')
    repondre('Non')
    expect(pont.appels).not.toHaveBeenCalledWith('EcranMessagerie.ActiverListe', 7, false)
    fireEvent.click(bouton('Désactiver'))
    repondre('Oui')
    expect((await screen.findByRole('status')).textContent).toContain('Liste « MANDATAIRES » désactivée.')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.ActiverListe', 7, false)
    fireEvent.click(bouton('Réactiver'))
    expect(question().textContent).toContain('Voulez-vous vraiment activer cette liste ?')
    repondre('Oui')
    expect((await screen.findByRole('status')).textContent).toContain('Liste « MANDATAIRES » réactivée.')
    expect(pont.appels).toHaveBeenCalledWith('EcranMessagerie.ActiverListe', 7, true)
  })

  it('sans serveur : le formulaire de la demonstration, et l\'enregistrement refuse proprement par le pont', async () => {
    installerFauxPont({
      'EcranMessagerie.Ouvrir': () => vue({ mode: 'demo', greffe: null, peutModifier: true, compte: 'agent.demo' }),
      'EcranMessagerie.Modele': (t) => ({ ...apercu(t as string), mode: 'demo' }),
      'EcranMessagerie.EnregistrerModele': (): ResultatMessagerie => ({
        ok: false, message: 'Démonstration : sans serveur, aucun modèle n\'est enregistré. Ouvrez RIG branché sur son serveur pour modifier les modèles du greffe.',
        vue: null, fiche: null,
      }),
    })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'Essai')
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('alert')).textContent).toContain('Démonstration : sans serveur, aucun modèle n\'est enregistré.')
    expect(valeur('Sujet')).toBe('Essai')
  })
})
