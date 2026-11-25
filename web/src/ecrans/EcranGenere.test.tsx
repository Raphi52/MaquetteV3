import { afterEach, describe, expect, it } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from './EcranGenere'
import type { DescripteurEcranVue, LigneEcranVue } from '../plan/types'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Un descripteur de test : le code n'est PAS une procedure du menu (aucun PROC_ dans web/src).
function descripteur(partiel: Partial<DescripteurEcranVue> = {}): DescripteurEcranVue {
  return {
    code: 'ECRAN_TEST', titre: 'Ecran test', sujet: 'dossiers', recherche: 'Rechercher un dossier…',
    criteres: ['Numéro', 'Date'], valider: 'Enregistrer', rang: 1,
    etats: [
      { code: 'AFaire', libelle: 'À saisir', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'En saisie', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Enregistrée', icone: 'validee' },
    ],
    groupes: ['Fond', 'Référé'],
    marches: [
      { titre: 'Instance', consigne: 'Le code affaire.', obligatoire: true, champs: ['Code affaire'] },
      { titre: 'Parties', consigne: 'Un demandeur au moins.', obligatoire: true, champs: ['Demandeurs'] },
      { titre: 'Événements', consigne: 'Facultatif.', obligatoire: false, champs: ['Événements'] },
    ],
    lignes: [
      { nom: 'Alpha SARL', precisions: 'Rôle 1', groupe: 'Fond', etat: 'AFaire', faites: [] },
      { nom: 'Bêta SAS', precisions: 'Rôle 2', groupe: 'Référé', etat: 'EnCours', faites: [0] },
      { nom: 'Gamma SARL', precisions: 'Rôle 3', groupe: 'Fond', etat: 'Validee', faites: [0, 1] },
    ],
    membres: [],
    ...partiel,
  }
}

// Un ecran REUNI comme le fabrique Pont/EcransGeneres/CatalogueGenere.cs : un groupe par procedure,
// tuiles communes, marches vides ; chaque procedure garde ses marches, son bouton, ses etats.
function reuni(): DescripteurEcranVue {
  const relance = descripteur({
    code: 'RELANCE_TEST', titre: 'Relances', valider: 'Envoyer la relance', groupes: ['Sans réponse'],
    etats: [
      { code: 'AFaire', libelle: 'À relancer', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'En cours', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Relancé', icone: 'validee' },
    ],
    marches: [{ titre: 'Exercices', consigne: 'Cocher.', obligatoire: true, champs: [] }],
    lignes: [{ nom: 'Delta SAS', precisions: 'Exercice 2025', groupe: 'Sans réponse', etat: 'AFaire', faites: [] }],
  })
  const membres = [descripteur(), relance]
  return descripteur({
    code: 'REUNI_TEST', titre: 'Ecran reuni', valider: 'Valider', marches: [],
    etats: [
      { code: 'AFaire', libelle: 'À traiter', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'En cours', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Terminé', icone: 'validee' },
    ],
    groupes: membres.map((m) => m.titre),
    lignes: membres.flatMap((m) => m.lignes.map((l) => ({ ...l, groupe: m.titre, precisions: `${l.groupe} · ${l.precisions}` }))),
    membres,
  })
}

// Ce que Pont/EcranGenere.cs rend sans serveur : les exemples du descripteur, tous ses groupes marques.
function vueExemple(d: DescripteurEcranVue, partiel: Partial<VueLignesEcran> = {}): VueLignesEcran {
  return { mode: 'exemple', message: null, greffe: null, lignes: d.lignes, exemples: d.groupes, ...partiel }
}

// Deux lignes lues dans la base du greffe (Serveur/Socle/EcransGeneres/) : aucun nom du descripteur.
const lignesDeLaBase: LigneEcranVue[] = [
  { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'reçu le 02/03/2026', groupe: 'Fond', etat: 'AFaire', faites: [] },
  { nom: 'XQN NEGOCE SAS · SIREN 412 000 002', precisions: 'déposé le 04/03/2026', groupe: 'Référé', etat: 'Validee', faites: [0, 1] },
]

/** Monte l'ecran derriere un faux pont qui rend <vue> (par defaut : les exemples), et attend la reponse. */
async function monter(d: DescripteurEcranVue = descripteur(), vue: VueLignesEcran = vueExemple(d)) {
  const pont = installerFauxPont({ 'EcranGenere.Lignes': () => vue })
  const rendu = render(<EcranGenere descripteur={d} />)
  const { container } = rendu
  await waitFor(() => expect(container.querySelector('.rig-tcw__provenance')).not.toBeNull())
  return { ...outils(container), pont, rendu }
}

function outils(container: HTMLElement) {
  const noms = () => [...container.querySelectorAll('.rig-tcw__cmd-nom')].map((n) => n.textContent)
  const valeurs = () => [...container.querySelectorAll('.rig-chiffre__valeur')].map((b) => b.textContent)
  const ligne = (nom: string) => [...container.querySelectorAll<HTMLButtonElement>('.rig-tcw__cmd')]
    .find((b) => b.querySelector('.rig-tcw__cmd-nom')?.textContent === nom)!
  // fix-ok: un index de tableau vaut T | undefined (noUncheckedIndexedAccess) : mesure par le typecheck (TS2532/TS2345 aux clics) ; les index sont assertes non nuls.
  const boutonsMarche = () => [...container.querySelectorAll<HTMLButtonElement>('.rig-tcw__marche .rig-tcw__lien')]
  const valider = () => container.querySelector<HTMLButtonElement>('.rig-tcw__valider')!
  return { container, noms, valeurs, ligne, boutonsMarche, valider }
}

describe('EcranGenere', () => {
  it('affiche une tuile par etat avec son compte, et les groupes avec leur nombre', async () => {
    const { container, valeurs } = await monter()
    expect(valeurs()).toEqual(['1', '1', '1'])
    expect([...container.querySelectorAll('.rig-tcw__groupe-titre')].map((t) => t.textContent)).toEqual(['Fond', 'Référé'])
    expect([...container.querySelectorAll('.rig-tcw__groupe-nombre')].map((t) => t.textContent)).toEqual(['2', '1'])
  })

  it('la pilule filtre la liste a chaque frappe, sans tenir compte de la casse', async () => {
    const { container, noms } = await monter()
    const champ = container.querySelector<HTMLInputElement>('.rig-recherche__saisie')!
    fireEvent.change(champ, { target: { value: 'bêta' } })
    expect(noms()).toEqual(['Bêta SAS'])
    fireEvent.change(champ, { target: { value: '' } })
    expect(noms()).toHaveLength(3)
  })

  it('une tuile filtre la liste sur son etat ; la presser a nouveau le relache', async () => {
    const { container, noms } = await monter()
    const tuiles = container.querySelectorAll<HTMLButtonElement>('.rig-tcw__tuile')
    fireEvent.click(tuiles[1]!)
    expect(tuiles[1]!.getAttribute('aria-pressed')).toBe('true')
    expect(noms()).toEqual(['Bêta SAS'])
    fireEvent.click(tuiles[1]!)
    expect(noms()).toHaveLength(3)
  })

  it('franchir les marches fait passer la ligne en cours, et Valider la valide une fois les obligatoires faites', async () => {
    const { container, ligne, boutonsMarche, valider, valeurs } = await monter()
    fireEvent.click(ligne('Alpha SARL'))
    expect(container.querySelector('.rig-tcw__scene--ouverte')).not.toBeNull()
    expect(valider().disabled).toBe(true)
    expect(container.querySelector('.rig-tcw__blocage')!.textContent).toBe('Reste à faire : Instance · Parties')

    fireEvent.click(boutonsMarche()[0]!)
    expect(valeurs()).toEqual(['0', '2', '1'])
    expect(valider().disabled).toBe(true)

    fireEvent.click(boutonsMarche()[1]!)
    expect(valider().disabled).toBe(false)
    fireEvent.click(valider())
    expect(valeurs()).toEqual(['0', '1', '2'])
    expect(container.querySelector('.rig-tcw__blocage')!.textContent).toBe('Ligne validée : elle ne se modifie plus.')
  })

  it('une ligne validee ne se modifie plus : ses marches sont figees', async () => {
    const { ligne, boutonsMarche, valider } = await monter()
    fireEvent.click(ligne('Gamma SARL'))
    expect(boutonsMarche()).toHaveLength(0)
    expect(valider().disabled).toBe(true)
  })

  it('un groupe replie garde le nom de ses lignes en resume, et ses lignes deviennent inertes', async () => {
    const { container } = await monter()
    fireEvent.click(container.querySelector('.rig-tcw__groupe-tete')!)
    const groupe = container.querySelector('.rig-tcw__groupe')!
    expect(groupe.classList.contains('rig-tcw__groupe--plie')).toBe(true)
    expect(groupe.querySelector('.rig-tcw__groupe-resume')!.textContent).toBe('Alpha SARL · Gamma SARL')
    expect(groupe.querySelector('.rig-tcw__groupe-corps')!.hasAttribute('inert')).toBe(true)
  })

  it('un ecran reuni : un groupe par procedure, et chaque ligne suit les marches, le bouton et les etats de la sienne', async () => {
    const { container, ligne, boutonsMarche, valider, valeurs } = await monter(reuni())
    expect([...container.querySelectorAll('.rig-tcw__groupe-titre')].map((t) => t.textContent)).toEqual(['Ecran test', 'Relances'])
    expect([...container.querySelectorAll('.rig-chiffre__libelle')].map((t) => t.textContent)).toEqual(['À traiter', 'En cours', 'Terminé'])
    expect(valeurs()).toEqual(['2', '1', '1'])
    // La ligne garde le nom d'etat et l'avancement de SA procedure.
    const delta = ligne('Delta SAS')
    expect(delta.querySelector('.rig-tcw__cmd-type')!.textContent).toBe('À relancer')
    expect(delta.querySelector('.rig-tcw__avancement')!.textContent).toBe('0/1')
    expect(ligne('Alpha SARL').querySelector('.rig-tcw__avancement')!.textContent).toBe('0/3')

    fireEvent.click(delta)
    expect(container.querySelector('.rig-tcw__type')!.textContent).toBe('Relances')
    expect(container.querySelectorAll('.rig-tcw__marche')).toHaveLength(1)
    expect(valider().textContent).toBe('Envoyer la relance0/1')
    fireEvent.click(boutonsMarche()[0]!)
    fireEvent.click(valider())
    expect(valeurs()).toEqual(['1', '1', '2'])
    expect(delta.querySelector('.rig-tcw__cmd-type')!.textContent).toBe('Relancé')
  })
})

// Les lignes de l'ecran (plan « ecrans retrouves branches sur la base ») : Pont/EcranGenere.cs les lit sur le serveur
// (un lecteur par procedure) ; sans serveur, sans lecteur ou en panne, les exemples du descripteur, marques « exemple ».
describe('EcranGenere : lignes de la base ou exemples', () => {
  // fix-ok: vitest rouge mesure (3 tests) : l'ecran range ses lignes groupe par groupe (Fond, puis Référé) ;
  // l'attente suivait l'ordre du descripteur. C'est l'ordre d'affichage qui est attendu ici.
  const nomsExemple = ['Alpha SARL', 'Gamma SARL', 'Bêta SAS']
  const provenance = (container: HTMLElement) => container.querySelector('.rig-tcw__provenance')!.textContent
  const mentions = (container: HTMLElement) => container.querySelectorAll('.rig-tcw__exemple').length

  it('branche sur la base : les vraies lignes, aucun nom fictif du descripteur, et la provenance dite', async () => {
    const d = descripteur()
    const { container, noms, valeurs, pont } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] })
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Lignes', 'ECRAN_TEST')
    expect(noms()).toEqual(['HKO DUPRAT SARL · SIREN 412 000 001', 'XQN NEGOCE SAS · SIREN 412 000 002'])
    expect(noms().some((n) => nomsExemple.includes(n))).toBe(false)
    expect(valeurs()).toEqual(['1', '0', '1'])
    expect(provenance(container)).toBe('Lignes de la base du greffe 3801.')
    expect(mentions(container)).toBe(0)
  })

  it('le plafond atteint se dit sous la provenance', async () => {
    const { container } = await monter(descripteur(), {
      mode: 'base', message: 'Les 200 premières lignes sur 1234.', greffe: '3801', lignes: lignesDeLaBase, exemples: [] })
    expect(provenance(container)).toBe('Lignes de la base du greffe 3801. Les 200 premières lignes sur 1234.')
  })

  it('une base sans ligne le dit, au lieu de « rien ne correspond »', async () => {
    const { container, noms } = await monter(descripteur(), { mode: 'base', message: null, greffe: '3801', lignes: [], exemples: [] })
    expect(noms()).toEqual([])
    expect(container.querySelector('.rig-tcw__vide')!.textContent).toBe('Aucune ligne dans la base du greffe 3801.')
  })

  it('sans serveur : les lignes d\'exemple, avec la mention visible « exemple »', async () => {
    const { container, noms } = await monter()
    expect(noms()).toEqual(nomsExemple)
    expect(provenance(container)).toBe('exemple Lignes d’exemple : noms fictifs, aucun dossier réel.')
    expect(mentions(container)).toBe(1)
  })

  it('le serveur en panne : les exemples restent, marques, avec le motif', async () => {
    const d = descripteur()
    const { container, noms } = await monter(d, vueExemple(d, { mode: 'erreur', greffe: '3801', message: 'La base du greffe 3801 ne répond pas.' }))
    expect(noms()).toEqual(nomsExemple)
    expect(provenance(container)).toBe('exemple Lignes d’exemple : noms fictifs, aucun dossier réel. La base du greffe 3801 ne répond pas.')
  })

  it('le pont qui echoue (erreur du C#) laisse aussi les exemples marques : jamais un ecran vide sans le dire', async () => {
    installerFauxPont({ 'EcranGenere.Lignes': () => { throw new Error('ECRAN_TEST n\'a pas de descripteur') } })
    const { container } = render(<EcranGenere descripteur={descripteur()} />)
    await waitFor(() => expect(container.querySelector('.rig-tcw__provenance')).not.toBeNull())
    expect(outils(container).noms()).toEqual(nomsExemple)
    expect(provenance(container)).toContain('Les lignes de la base n’ont pas pu être lues.')
    expect(mentions(container)).toBe(1)
  })

  it('avant la reponse : aucun nom fictif, la liste dit qu\'elle lit', async () => {
    let repondre: (v: VueLignesEcran) => void = () => {}
    installerFauxPont({ 'EcranGenere.Lignes': () => new Promise<VueLignesEcran>((r) => { repondre = r }) })
    const { container } = render(<EcranGenere descripteur={descripteur()} />)
    const { noms } = outils(container)
    expect(noms()).toEqual([])
    expect(container.querySelector('.rig-tcw__vide')!.textContent).toBe('Lecture des lignes…')
    await act(async () => { repondre({ mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] }) })
    expect(noms()).toHaveLength(2)
  })

  it('un ecran reuni dont une procedure n\'a pas de lecteur : ses lignes restent des exemples, son groupe le dit', async () => {
    const d = reuni()
    const reelle = { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'Fond · reçu le 02/03/2026', groupe: 'Ecran test', etat: 'AFaire', faites: [] }
    const exemple = d.lignes.filter((l) => l.groupe === 'Relances')
    const { container, noms } = await monter(d, { mode: 'mixte', message: null, greffe: '3801', lignes: [reelle, ...exemple], exemples: ['Relances'] })
    expect(noms()).toEqual(['HKO DUPRAT SARL · SIREN 412 000 001', 'Delta SAS'])
    expect(provenance(container)).toBe('Lignes de la base du greffe 3801, sauf les groupes marqués exemple.')
    const groupes = [...container.querySelectorAll('.rig-tcw__groupe-tete')]
    expect(groupes.map((g) => g.querySelector('.rig-tcw__exemple') !== null)).toEqual([false, true])
  })

  it('un redessin de la racine (descripteur neuf, meme procedure) ne relit pas la base et garde la ligne ouverte', async () => {
    const d = descripteur()
    const { container, pont, rendu, ligne } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] })
    fireEvent.click(ligne('HKO DUPRAT SARL · SIREN 412 000 001'))
    rendu.rerender(<EcranGenere descripteur={{ ...d, lignes: [...d.lignes] }} />)
    await act(async () => {})
    expect(pont.appels).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.rig-tcw__titre')!.textContent).toBe('HKO DUPRAT SARL · SIREN 412 000 001')
  })
})
