import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import {
  EcranMessagerie, type ApercuModele, type ListeDiffusion, type ModeleMessage, type VueEcranMessagerie, type VueModeleMessagerie,
} from './EcranMessagerie'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Ce que le serveur rend est prouve par tests/SocleCourrielsCheck (CA05 : un agent du greffe ne recoit jamais un modele
// reserve a Amitel, ni par la liste ni par son nom). Ici : l'ecran montre ce que le pont lui donne, et SEULEMENT a un
// agent d'Amitel la bascule « Amitel » (OPE_EDIT_EMAILMODEL.cs:333, CheckBoxAmitel visible pour IsUserAmitel).
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

function vue(partiel: Partial<VueEcranMessagerie> = {}): VueEcranMessagerie {
  return {
    mode: 'serveur', message: null, greffe: '3801', voitReservesAmitel: false, modeles: modelesDuGreffe,
    listes: [liste(), liste({ id: 8, code: 'COMPTA_PERSO', libelle: 'Ma liste', proprietaire: 'banc.courriel', judiciaire: false }),
             liste({ id: 9, code: 'VIEILLE', libelle: 'Liste inactive', actif: false })],
    ...partiel,
  }
}

function apercu(type: string): VueModeleMessagerie {
  const m = [...modelesDuGreffe, modele({ type: 'AMI_INTERNE', reserveAmitel: true })].find((x) => x.type === type)!
  const detail: ApercuModele = {
    modele: m, corps: 'Maître,|Le certificat.|$PieceJointe$',
    expediteur: 'judiciaire@greffe-banc.example', reponse: 'ne-pas-repondre@greffe-banc.example', copie: '', destinataireDefaut: '',
    rendu: '<html><body><h1>Tribunal de commerce de GRENOBLE</h1>Maître,<br/>Le certificat.</body></html>', habille: true,
    valeursManquantes: [], listesParDefaut: [liste()],
  }
  return { mode: 'serveur', message: null, apercu: detail }
}

const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value
const resultats = () => within(screen.getByRole('list', { name: 'Modèles' })).getAllByRole('button').map((b) => b.textContent)

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
})
