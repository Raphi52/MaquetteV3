import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import {
  EcranMessagerie, type ApercuModele, type ListeDiffusion, type ModeleMessage, type ResultatMessagerie, type SaisieListe, type SaisieModele,
  type VueEcranMessagerie, type VueModeleMessagerie,
} from './EcranMessagerie'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

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

function vue(partiel: Partial<VueEcranMessagerie> = {}): VueEcranMessagerie {
  return {
    mode: 'serveur', message: null, greffe: '3801', voitReservesAmitel: false, modeles: modelesDuGreffe, listes: listesDuGreffe,
    peutModifier: false, estAmitel: false, compte: 'banc.courriel', dictionnaire: null, ...partiel,
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
    const lignes = () => [...container.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
    expect(lignes()).toEqual([
      ['MANDATAIRES', 'Mandataires du ressort', 'Non', 'Oui', 'Tous les agents', 'Oui', 'a@etude.example ; b@etude.example'],
      ['COMPTA_PERSO', 'Ma liste', 'Non', 'Non', 'banc.courriel', 'Oui', 'a@etude.example ; b@etude.example'],
    ])
    fireEvent.click(screen.getByRole('button', { name: 'Afficher les éléments inactifs' }))
    expect(lignes()).toHaveLength(3)
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
    // En modification, l'adresse se saisit telle qu'en base (l'etiquette de la boite du greffe) ; le nom ne change plus.
    expect(valeur('Expéditeur')).toBe('[ADRESSE_MAIL_JUDICIAIRE]')
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
    saisir('Expéditeur', 'pas une adresse')
    fireEvent.click(bouton('Enregistrer'))
    expect((await screen.findByRole('alert')).textContent).toContain('« pas une adresse » n\'est ni une adresse de courriel')
    expect(valeur('Expéditeur')).toBe('pas une adresse')
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
    saisir('Expéditeur', 'Greffe <[ADRESSE_MAIL_RCS]>')
    saisir('Texte du modèle', 'Bonjour,\nLe greffe')
    fireEvent.click(bouton('Créer le modèle'))
    expect((await screen.findByRole('status')).textContent).toContain('Modèle « NOUVEAU » créé.')
    const appel = pont.appels.mock.calls.find((c) => c[0] === 'EcranMessagerie.EnregistrerModele')!
    expect(appel[2]).toBeNull()
    expect(appel[1]).toMatchObject({ type: 'NOUVEAU', corps: 'Bonjour,|Le greffe', expediteur: 'Greffe <[ADRESSE_MAIL_RCS]>', lot1: true, lot2: true })
    expect(resultats()).toContain('NOUVEAU — Nouveau courrier')
    expect(screen.getByText('Modèle NOUVEAU')).toBeTruthy()
  })

  it('Annuler remet le modele tel qu\'il etait ; une saisie non enregistree empeche d\'ouvrir un autre modele', async () => {
    const pont = installerFauxPont({ 'EcranMessagerie.Ouvrir': () => gestionnaire(), 'EcranMessagerie.Modele': (t) => apercu(t as string) })
    render(<EcranMessagerie />)
    await screen.findByTitle('Aperçu du courriel')
    saisir('Sujet', 'Sujet change')
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    expect((await screen.findByRole('alert')).textContent).toContain('modifications non enregistrées')
    expect(pont.appels).not.toHaveBeenCalledWith('EcranMessagerie.Modele', 'REFUS_RCS')
    fireEvent.click(bouton('Annuler'))
    expect(valeur('Sujet')).toBe("Certificat d'audience")
    fireEvent.click(bouton('REFUS_RCS — Refus de formalité'))
    expect(await screen.findByText('Modèle REFUS_RCS')).toBeTruthy()
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
