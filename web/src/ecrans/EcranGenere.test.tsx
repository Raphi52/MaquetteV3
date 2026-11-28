import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from './EcranGenere'
import type { ChampEcranVue, DescripteurEcranVue, FormeEcranVue, GestePlan, LigneEcranVue, TypeChampVue } from '../plan/types'
import { Aiguillage } from '../plan/Aiguillage'
import { proc } from '../test/plan'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

afterEach(() => { cleanup(); retirerFauxPont() })

const champ = (libelle: string, type: TypeChampVue = 'Texte', options: string[] = []): ChampEcranVue => ({ libelle, type, options })

// Un descripteur de test : le code n'est PAS une procedure du menu (aucun PROC_ dans web/src). Forme File par defaut.
function descripteur(partiel: Partial<DescripteurEcranVue> = {}): DescripteurEcranVue {
  return {
    code: 'ECRAN_TEST', titre: 'Ecran test', forme: 'File', sujet: 'dossiers', recherche: 'Rechercher un dossier…',
    criteres: ['Numéro', 'Date'], valider: 'Relancer', rang: 1,
    etats: [
      { code: 'AFaire', libelle: 'À relancer', icone: 'a-traiter' },
      { code: 'EnCours', libelle: 'En cours', icone: 'saisie-manuelle' },
      { code: 'Validee', libelle: 'Relancé', icone: 'validee' },
    ],
    groupes: ['Fond', 'Référé'],
    marches: [],
    sections: [
      { titre: 'Instance', consigne: 'Le code affaire.', obligatoire: true, champs: [champ('Code affaire'), champ('Date de l’audience', 'Date')] },
      { titre: 'Parties', consigne: 'Un demandeur au moins.', obligatoire: false, champs: [champ('Demandeurs', 'Tableau'), champ('Aide juridictionnelle', 'Case')] },
      { titre: 'Décision', consigne: 'Le sens de la décision.', obligatoire: false, champs: [champ('Sens', 'Liste', ['Accepte', 'Rejette'])] },
    ],
    colonnes: [], parametres: [], gestes: [],
    lignes: [
      { nom: 'Alpha SARL', precisions: 'Rôle 1', groupe: 'Fond', etat: 'AFaire', faites: [] },
      { nom: 'Bêta SAS', precisions: 'Rôle 2', groupe: 'Référé', etat: 'EnCours', faites: [0] },
      { nom: 'Gamma SARL', precisions: 'Rôle 3', groupe: 'Fond', etat: 'Validee', faites: [0] },
    ],
    membres: [],
    enOnglets: false,
    ...partiel,
  }
}

// Un ecran REUNI comme le fabrique Pont/EcransGeneres/CatalogueGenere.cs : un groupe par procedure, tuiles communes ;
// chaque procedure garde ses sections, ses gestes, ses etats.
function reuni(forme: FormeEcranVue = 'File', enOnglets = false): DescripteurEcranVue {
  const relance = descripteur({
    code: 'RELANCE_TEST', titre: 'Relances', forme, valider: 'Envoyer la relance', groupes: ['Sans réponse'],
    gestes: ['Envoyer la relance', 'Imprimer'],
    lignes: [{ nom: 'Delta SAS', precisions: 'Exercice 2025', groupe: 'Sans réponse', etat: 'AFaire', faites: [] }],
  })
  const membres = [descripteur({ forme }), relance]
  return descripteur({
    code: 'REUNI_TEST', titre: 'Ecran reuni', forme, valider: 'Valider', sections: [], enOnglets,
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
  { nom: 'XQN NEGOCE SAS · SIREN 412 000 002', precisions: 'déposé le 04/03/2026', groupe: 'Référé', etat: 'Validee', faites: [0] },
]

/** Monte l'ecran derriere un faux pont qui rend <vue> (par defaut : les exemples), et attend la reponse. */
async function monter(d: DescripteurEcranVue = descripteur(), vue: VueLignesEcran = vueExemple(d)) {
  const pont = installerFauxPont({ 'EcranGenere.Lignes': () => vue })
  const rendu = render(<EcranGenere descripteur={d} />)
  const { container } = rendu
  await waitFor(() => expect(container.querySelector('.rig-eg-note')).not.toBeNull())
  return { ...outils(container), pont, rendu }
}

function outils(container: HTMLElement) {
  const bouton = (texte: string) => [...container.querySelectorAll('button')].find((b) => b.textContent === texte)!
  return {
    container,
    bouton,
    noms: () => [...container.querySelectorAll('.rig-lm-identite__nom')].map((n) => n.textContent),
    ligne: (nom: string) => [...container.querySelectorAll('tr')].find((tr) => tr.querySelector('.rig-lm-identite__nom')?.textContent === nom)!,
    tuiles: () => [...container.querySelectorAll('.rig-lm-carte')].map((c) => [c.querySelector('.rig-lm-carte__titre')!.textContent, c.querySelector('.rig-lm-carte__valeur')!.firstChild!.textContent]),
    annonce: () => container.querySelector('.rig-eg-annonce')?.textContent ?? null,
    forme: () => container.querySelector('[data-forme]')?.getAttribute('data-forme'),
  }
}

/** L'escalier n'existe plus nulle part : ni « Terminer l'etape », ni compteur « n/m » sur un bouton. */
function sansEscalier(container: HTMLElement) {
  expect(container.textContent).not.toContain('Terminer l’étape')
  expect([...container.querySelectorAll('button')].some((b) => /\d+\/\d+/.test(b.textContent ?? ''))).toBe(false)
  expect(container.querySelector('.rig-tcw__escalier, .rig-tcw__valider-compte')).toBeNull()
}

describe('EcranGenere : l’aiguilleur monte le composant de la forme', () => {
  const formes: FormeEcranVue[] = ['File', 'Consultation', 'Fiche', 'Saisie', 'Referentiel', 'Etat', 'Audience', 'Document', 'Operation']
  for (const forme of formes) {
    it(`forme ${forme}`, async () => {
      const partiel: Partial<DescripteurEcranVue> = forme === 'Saisie'
        ? { forme, marches: descripteur().sections, sections: [] }
        : forme === 'Referentiel' || forme === 'Etat' ? { forme, colonnes: [champ('Libellé'), champ('Montant', 'Montant')] }
        : { forme }
      const { forme: monte, container } = await monter(descripteur(partiel))
      expect(monte()).toBe(forme)
      sansEscalier(container)
    })
  }
})

describe('Forme File : une file de travail', () => {
  it('une tuile par etat compte et filtre ; la presser a nouveau la relache', async () => {
    const { tuiles, container, noms } = await monter()
    expect(tuiles()).toEqual([['À relancer', '1'], ['En cours', '1'], ['Relancé', '1']])
    fireEvent.click(container.querySelector('.rig-lm-carte')!)
    expect(noms()).toEqual(['Alpha SARL'])
    fireEvent.click(container.querySelector('.rig-lm-carte')!)
    expect(noms()).toHaveLength(3)
  })

  it('le champ de recherche filtre a chaque frappe, sans casse ni accents', async () => {
    const { container, noms } = await monter()
    fireEvent.change(container.querySelector('.rig-lm-cherche__saisie')!, { target: { value: 'beta' } })
    expect(noms()).toEqual(['Bêta SAS'])
  })

  it('cases a cocher + geste en lot : le geste dit « Demonstration » et ne change aucun etat (jamais un faux succes)', async () => {
    const { container, bouton, annonce, tuiles, ligne } = await monter()
    expect(bouton('Relancer').disabled).toBe(true)
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    fireEvent.click(ligne('Bêta SAS').querySelector('input[type=checkbox]')!)
    expect(container.querySelector('.rig-eg-lot__compte')!.textContent).toBe('2 sélectionnées')
    fireEvent.click(bouton('Relancer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Relancer » sur 2 lignes : aucune n’est modifiée dans la base.')
    expect(tuiles()).toEqual([['À relancer', '1'], ['En cours', '1'], ['Relancé', '1']])
  })

  it('une ligne figee ne se coche pas ; « tout cocher » ne prend que les lignes a traiter', async () => {
    const { container, ligne } = await monter()
    expect((ligne('Gamma SARL').querySelector('input[type=checkbox]') as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(container.querySelector('input[aria-label="Tout cocher"]')!)
    expect(container.querySelector('.rig-eg-lot__compte')!.textContent).toBe('2 sélectionnées')
  })

  it('un ecran reuni : chaque geste ne vise que les lignes des procedures qui le portent', async () => {
    const { bouton, ligne, annonce } = await monter(reuni())
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    fireEvent.click(ligne('Delta SAS').querySelector('input[type=checkbox]')!)
    fireEvent.click(bouton('Imprimer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Imprimer » sur 1 ligne : aucune n’est modifiée dans la base.')
    fireEvent.click(bouton('Relancer'))
    expect(annonce()).toContain('« Relancer » sur 1 ligne')
  })

  it('une action grisee dit pourquoi : rien de coche, ou les lignes cochees sont d’une procedure qui ne la porte pas', async () => {
    const { bouton, ligne } = await monter(reuni())
    expect(bouton('Imprimer').title).toBe('Cochez au moins une ligne')
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    expect(bouton('Imprimer').disabled).toBe(true)
    expect(bouton('Imprimer').title).toBe('« Imprimer » ne s’applique pas aux lignes cochées : il ne vaut que pour « Relances »')
    expect(bouton('Relancer').title).toBe('')
  })

  // Lot 16 du plan « RIG v3 100 % fonctionnel » (27/11/2026, decision du chef) : un geste de RIG v2 qui vise une ligne hors « a
  // faire » et « en cours » -- « Désactiver » une demande FNIG ENVOYEE, « Renvoyer » une demande EN ERREUR (OPE_EDITION.cs:296-305)
  // -- declare ses etats (etatsGestes) : la ligne se coche, ce geste part sur elle ; un geste qui ne les declare pas reste fige.
  it('un geste qui declare ses etats se joue sur une ligne figee ; un geste sans declaration reste fige sur elle', async () => {
    const d = descripteur({ gestes: ['Désactiver', 'Relancer'], etatsGestes: { Désactiver: ['Validee'] } })
    const { bouton, ligne, annonce, container } = await monter(d)
    const gamma = ligne('Gamma SARL').querySelector('input[type=checkbox]') as HTMLInputElement
    expect(gamma.disabled).toBe(false)
    fireEvent.click(gamma)
    expect(bouton('Désactiver').disabled).toBe(false)
    expect(bouton('Relancer').disabled).toBe(true)
    expect(bouton('Relancer').title).toBe('« Relancer » ne s’applique pas aux lignes cochées : il ne vaut que pour les lignes « À relancer », « En cours »')
    fireEvent.click(bouton('Désactiver'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Désactiver » sur 1 ligne : aucune n’est modifiée dans la base.')
    // « Désactiver » ne vaut que pour « Relancé » : cochee avec Gamma, Alpha (« À relancer ») n'est pas visee.
    fireEvent.click(bouton('Décocher'))
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    fireEvent.click(gamma)
    expect(container.querySelector('.rig-eg-lot__compte')!.textContent).toBe('2 sélectionnées')
    fireEvent.click(bouton('Relancer'))
    expect(annonce()).toContain('« Relancer » sur 1 ligne')
    fireEvent.click(bouton('Désactiver'))
    expect(annonce()).toContain('« Désactiver » sur 1 ligne')
    // « Tout cocher » prend les trois lignes : chacune a un geste qui vaut pour son etat.
    fireEvent.click(bouton('Décocher'))
    fireEvent.click(container.querySelector('input[aria-label="Tout cocher"]')!)
    expect(container.querySelector('.rig-eg-lot__compte')!.textContent).toBe('3 sélectionnées')
  })

  // Lot 8 (26/11/2026), mesure sur le bureau cache : une relance faite sortait des 200 lignes lues, et la recherche, qui ne
  // filtrait que ces lignes, ne la retrouvait pas. Comme les autres formes (useRecherche) : le texte part dans la base.
  it('la recherche part dans la base quand un lecteur de la file sait chercher ; ses lignes cherchees ne sont pas refiltrees', async () => {
    const trouvee: LigneEcranVue = { nom: 'SANS LE MOT', precisions: 'Relance 1 du 26/11/2026', groupe: 'Fond', etat: 'Validee', faites: [0], id: '164779' }
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [], recherche: true }),
      'EcranGenere.Chercher': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [trouvee], exemples: [], recherche: true, cherchees: ['Fond'] }),
    })
    const { container } = render(<EcranGenere descripteur={descripteur()} />)
    const o = outils(container)
    await waitFor(() => expect(o.noms()).toHaveLength(2))
    expect(container.textContent).toContain('La recherche et les critères cherchent aussi dans la base du greffe.')
    fireEvent.change(container.querySelector('.rig-lm-cherche__saisie')!, { target: { value: 'bunbon' } })
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', 'bunbon', {}))
    await waitFor(() => expect(o.noms()).toEqual(['SANS LE MOT']))
  })

  // Lot 8 (26/11/2026) : l'injonction de RIG v2 (PROC_ISURV) exige l'etape « Cabinet et decision » -- le cabinet, le juge, la date
  // prevue. Les Parametres d'une file se saisissent a cote de la liste et partent avec le geste en lot, par leur libelle ; une file
  // sans parametre n'a pas ce panneau.
  // fix-ok: mesure le 26/11/2026 -- les lignes du test portent un Id (sans Id, un geste ne part pas a la base : Demonstration), et
  // l'acces lignesDeLaBase[0]! satisfait TypeScript strict (compiler.ps1 : TS2532 sur aEnjoindre[0]) ; rouge sans la retouche de FormeFile.
  it('les saisies de l’action (Parametres) partent avec le geste en lot ; une file sans parametre n’en montre aucune', async () => {
    const aEnjoindre: LigneEcranVue[] = lignesDeLaBase.map((l, k) => ({ ...l, id: `dossier:${k + 1}` }))
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: aEnjoindre, exemples: [] }),
      'EcranGenere.Geste': () => ({ fait: true, message: 'Traitements effectués : 1 injonction.', sansEcrivain: false, ids: ['9'] }),
    })
    const injonctions = descripteur({ valider: 'Générer les injonctions', parametres: [champ('Date du cabinet', 'Date'), champ('Cabinet')] })
    const { container } = render(<EcranGenere descripteur={injonctions} />)
    const o = outils(container)
    await waitFor(() => expect(o.noms()).toHaveLength(2))
    const panneau = container.querySelector('aside.rig-criteres')!
    expect(panneau.textContent).toContain('Saisies de l’action')
    fireEvent.change(panneau.querySelector('input[aria-label="Date du cabinet"]')!, { target: { value: '2099-01-15' } })
    fireEvent.change(panneau.querySelector('input[aria-label="Cabinet"]')!, { target: { value: 'CB18' } })
    fireEvent.click(o.ligne(lignesDeLaBase[0]!.nom).querySelector('input[type=checkbox]')!)
    fireEvent.click(o.bouton('Générer les injonctions'))
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Fond', 'Agir', 'Générer les injonctions',
      ['dossier:1'], { 'Date du cabinet': '2099-01-15', Cabinet: 'CB18' }))
    await waitFor(() => expect(o.annonce()).toBe('Traitements effectués : 1 injonction.'))
    cleanup()
    retirerFauxPont()
    installerFauxPont({ 'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] }) })
    const sans = render(<EcranGenere descripteur={descripteur()} />).container
    await waitFor(() => expect(outils(sans).noms()).toHaveLength(2))
    expect(sans.querySelector('aside.rig-criteres')).toBeNull()
  })
})

describe('Forme Fiche : retrouver, corriger, Enregistrer', () => {
  it('une ligne ouverte montre un formulaire aux vrais champs (texte, date, liste, case), sans escalier', async () => {
    const { container, ligne, bouton } = await monter(descripteur({ forme: 'Fiche' }))
    fireEvent.click(ligne('Alpha SARL'))
    expect(container.querySelector('input[aria-label="Code affaire"]')!.getAttribute('type')).toBe('text')
    expect(container.querySelector('input[aria-label="Date de l’audience"]')!.getAttribute('type')).toBe('date')
    expect(container.querySelector('select[aria-label="Sens"]')!.textContent).toBe('—AccepteRejette')
    expect(container.querySelector('input[aria-label="Aide juridictionnelle"]')!.getAttribute('type')).toBe('checkbox')
    expect(bouton('Enregistrer')).toBeDefined()
    expect(bouton('Annuler')).toBeDefined()
    sansEscalier(container)
  })

  // Une fiche CORRIGE un objet qui existe : un champ laisse vide garde la valeur de la base (le pied le dit). Une section
  // obligatoire n'y manque donc jamais -- la regle de la creation (Saisie) la redemandait : « Corriger une affaire »
  // ouverte sur une affaire repondait « Instance est obligatoire » (controle final du lot 1, 25/11/2026).
  it('Enregistrer : un champ vide garde la valeur de la base, rien n’est redemande ; puis « Demonstration »', async () => {
    const { container, ligne, bouton, annonce } = await monter(descripteur({ forme: 'Fiche' }))
    fireEvent.click(ligne('Alpha SARL'))
    fireEvent.click(bouton('Enregistrer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Enregistrer » : aucun champ n’est modifié, « Alpha SARL » garde les valeurs de la base.')
    expect(annonce()).not.toContain('obligatoire')
    fireEvent.change(container.querySelector('input[aria-label="Code affaire"]')!, { target: { value: '2026J00201' } })
    fireEvent.click(bouton('Enregistrer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Enregistrer » : « Alpha SARL » n’est pas modifiée dans la base, la fiche reste ouverte.')
    expect((container.querySelector('input[aria-label="Code affaire"]') as HTMLInputElement).value).toBe('2026J00201')
  })

  it('Annuler revient aux resultats ; une ligne figee s’ouvre en lecture seule', async () => {
    const { container, ligne, bouton, noms } = await monter(descripteur({ forme: 'Fiche' }))
    fireEvent.click(ligne('Gamma SARL'))
    // fix-ok: mesure (vitest, TypeError sur .disabled de null) -- une fiche figee se lit (dl), elle ne pose aucun champ desactive.
    expect(container.querySelectorAll('.rig-eg-sections input, .rig-eg-sections select')).toHaveLength(0)
    expect(container.querySelector('.rig-eg-note')!.textContent).toBe('Fiche figée (Relancé) : elle s’ouvre en lecture seule.')
    expect(bouton('Enregistrer').disabled).toBe(true)
    fireEvent.click(bouton('Annuler'))
    expect(noms()).toHaveLength(3)
  })

  it('un ecran reuni : chaque ligne s’ouvre sur les sections de SA procedure', async () => {
    const { container, ligne } = await monter(reuni('Fiche'))
    fireEvent.click(ligne('Delta SAS'))
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('Relances · Sans réponse · Exercice 2025')
  })

  it('les criteres de la fiche RIG v2 filtrent les resultats', async () => {
    const { container, noms } = await monter(descripteur({ forme: 'Fiche' }))
    fireEvent.change(container.querySelector('aside input[aria-label="Numéro"]')!, { target: { value: 'Rôle 2' } })
    expect(noms()).toEqual(['Bêta SAS'])
  })
})

describe('Forme Consultation : une fiche en lecture seule', () => {
  it('la fiche ouverte n’a aucun champ de saisie ; ses gestes disent « Demonstration »', async () => {
    const { container, ligne, bouton, annonce } = await monter(descripteur({ forme: 'Consultation', gestes: ['Exporter'] }))
    fireEvent.click(ligne('Bêta SAS'))
    expect(container.querySelectorAll('.rig-eg-sections input, .rig-eg-sections select, .rig-eg-sections textarea')).toHaveLength(0)
    expect([...container.querySelectorAll('.rig-eg-lecture dt')].map((t) => t.textContent)).toContain('Code affaire')
    fireEvent.click(bouton('Exporter'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Exporter » : « Bêta SAS ».')
  })

  // Ecart releve a la reception du lot 2 (plan-f4fb097f) : « Exporter » des Listes Excel posait une demande jamais executee, aucun
  // fichier. RIG v2 ECRIT le fichier sur le poste (OPE_LISTE.Exporter, OPE_LISTE.cs:125-136 : « exportListeExcel_<type>.sql ») : le
  // geste rend ce fichier (ResultatGeste.Fichier), que l'ecran enregistre sur le poste, au nom et au texte exacts.
  // Entree qui le fait echouer si la correction est fausse : un resultat fait AVEC un fichier (sans telechargement, aucun clic).
  // fix-ok: pas un correctif a l'aveugle -- trois tests NEUFS ou recrits pour le lot 16 (deux ecarts mesures a la reception des lots 2 et
  // 4), chacun vu rouge sur le code d'avant (vitest : « Chèque : Journal… Espèces : Journal… », puis « expected [] » sans telechargement).
  it('un geste fait qui rend un fichier l’enregistre sur le poste (nom et texte exacts) ; sans fichier, rien n’est telecharge', async () => {
    const d = descripteur({ forme: 'Consultation', gestes: ['Exporter'] })
    const lue: LigneEcranVue = { nom: 'Liste des instances par évènement futur', precisions: '', groupe: 'Instances', etat: 'Validee', faites: [], id: '3200101' }
    const script = "IF NOT EXISTS(SELECT PRMLS_VALEUR FROM PARAMETRAGE_LISTE WHERE PRMLS_VALEUR = 'IEF') \r\nINSERT INTO PARAMETRAGE_LISTE (PRMLS_VALEUR) VALUES ('IEF')\r\n"
    let avecFichier = true
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [lue], exemples: [] }),
      'EcranGenere.Geste': () => ({ fait: true, message: avecFichier ? 'Fichier exporté : exportListeExcel_IEF.sql.' : 'Fait, sans fichier.',
                                    sansEcrivain: false, ids: ['9'], fichier: avecFichier ? { nom: 'exportListeExcel_IEF.sql', contenu: script } : null }),
    })
    const recu: { blob?: Blob } = {}
    const creer = vi.fn((b: Blob) => { recu.blob = b; return 'blob:export' })
    Object.assign(URL, { createObjectURL: creer, revokeObjectURL: vi.fn() })
    const noms: string[] = []
    const clic = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { noms.push(this.download) })
    try {
      const { container } = render(<EcranGenere descripteur={d} />)
      const o = outils(container)
      await waitFor(() => expect(o.noms()).toEqual(['Liste des instances par évènement futur']))
      fireEvent.click(o.ligne('Liste des instances par évènement futur'))
      fireEvent.click(o.bouton('Exporter'))
      await waitFor(() => expect(o.annonce()).toBe('Fichier exporté : exportListeExcel_IEF.sql.'))
      expect(noms).toEqual(['exportListeExcel_IEF.sql'])
      expect(await recu.blob!.text()).toBe(script)
      avecFichier = false
      fireEvent.click(o.bouton('Exporter'))
      await waitFor(() => expect(o.annonce()).toBe('Fait, sans fichier.'))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toHaveLength(2)
      expect(creer).toHaveBeenCalledTimes(1)
      expect(noms).toEqual(['exportListeExcel_IEF.sql'])
    } finally {
      clic.mockRestore()
    }
  })
})

describe('Forme Saisie : creer en etapes, aux vrais champs', () => {
  const saisie = () => descripteur({ forme: 'Saisie', marches: descripteur().sections, sections: [], valider: 'Enregistrer l’instance' })
  it('la frise montre les etapes ; chacune porte ses champs ; Creer controle les etapes obligatoires', async () => {
    const { container, bouton, annonce } = await monter(saisie())
    expect([...container.querySelectorAll('.rig-frise__libelle')].map((e) => e.textContent)).toEqual(['Instance', 'Parties', 'Décision'])
    fireEvent.click(bouton('Suivant'))
    expect(container.querySelector('input[aria-label="Aide juridictionnelle"]')).not.toBeNull()
    fireEvent.click(bouton('Créer'))
    expect(annonce()).toBe('Contrôle de la fiche RIG v2 : « Instance » est obligatoire. Démonstration : rien n’est enregistré.')
    expect(container.querySelector('input[aria-label="Code affaire"]')).not.toBeNull()
    fireEvent.change(container.querySelector('input[aria-label="Code affaire"]')!, { target: { value: 'X' } })
    fireEvent.click(bouton('Créer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Créer » : rien n’est créé dans la base, la saisie reste à l’écran.')
  })
})

describe('Forme Referentiel : une grille editable', () => {
  const ref = () => descripteur({
    forme: 'Referentiel', colonnes: [champ('Code'), champ('Libellé'), champ('Actif', 'Case')],
    lignes: [
      { nom: 'Espèces', precisions: '', groupe: 'Fond', etat: 'Validee', faites: [0], valeurs: ['ESP', 'Espèces', 'oui'] },
      { nom: 'Chèque', precisions: 'CHQ · remise en banque', groupe: 'Fond', etat: 'Validee', faites: [0] },
    ],
  })
  it('les colonnes de la fiche, les cellules de chaque ligne (sans valeurs : le nom, puis ses precisions)', async () => {
    const { container } = await monter(ref())
    expect([...container.querySelectorAll('thead th')].map((t) => t.textContent)).toEqual(['Code', 'Libellé', 'Actif', ''])
    const rangees = [...container.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].slice(0, -1).map((td) => td.textContent))
    expect(rangees).toEqual([['ESP', 'Espèces', 'Oui'], ['Chèque', 'CHQ · remise en banque']])
  })
  // Controle final du lot 1 (25/11/2026, conv-2142 msg-878e557c) : les lecteurs de la base (Serveur/Socle/EcransGeneres/
  // Lecteur*.cs) ne rendent que nom, precisions, groupe et etat (Contrats/EcransGeneres/LignesEcran.cs, LigneLue) ; le
  // pont les recopie avec des valeurs vides (Pont/EcranGenere.cs). « Mandataires » branche sur la base montrait le nom
  // puis « — » partout : l'abrege, le SIREN et le CNBF, visibles avant les formes, ne restaient qu'au survol.
  it('une ligne lue dans la base (sans cellules) montre son nom puis ses precisions, jamais des colonnes de « — »', async () => {
    const d = descripteur({ forme: 'Referentiel', colonnes: [champ('Désignation'), champ('Abrégé'), champ('SIREN'), champ('Actif', 'Case')] })
    const lue = { nom: 'Me Irène Ardoise', precisions: 'abrégé ARD · SIREN 412 000 001 · CNBF 7788', groupe: 'Fond', etat: 'Validee', faites: [0], valeurs: [] }
    const { container } = await monter(d, vueExemple(d, { mode: 'base', greffe: '3801', lignes: [lue], exemples: [] }))
    const cases = [...container.querySelector('tbody tr')!.querySelectorAll('td')].slice(0, -1)
    expect(cases.map((td) => td.textContent)).toEqual(['Me Irène Ardoise', 'abrégé ARD · SIREN 412 000 001 · CNBF 7788'])
    expect(cases[1]!.colSpan).toBe(3)
    // Une seule colonne : les precisions suivent le nom dans la meme case.
    const seule = descripteur({ forme: 'Referentiel', colonnes: [champ('Désignation')] })
    cleanup()
    const r = await monter(seule, vueExemple(seule, { mode: 'base', greffe: '3801', lignes: [lue], exemples: [] }))
    expect(r.container.querySelector('tbody tr td')!.textContent).toBe('Me Irène Ardoise abrégé ARD · SIREN 412 000 001 · CNBF 7788')
  })
  it('Ajouter ouvre une ligne de champs ; Enregistrer dit « Demonstration » et la ligne reste a l’ecran', async () => {
    const { container, bouton, annonce } = await monter(ref())
    fireEvent.click(bouton('Ajouter'))
    const saisies = container.querySelectorAll('tr.rig-eg-ligne--edition input')
    expect(saisies).toHaveLength(3)
    fireEvent.change(saisies[0]!, { target: { value: 'VIR' } })
    fireEvent.click(bouton('Enregistrer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Enregistrer » : la ligne n’est pas ajoutée à la table.')
    expect((container.querySelector('tr.rig-eg-ligne--edition input') as HTMLInputElement).value).toBe('VIR')
    fireEvent.click(bouton('Annuler'))
    expect(container.querySelector('tr.rig-eg-ligne--edition')).toBeNull()
  })
  // Controle final du lot 14 (27/11/2026) : « Dates de travail » offrait « Ajouter » (une demonstration) alors que la V2
  // n'ajoute ni ne supprime de date (ETP_CATEGORIE.cs:10-12), et « Cabinets » offrait « Supprimer », absent de PROC_CABINET.
  it('sans ajout ni suppression V2 (descripteur.ajout / suppression faux), ni « Ajouter » ni « Supprimer » ; par defaut, les deux', async () => {
    const actions = (container: HTMLElement) => {
      fireEvent.click(container.querySelector('tbody tr .rig-lm-menu')!)
      return [...container.querySelectorAll('[role=menuitem]')].map((b) => b.textContent)
    }
    const fige = await monter({ ...ref(), ajout: false, suppression: false })
    expect(fige.bouton('Ajouter')).toBeUndefined()
    expect(actions(fige.container)).toEqual(['Modifier'])
    cleanup()
    const libre = await monter(ref())
    expect(libre.bouton('Ajouter')).toBeDefined()
    expect(actions(libre.container)).toEqual(['Modifier', 'Supprimer'])
  })
  it('un double-clic met la ligne en modification, avec ses valeurs', async () => {
    const { container } = await monter(ref())
    fireEvent.doubleClick(container.querySelector('tbody tr')!)
    expect([...container.querySelectorAll('tr.rig-eg-ligne--edition input[type=text]')].map((i) => (i as HTMLInputElement).value)).toEqual(['ESP', 'Espèces'])
    // Ses cellules sont dans les champs : aucun rappel de precisions.
    expect(container.querySelector('tr.rig-eg-edition-lu')).toBeNull()
  })
  // Controle final du lot 1 (25/11/2026, 4e passage) : une ligne lue sans cellules, ouverte en modification (double-clic
  // ou menu « Modifier »), s'ouvrait en [nom, '', ...] : sur « Mandataires », Abrege et SIREN vides, et ses precisions
  // ne se voyaient plus nulle part. Elles restent affichees, en lecture, sous les champs.
  it('une ligne lue dans la base, ouverte en modification, garde ses precisions visibles en lecture', async () => {
    const d = descripteur({ forme: 'Referentiel', colonnes: [champ('Désignation'), champ('Abrégé'), champ('SIREN')] })
    const lue = { nom: 'Me Irène Ardoise', precisions: 'abrégé ARD · SIREN 412 000 001 · CNBF 7788', groupe: 'Fond', etat: 'Validee', faites: [0], valeurs: [] }
    const { container, bouton } = await monter(d, vueExemple(d, { mode: 'base', greffe: '3801', lignes: [lue], exemples: [] }))
    fireEvent.doubleClick(container.querySelector('tbody tr')!)
    expect((container.querySelector('tr.rig-eg-ligne--edition input') as HTMLInputElement).value).toBe('Me Irène Ardoise')
    const lu = container.querySelector('tr.rig-eg-edition-lu')
    expect(lu?.textContent).toContain('abrégé ARD · SIREN 412 000 001 · CNBF 7788')
    expect(lu?.textContent).toContain('pas encore lues une à une')
    expect(lu?.querySelector('input')).toBeNull()
    fireEvent.click(bouton('Annuler'))
    expect(container.querySelector('tr.rig-eg-edition-lu')).toBeNull()
  })
})

describe('Forme Etat : parametres, tableau, totaux', () => {
  const etat = () => descripteur({
    forme: 'Etat', gestes: ['Éditer'], groupes: ['Espèces', 'Chèque'],
    parametres: [champ('Période', 'Periode'), champ('Caissier')],
    colonnes: [champ('Encaissement'), champ('Date', 'Date'), champ('Montant', 'Montant')],
    lignes: [
      { nom: 'E1', precisions: '', groupe: 'Espèces', etat: 'Validee', faites: [], valeurs: ['E1', '2026-03-12', '180.00'] },
      { nom: 'E2', precisions: '', groupe: 'Chèque', etat: 'Validee', faites: [], valeurs: ['E2', '2026-03-13', '950.00'] },
      { nom: 'E3', precisions: '', groupe: 'Espèces', etat: 'Validee', faites: [], valeurs: ['E3', '2026-03-20', '42.50'] },
    ],
  })
  const pied = (c: HTMLElement) => c.querySelector('.rig-lm-pied')!.textContent!.replace(/ /g, ' ')
  it('le pied totalise la colonne montant, par groupe puis en tout', async () => {
    const { container } = await monter(etat())
    expect(pied(container)).toContain('Espèces 222,50 €')
    expect(pied(container)).toContain('Chèque 950,00 €')
    expect(pied(container)).toContain('Total montant 1 172,50 €')
  })
  it('la periode borne le tableau sur sa colonne date, et les totaux suivent', async () => {
    const { container } = await monter(etat())
    fireEvent.change(container.querySelector('input[aria-label="Période : au"]')!, { target: { value: '2026-03-13' } })
    expect(container.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(pied(container)).toContain('Total montant 1 130,00 €')
  })
  it('le geste de la fiche ne produit rien et le dit', async () => {
    const { bouton, annonce } = await monter(etat())
    fireEvent.click(bouton('Éditer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Éditer » : aucun état n’est produit (3 lignes à l’écran).')
  })
  // Le meme defaut sur un Etat branche sur la base (« Bordereaux de lettres recommandées », LecteurBordereauxLr.cs).
  it('une ligne lue dans la base (sans cellules) montre son nom puis ses precisions, et le total reste « — »', async () => {
    const d = etat()
    const lue = { nom: 'Bordereau 2026-014', precisions: 'édité le 03/03/2026 · 12 plis', groupe: 'Espèces', etat: 'Validee', faites: [], valeurs: [] }
    const { container } = await monter(d, vueExemple(d, { mode: 'base', greffe: '3801', lignes: [lue], exemples: [] }))
    const cases = [...container.querySelector('tbody tr')!.querySelectorAll('td')]
    expect(cases.map((td) => td.textContent)).toEqual(['Bordereau 2026-014', 'édité le 03/03/2026 · 12 plis'])
    expect(cases[1]!.colSpan).toBe(2)
    expect(pied(container)).toContain('Total montant —')
  })
})

describe('Formes Audience, Document, Operation', () => {
  it('Audience : la date et la chambre se choisissent, le geste vise les affaires cochees', async () => {
    const { container, ligne, bouton, annonce } = await monter(descripteur({
      forme: 'Audience', gestes: ['Renvoyer'], parametres: [champ('Date d’audience', 'Date'), champ('Chambre')] }))
    expect(container.querySelector('aside input[aria-label="Date d’audience"]')!.getAttribute('type')).toBe('date')
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    fireEvent.click(bouton('Renvoyer'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Renvoyer » sur 1 affaire : le rôle n’est pas modifié.')
  })

  it('Document : Pivoter tourne la page a l’ecran sans rien ecrire ; Enregistrer dit « Demonstration »', async () => {
    const { container, bouton, annonce } = await monter(descripteur({ forme: 'Document', gestes: ['Pivoter', 'Enregistrer le PDF'] }))
    fireEvent.click(bouton('Pivoter'))
    expect(container.querySelector('.rig-eg-page')!.getAttribute('data-angle')).toBe('90')
    expect(annonce()).toBe('Page tournée à l’écran (90°) : le fichier n’est pas modifié.')
    fireEvent.click(bouton('Enregistrer le PDF'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Enregistrer le PDF » : « Alpha SARL » n’est pas modifié.')
  })

  it('Operation : choisir, voir les consequences, confirmer -- trois temps, pas un de plus', async () => {
    const { container, ligne, bouton, annonce } = await monter(descripteur({ forme: 'Operation', valider: 'Supprimer l’instance' }))
    expect([...container.querySelectorAll('.rig-frise__libelle')].map((e) => e.textContent)).toEqual(['Choisir', 'Conséquences', 'Confirmer'])
    expect(bouton('Suivant').disabled).toBe(true)
    fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
    fireEvent.click(bouton('Suivant'))
    fireEvent.change(container.querySelector('input[aria-label="Code affaire"]')!, { target: { value: '2026J00201' } })
    fireEvent.click(bouton('Suivant'))
    expect(container.querySelector('.rig-eg-recap')!.textContent).toContain('Alpha SARL')
    fireEvent.click(bouton('Supprimer l’instance'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Supprimer l’instance » sur 1 objet : rien n’est modifié dans la base.')
  })

  // PROC_IA (controle final du lot 1, 25/11/2026) : la fiche RIG v2 RECOIT une liasse de la visionneuse de ses pieces
  // (OPE_DOC_DEMAT.cs:818) et se joue en deux temps, lancement puis restitution (proc_ia.md:151-156). Aucune liste : la
  // liasse se designe par son numero ; le resultat ne vient qu'apres le lancement, et tout de suite pour une liasse deja
  // analysee, qui n'a plus de bouton (PROC_IA.cs:60-64, ETP_ENTETE.cs:36-37).
  describe('Operation sur UN objet recu : son numero, le lancement, puis le resultat', () => {
    const lignes = [
      { nom: '2026-001', precisions: 'Liasse CFE · analysée', groupe: 'Fond', etat: 'Validee', faites: [0] },
      { nom: '2026-002', precisions: 'Liasse CFE · à analyser', groupe: 'Fond', etat: 'AFaire', faites: [] },
    ]
    const ia = () => descripteur({
      forme: 'Operation', valider: 'Lancer l’analyse', objetUnique: true, criteres: ['Numéro de liasse'], lignes,
      sections: [{ titre: 'Pièces envoyées', consigne: 'Les PDF partent.', obligatoire: false, champs: [champ('Pièces', 'Tableau')] }],
      resultat: [{ titre: 'Synthèse', consigne: 'Indice de confiance.', obligatoire: false, champs: [champ('Indice', 'Tableau')] }],
    })
    const saisir = (container: HTMLElement, numero: string) =>
      fireEvent.change(container.querySelector('input[aria-label="Numéro de liasse"]')!, { target: { value: numero } })
    const section = (container: HTMLElement, titre: string) => container.querySelector(`section[aria-label="${titre}"]`)
    const temps = (container: HTMLElement, libelle: string) => [...container.querySelectorAll<HTMLButtonElement>('.rig-frise__bouton')]
      .find((b) => b.querySelector('.rig-frise__libelle')?.textContent === libelle)!

    it('aucune liste : ni ligne ni case a cocher, un seul champ, le numero', async () => {
      const { container } = await monter(ia())
      expect(container.querySelector('tbody tr')).toBeNull()
      expect(container.querySelector('input[type=checkbox]')).toBeNull()
      expect(container.querySelectorAll('input:not([type=checkbox])')).toHaveLength(1)
      expect(container.querySelector('input[aria-label="Numéro de liasse"]')).not.toBeNull()
    })

    it('a analyser : d’abord le lancement, confirme ; le resultat reste ferme', async () => {
      const { container, bouton, annonce } = await monter(ia())
      saisir(container, '2026-002')
      expect(section(container, 'Pièces envoyées')).not.toBeNull()
      expect(section(container, 'Synthèse')).toBeNull()
      expect(temps(container, 'Résultat').disabled).toBe(true)
      fireEvent.click(bouton('Lancer l’analyse'))
      expect(container.querySelector('[role=alertdialog]')!.textContent).toContain('Lancer l’analyse sur « 2026-002 » ?')
      expect(annonce()).toBeNull()
      fireEvent.click(bouton('Confirmer'))
      expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Lancer l’analyse » sur « 2026-002 » : rien n’est modifié dans la base.')
      expect(section(container, 'Synthèse')).toBeNull()
    })

    it('deja analysee : elle s’ouvre sur son resultat, sans bouton de lancement', async () => {
      const { container, bouton } = await monter(ia())
      saisir(container, '2026-001')
      expect(section(container, 'Synthèse')).not.toBeNull()
      expect(section(container, 'Pièces envoyées')).toBeNull()
      expect(bouton('Lancer l’analyse')).toBeUndefined()
      fireEvent.click(bouton('Précédent'))
      expect(section(container, 'Pièces envoyées')).not.toBeNull()
      expect(bouton('Lancer l’analyse')).toBeUndefined()
    })

    it('un numero que rien ne porte le dit, sans rien ouvrir', async () => {
      const { container, annonce } = await monter(ia())
      saisir(container, '2026-999')
      expect(annonce()).toBe('Aucune ligne ne porte le numéro « 2026-999 ».')
      expect(section(container, 'Pièces envoyées')).toBeNull()
    })
  })

  // Controle final du lot 1 (25/11/2026, capture de l'Analyse IA) : au temps « Confirmer », dont le recapitulatif est
  // court, les trois temps tombaient au pied de la carte sous un grand vide. La frise horizontale partagee se colle au BAS
  // de sa carte (.rig-frise--h, margin-top: auto, rig-accueil.css, faite pour l'escalier). Entree qui ferait echouer ce
  // test : une feuille ou la frise de l'Operation garde ce margin-top: auto.
  it('Operation : au temps « Confirmer », les trois temps restent en tete, pas colles au pied sous un vide', async () => {
    const retirer = poserFeuilles()
    try {
      const { container, ligne, bouton } = await monter(descripteur({ forme: 'Operation', valider: 'Lancer l’analyse' }))
      fireEvent.click(ligne('Alpha SARL').querySelector('input[type=checkbox]')!)
      fireEvent.click(bouton('Suivant'))
      fireEvent.change(container.querySelector('input[aria-label="Code affaire"]')!, { target: { value: '2026J00201' } })
      fireEvent.click(bouton('Suivant'))
      expect(container.querySelector('.rig-eg-recap')).not.toBeNull()
      expect(cascade(container.querySelector('ol[aria-label="Temps de l’opération"]')!, 'margin-top')).not.toBe('auto')
      // Le jumeau : la frise horizontale partagee, hors des formes, reste collee au bas de sa carte (reference intacte).
      const partagee = document.createElement('ol')
      partagee.className = 'rig-frise rig-frise--h'
      document.body.appendChild(partagee)
      expect(cascade(partagee, 'margin-top')).toBe('auto')
      partagee.remove()
    } finally { retirer() }
  })
})

// jsdom ne dessine rien : ce qu'une capture montrerait (une marge) se calcule sur les feuilles de wwwroot/index.html,
// posees dans son ordre. wwwroot/ est hors de la racine de Vite, qui refuse de l'importer : lu sur le disque, comme
// ListeModerne.test.tsx. Le dessin lui-meme se prouve par la capture du bureau cache.
function poserFeuilles(): () => void {
  const styles = ['rig-accueil.css', 'rig-listes.css', 'rig-ecrans-generes.css'].map((nom) => {
    const style = document.createElement('style')
    style.textContent = readFileSync(join(import.meta.dirname, '../../../wwwroot', nom), 'utf8')
    document.head.appendChild(style)
    return style
  })
  return () => styles.forEach((s) => s.remove())
}

/** Specificite d'un selecteur de nos feuilles (id, puis classe / attribut / pseudo-classe, puis type), en un nombre. */
function specificite(selecteur: string): number {
  const s = selecteur.replace(/:where\([^()]*\)/g, '').replace(/:(?:not|is)\(([^()]*)\)/g, ' $1')
  const ids = (s.match(/#[\w-]+/g) ?? []).length
  const classes = (s.match(/\.[\w-]+|\[[^\]]*\]|:(?!:)[\w-]+/g) ?? []).length
  const types = (s.replace(/\[[^\]]*\]/g, '').match(/(?:^|[\s>+~])[a-z][\w-]*/gi) ?? []).length
  return ids * 10000 + classes * 100 + types
}

/** La valeur que la cascade donne a <propriete> sur <el> : specificite, puis ordre des feuilles (hors @media). */
function cascade(el: Element, propriete: string): string {
  let gagnant = { poids: -1, valeur: '' }
  for (const feuille of [...document.styleSheets]) {
    for (const regle of [...feuille.cssRules]) {
      if (!('selectorText' in regle) || !('style' in regle)) continue
      const valeur = (regle as CSSStyleRule).style.getPropertyValue(propriete)
      if (!valeur) continue
      for (const s of (regle as CSSStyleRule).selectorText.split(',')) {
        let vise = false
        try { vise = el.matches(s) } catch { /* un pseudo-element (::before) ne vise pas l'element */ }
        if (vise && specificite(s) >= gagnant.poids) gagnant = { poids: specificite(s), valeur }
      }
    }
  }
  return gagnant.valeur
}

// Un ecran reuni EN ONGLETS par ses formes (CatalogueGenere.Reunir) passe par l'aiguillage comme un ecran reuni par le
// menu : la vue du C# porte ses onglets et l'onglet montre (Pont/PlanTravailVue.cs, VueOngletsReunis), aucun descripteur
// de liste. Avant le 25/11/2026 l'onglet se tenait ici et partait du premier : « Livre de repertoire » cherche montrait
// le journal de caisse (controle final du lot 1).
describe('Ecran reuni en onglets : un onglet par procedure, chacune dans sa forme', () => {
  it('l’onglet demande par le C# est montre dans sa forme ; un clic renvoie l’onglet au C#', async () => {
    const d = reuni('File', true)
    const [corriger, relances] = [descripteur({ forme: 'Fiche', titre: 'Corriger' }), { ...d.membres[1]!, forme: 'File' as const }]
    const pont = installerFauxPont({ 'EcranGenere.Lignes': (code: unknown) => vueExemple([corriger, relances].find((m) => m.code === code) ?? d) })
    const plan = vi.fn<GestePlan>()
    const vue = proc({ code: d.code, libelle: d.titre, ecran: { ...proc().ecran, descripteur: null, reunis: {
      onglets: [{ code: corriger.code, libelle: 'Corriger', descripteur: corriger }, { code: relances.code, libelle: 'Relances', descripteur: relances }],
      actif: relances.code } } })
    const { container } = render(<Aiguillage proc={vue} plan={plan} />)
    await waitFor(() => expect(container.querySelector('.rig-eg-note')).not.toBeNull())
    expect([...container.querySelectorAll('[role=tab]')].map((t) => [t.textContent, t.getAttribute('aria-selected')]))
      .toEqual([['Corriger', 'false'], ['Relances', 'true']])
    expect(container.querySelector('[role=tabpanel]:not([hidden]) [data-forme]')!.getAttribute('data-forme')).toBe('File')
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Lignes', 'RELANCE_TEST')
    fireEvent.click([...container.querySelectorAll('[role=tab]')][0]!)
    expect(plan).toHaveBeenCalledWith('ongletReuni', 'ECRAN_TEST')
  })
})

describe('EcranGenere : lignes de la base ou exemples', () => {
  const nomsExemple = ['Alpha SARL', 'Bêta SAS', 'Gamma SARL']
  const provenance = (container: HTMLElement) => container.querySelector('.rig-eg-note')!.textContent
  const mentions = (container: HTMLElement) => container.querySelectorAll('.rig-tcw__exemple').length

  it('branche sur la base : les vraies lignes, aucun nom fictif du descripteur, et la provenance dite', async () => {
    const d = descripteur()
    const { container, noms, tuiles, pont } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] })
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Lignes', 'ECRAN_TEST')
    expect(noms()).toEqual(['HKO DUPRAT SARL · SIREN 412 000 001', 'XQN NEGOCE SAS · SIREN 412 000 002'])
    expect(noms().some((n) => nomsExemple.includes(n!))).toBe(false)
    expect(tuiles().map((t) => t[1])).toEqual(['1', '0', '1'])
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
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune ligne dans la base du greffe 3801.')
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
    await waitFor(() => expect(container.querySelector('.rig-eg-note')).not.toBeNull())
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
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Lecture des lignes…')
    await act(async () => { repondre({ mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] }) })
    expect(noms()).toHaveLength(2)
  })

  it('un ecran reuni dont une procedure n\'a pas de lecteur : ses lignes restent des exemples, sa procedure le dit', async () => {
    const d = reuni()
    const reelle = { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'Fond · reçu le 02/03/2026', groupe: 'Ecran test', etat: 'AFaire', faites: [] }
    const exemple = d.lignes.filter((l) => l.groupe === 'Relances')
    const { container, noms, ligne } = await monter(d, { mode: 'mixte', message: null, greffe: '3801', lignes: [reelle, ...exemple], exemples: ['Relances'] })
    expect(noms()).toEqual(['HKO DUPRAT SARL · SIREN 412 000 001', 'Delta SAS'])
    expect(provenance(container)).toBe('Lignes de la base du greffe 3801, sauf les groupes marqués exemple.')
    expect(ligne('HKO DUPRAT SARL · SIREN 412 000 001').querySelector('.rig-tcw__exemple')).toBeNull()
    expect(ligne('Delta SAS').querySelector('.rig-tcw__exemple')).not.toBeNull()
  })

  it('un groupe branche mais sans ligne reste visible, avec son titre, sans « exemple », et dit « Aucune ligne »', async () => {
    const d = reuni()
    const reelle = { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'Fond · reçu le 02/03/2026', groupe: 'Ecran test', etat: 'AFaire', faites: [] }
    const { container, noms } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: [reelle], exemples: [], vides: ['Relances'] })
    expect(noms()).toEqual(['HKO DUPRAT SARL · SIREN 412 000 001'])
    const groupes = [...container.querySelectorAll('.rig-tcw__groupe')]
    expect(groupes.map((g) => g.querySelector('.rig-tcw__groupe-titre')!.textContent)).toEqual(['Ecran test', 'Relances'])
    expect(groupes[1]!.querySelector('.rig-tcw__groupe-nombre')!.textContent).toBe('0')
    expect(groupes[1]!.querySelector('.rig-tcw__vide-groupe')!.textContent).toBe('Aucune ligne dans la base du greffe 3801.')
    expect(container.querySelectorAll('.rig-tcw__exemple')).toHaveLength(0)
  })

  // Cas vu le 25/11/2026 (plan-f7ba4de2, lot 2) : « Envois et retours », ses 4 procedures branchees et vides sur la
  // copie de dev. Chaque groupe reste a l'ecran avec son titre, aucun message unique ne les remplace.
  it('tous les groupes branches et vides : chacun reste visible avec « Aucune ligne », sans message unique', async () => {
    const d = reuni()
    const { container } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: [], exemples: [], vides: ['Ecran test', 'Relances'] })
    const groupes = [...container.querySelectorAll('.rig-tcw__groupe')]
    expect(groupes.map((g) => g.querySelector('.rig-tcw__groupe-titre')!.textContent)).toEqual(['Ecran test', 'Relances'])
    expect(groupes.map((g) => g.querySelector('.rig-tcw__vide-groupe')?.textContent)).toEqual(
      ['Aucune ligne dans la base du greffe 3801.', 'Aucune ligne dans la base du greffe 3801.'])
    expect(container.querySelector('.rig-table__vide')).toBeNull()
  })

  it('un groupe vide disparait quand une recherche ou un etat filtre la liste', async () => {
    const d = reuni()
    const reelle = { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'Fond · reçu le 02/03/2026', groupe: 'Ecran test', etat: 'AFaire', faites: [] }
    const { container } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: [reelle], exemples: [], vides: ['Relances'] })
    fireEvent.change(container.querySelector('input')!, { target: { value: 'HKO' } })
    expect(container.querySelectorAll('.rig-tcw__groupe')).toHaveLength(1)
  })

  it('en mode mixte, les tuiles et la synthese ne comptent que les lignes de la base, pas les exemples', async () => {
    const d = reuni()
    const reelle = { nom: 'HKO DUPRAT SARL · SIREN 412 000 001', precisions: 'Fond · reçu le 02/03/2026', groupe: 'Ecran test', etat: 'AFaire', faites: [] }
    const exemple = d.lignes.filter((l) => l.groupe === 'Relances') // 1 ligne fictive, a l'etat AFaire
    const { container, tuiles } = await monter(d, { mode: 'mixte', message: null, greffe: '3801', lignes: [reelle, ...exemple], exemples: ['Relances'] })
    expect(tuiles()[0]).toEqual(['À traiter', '1'])
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('1 dossiers · 1 en exemple')
  })

  it('un redessin de la racine (descripteur neuf, meme procedure) ne relit pas la base et garde la fiche ouverte', async () => {
    const d = descripteur({ forme: 'Fiche' })
    const { container, pont, rendu, ligne } = await monter(d, { mode: 'base', message: null, greffe: '3801', lignes: lignesDeLaBase, exemples: [] })
    fireEvent.click(ligne('HKO DUPRAT SARL · SIREN 412 000 001'))
    rendu.rerender(<EcranGenere descripteur={{ ...d, lignes: [...d.lignes] }} />)
    await act(async () => {})
    expect(pont.appels).toHaveBeenCalledTimes(1)
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('HKO DUPRAT SARL · SIREN 412 000 001')
  })
})

// LE SOCLE FONCTIONNEL (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) : une ligne de la base (avec son id) ouvre
// SA fiche lue, Enregistrer n'envoie que les champs changes a l'ecrivain, puis l'ecran relit fiche et lignes ; un refus de
// RIG v2 garde la saisie avec son message ; une instance verrouillee s'ouvre en lecture seule avec le message de RIG v2 ;
// sans ecrivain, la demonstration. Le faux pont joue le C# (Pont/EcranGenere.cs).
describe('socle fonctionnel : fiche lue, geste ecrit puis relu', () => {
  const instance: DescripteurEcranVue = descripteur({
    forme: 'Fiche', titre: 'Instance judiciaire', groupes: ['Au fond'],
    sections: [
      { titre: 'Instance', consigne: '', obligatoire: false,
        champs: [champ('Numéro de rôle'), champ('Saisine tribunal', 'Date'), champ('Note sur l’affaire', 'Zone')] },
      { titre: 'Parties', consigne: '', obligatoire: false, champs: [champ('Demandeurs', 'Tableau')] },
      { titre: 'Facturation', consigne: '', obligatoire: false, champs: [champ('Articles', 'Tableau')] },
    ],
  })
  const ligneBase = (): LigneEcranVue =>
    ({ nom: 'DUPONT c/ MARTIN', precisions: 'Rôle 2026J01201', groupe: 'Au fond', etat: 'AFaire', faites: [], id: '1200001' })
  const fiche = (saisine: string, partiel: Record<string, unknown> = {}) => ({
    mode: 'base', message: null,
    fiche: {
      procedure: 'ECRAN_TEST', id: '1200001', nom: 'DUPONT c/ MARTIN', lectureSeule: false, motif: null,
      sections: [
        { titre: 'Instance', champs: [
          { libelle: 'Numéro de rôle', valeur: '2026J01201', modifiable: false, colonnes: [], lignes: [] },
          { libelle: 'Saisine tribunal', valeur: saisine, modifiable: true, colonnes: [], lignes: [] },
          { libelle: 'Note sur l’affaire', valeur: '', modifiable: false, colonnes: [], lignes: [] },
        ] },
        { titre: 'Parties', champs: [
          { libelle: 'Demandeurs', valeur: '', modifiable: false, colonnes: ['Désignation'], lignes: [['M. DUPONT']] },
        ] },
      ],
      ...partiel,
    },
  })

  async function ouvrir(reponses: Record<string, (...args: unknown[]) => unknown>) {
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [ligneBase()], exemples: [] }),
      ...reponses,
    })
    const rendu = render(<EcranGenere descripteur={instance} />)
    const o = outils(rendu.container)
    await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
    fireEvent.click(o.ligne('DUPONT c/ MARTIN'))
    await waitFor(() => expect(rendu.container.textContent).toContain('Fiche lue dans la base du greffe 3801.'))
    return { ...o, pont }
  }
  const saisine = (container: HTMLElement) => container.querySelector('input[aria-label="Saisine tribunal"]') as HTMLInputElement | null

  it('la fiche lue : chaque champ porte sa valeur, seul le champ que l’ecrivain enregistre se saisit, un champ non lu le dit', async () => {
    const { container, pont } = await ouvrir({ 'EcranGenere.Fiche': () => fiche('2026-03-10') })
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Fiche', 'ECRAN_TEST', 'Au fond', '1200001')
    expect(saisine(container)!.value).toBe('2026-03-10')
    expect(container.querySelector('input[aria-label="Numéro de rôle"]')).toBeNull()
    expect(container.querySelector('[aria-label="Numéro de rôle"]')!.textContent).toBe('2026J01201')
    expect(container.querySelector('table[aria-label="Demandeurs"]')!.textContent).toContain('M. DUPONT')
    expect(container.querySelector('section[aria-label="Facturation"]')!.textContent).toContain('Non lu dans la base par la V3')
  })

  it('Enregistrer n’envoie que le champ change ; ecrit, la fiche et les lignes sont relues dans la base', async () => {
    let enBase = '2026-03-10'
    const { container, pont, bouton, annonce } = await ouvrir({
      'EcranGenere.Fiche': () => fiche(enBase),
      'EcranGenere.Geste': (...args: unknown[]) => {
        enBase = (args[5] as Record<string, string>)['Instance › Saisine tribunal']!
        return { fait: true, message: 'Instance 2026J01201 enregistrée dans la base du greffe : « Saisine tribunal ».', sansEcrivain: false, ids: ['1200001'] }
      },
    })
    fireEvent.change(saisine(container)!, { target: { value: '2026-02-01' } })
    fireEvent.click(bouton('Enregistrer'))
    await waitFor(() => expect(annonce()).toBe('Instance 2026J01201 enregistrée dans la base du greffe : « Saisine tribunal ».'))
    // Le succes garde role="status", sans badge « Refus » (jumeau du refus ci-dessous).
    expect(container.querySelector('.rig-eg-annonce')!.getAttribute('role')).toBe('status')
    expect(container.querySelector('.rig-eg-annonce .rig-etat--alerte')).toBeNull()
    expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Au fond', 'Enregistrer', null, ['1200001'],
      { 'Instance › Saisine tribunal': '2026-02-01' })
    await waitFor(() => expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Fiche')).toHaveLength(2))
    expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Lignes')).toHaveLength(2)
    await waitFor(() => expect(saisine(container)!.value).toBe('2026-02-01'))
  })

  it('un controle de RIG v2 refuse : son message, la saisie reste, rien n’est relu', async () => {
    const motif = 'La date de saisine du Tribunal doit être inférieure ou égale à la date d’enrôlement.'
    const { container, pont, bouton, annonce } = await ouvrir({
      'EcranGenere.Fiche': () => fiche('2026-03-10'),
      'EcranGenere.Geste': () => ({ fait: false, message: motif, sansEcrivain: false, ids: [] }),
    })
    fireEvent.change(saisine(container)!, { target: { value: '2027-01-01' } })
    fireEvent.click(bouton('Enregistrer'))
    // Un refus ne ressemble pas a un succes : badge « Refus » et role="alert" (EcranCourriels.tsx, EcranComptes.tsx).
    await waitFor(() => expect(annonce()).toBe(`Refus ${motif}`))
    const note = container.querySelector('.rig-eg-annonce')!
    expect(note.getAttribute('role')).toBe('alert')
    expect(note.querySelector('.rig-etat.rig-etat--alerte')?.textContent).toBe('Refus')
    expect(saisine(container)!.value).toBe('2027-01-01')
    expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Lignes')).toHaveLength(1)
  })

  it('sans ecrivain : la demonstration, jamais un faux succes', async () => {
    const { container, bouton, annonce } = await ouvrir({
      'EcranGenere.Fiche': () => fiche('2026-03-10'),
      'EcranGenere.Geste': () => ({ fait: false, message: 'Aucun écrivain', sansEcrivain: true, ids: [] }),
    })
    fireEvent.change(saisine(container)!, { target: { value: '2026-02-01' } })
    fireEvent.click(bouton('Enregistrer'))
    await waitFor(() => expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Enregistrer » : « DUPONT c/ MARTIN » n’est pas modifiée dans la base, la fiche reste ouverte.'))
  })

  it('une instance verrouillee par un autre utilisateur s’ouvre en lecture seule, avec le message de RIG v2', async () => {
    const motif = 'Instance bloquée (26001200001), demande "En cours" : MJUD par M. AUTRE'
    const { container, bouton } = await ouvrir({ 'EcranGenere.Fiche': () => fiche('2026-03-10', { lectureSeule: true, motif }) })
    expect(container.textContent).toContain(`Fiche en lecture seule : ${motif}.`)
    expect(saisine(container)).toBeNull()
    expect(bouton('Enregistrer').disabled).toBe(true)
  })

  // Mesure sur le bureau cache (26/11/2026) : apres Enregistrer, la ligne relue passe « Modifiée » (Validee) ; la fiche se
  // rouvrait figee, alors que RIG v2 remodifie une instance. Fiche lue : la base decide (seul son verrou fige).
  it('une ligne « Modifiée » dont la fiche est libre dans la base reste modifiable', async () => {
    installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [{ ...ligneBase(), etat: 'Validee' }], exemples: [] }),
      'EcranGenere.Fiche': () => fiche('2026-03-10'),
    })
    const rendu = render(<EcranGenere descripteur={instance} />)
    const o = outils(rendu.container)
    await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
    fireEvent.click(o.ligne('DUPONT c/ MARTIN'))
    await waitFor(() => expect(saisine(rendu.container)).not.toBeNull())
    expect(o.bouton('Enregistrer').disabled).toBe(false)
    expect(rendu.container.textContent).not.toContain('Fiche figée')
  })

  it('la recherche part dans la base quand le lecteur sait chercher ; ses lignes ne sont pas refiltrees a l’ecran', async () => {
    const trouvee: LigneEcranVue = { nom: 'SANS LE MOT', precisions: 'Rôle 2026J09999', groupe: 'Au fond', etat: 'AFaire', faites: [], id: '1200099' }
    const pont = installerFauxPont({
      'EcranGenere.Lignes': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [ligneBase()], exemples: [], recherche: true }),
      'EcranGenere.Chercher': () => ({ mode: 'base', message: null, greffe: '3801', lignes: [trouvee], exemples: [], recherche: true, cherchees: ['Au fond'] }),
    })
    const { container } = render(<EcranGenere descripteur={instance} />)
    const o = outils(container)
    await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
    expect(container.textContent).toContain('La recherche et les critères cherchent aussi dans la base du greffe.')
    fireEvent.change(container.querySelector('input[aria-label="Numéro"]')!, { target: { value: '9999' } })
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', '', { 'Numéro': '9999' }))
    await waitFor(() => expect(o.noms()).toEqual(['SANS LE MOT']))
  })
})

// LES CINQ FORMES (controle final du lot 2, 26/11/2026) : la recherche dans la base et la fiche lue n'etaient branchees
// que sur Consultation et Fiche ; Referentiel, Audience et Operation filtraient leurs lignes deja lues et ne lisaient
// aucune fiche. Un lot d'ecran (audiences, referentiels...) qui pose IRechercheEcran / IFicheEcran sur son lecteur doit
// voir son ecran chercher et lire, sans toucher aux formes. fix-ok: mesure (26/11/2026) -- les trois formes remises a
// leur version d'avant (commit 14f3478), ces 7 tests sortent rouges (7 failed | 59 passed) ; branchees, 66/66.
describe('socle fonctionnel : recherche et fiche sur Referentiel, Audience et Operation', () => {
  const base = (lignes: LigneEcranVue[], partiel: Partial<VueLignesEcran> = {}): VueLignesEcran =>
    ({ mode: 'base', message: null, greffe: '3801', lignes, exemples: [], ...partiel })
  const lu = (libelle: string, valeur: string, modifiable = false) => ({ libelle, valeur, modifiable, colonnes: [], lignes: [] })
  const ficheDe = (id: string, nom: string, sections: { titre: string; champs: ReturnType<typeof lu>[] }[], partiel: Record<string, unknown> = {}) =>
    ({ mode: 'base', message: null, fiche: { procedure: 'ECRAN_TEST', id, nom, lectureSeule: false, motif: null, sections, ...partiel } })
  const filtreTexte = (container: HTMLElement) => container.querySelector('input.rig-lm-cherche__saisie') as HTMLInputElement

  // Lot 6 du plan « RIG v3 100 % fonctionnel » : le geste d'un Etat part a l'ecrivain (l'impression des registres judiciaires),
  // avec les lignes affichees. Lot 16 du plan-f4fb097f (28/11/2026) : UN envoi pour toute la procedure, pas un par groupe -- RIG v2
  // edite le resultat en un clic (VALIDER des registres : une demande par registre dans la meme validation, OPE_REGISTRE_ABSTRACT.cs:
  // 113-126 ; EDITER du journal de caisse : un seul etat trie par mode, OPE_METHODETRI.Editer, OPE_METHODETRI.cs:83-109).
  describe('Etat', () => {
    const registres = descripteur({
      forme: 'Etat', titre: 'Registres', gestes: ['Impression'], groupes: ['Répertoire général', 'Injonctions de payer'],
      colonnes: [champ('Rôle'), champ("Date d'acte", 'Date')],
    })
    const ligne = (id: string, groupe: string): LigneEcranVue => ({ nom: `Rôle ${id}`, precisions: '', groupe, etat: 'Validee', faites: [], id })
    it("lu en base, « Impression » envoie toutes les lignes affichees a l'ecrivain en un geste, et dit ce qui est ecrit", async () => {
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([ligne('G-1', 'Répertoire général'), ligne('G-2', 'Répertoire général'), ligne('I-7', 'Injonctions de payer')]),
        'EcranGenere.Geste': () => ({ fait: true, message: '2 registres placés dans la file des éditions.', sansEcrivain: false, ids: ['9', '10'] }),
      })
      const { container } = render(<EcranGenere descripteur={registres} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(3))
      fireEvent.click(o.bouton('Impression'))
      await waitFor(() => expect(o.annonce()).toBe('2 registres placés dans la file des éditions.'))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toEqual(
        [['EcranGenere.Geste', 'ECRAN_TEST', 'Répertoire général', 'Agir', 'Impression', ['G-1', 'G-2', 'I-7'], {}]])
    })

    // Ecart releve a la reception du lot 4 (plan-f4fb097f) : en « Tout », le journal de caisse posait DEUX editions au meme titre
    // (« Chèque : Journal de caisse de la période… » puis « Espèces : … »), une par mode de paiement. RIG v2 n'en pose qu'une, au
    // titre de la periode SAISIE (JournalCaisse.cs:186, dateDebut/dateFin d'OPE_CRITERESGENERAUX) : un geste, toutes les lignes
    // affichees, et les parametres saisis (la periode, le tri) que l'ecrivain lit (EcrivainJournalCaisse.cs).
    // Entree qui le fait echouer si la correction est fausse : deux groupes affiches a la fois (Espèces ET Chèque).
    it('journal de caisse en « Tout » : un seul « Éditer », toutes les lignes affichees, la periode et le tri saisis', async () => {
      const journal = descripteur({
        forme: 'Etat', titre: 'Journal de caisse', gestes: ['Éditer'], groupes: ['Espèces', 'Chèque'],
        colonnes: [champ('Encaissement'), champ('Date', 'Date'), champ('Montant', 'Montant')],
        parametres: [champ('Période', 'Periode'), champ('Tri', 'Liste', ['Par mode de paiement', 'Par date'])],
      })
      const encaissement = (id: string, groupe: string, montant: string): LigneEcranVue =>
        ({ nom: `Encaissement ${id}`, precisions: '', groupe, etat: 'Validee', faites: [], id, valeurs: [`Encaissement ${id}`, '2002-10-29', montant] })
      const titre = 'Journal de caisse de la période du 29/10/2002 au 31/10/2002 : 3 encaissements, 60,00 €, placé dans la file des éditions.'
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([encaissement('152', 'Chèque', '20.00'), encaissement('7', 'Espèces', '10.00'), encaissement('153', 'Chèque', '30.00')]),
        'EcranGenere.Geste': () => ({ fait: true, message: titre, sansEcrivain: false, ids: ['4445300'] }),
      })
      const { container } = render(<EcranGenere descripteur={journal} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(3))
      fireEvent.change(container.querySelector('aside input[aria-label="Période : du"]')!, { target: { value: '2002-10-29' } })
      fireEvent.change(container.querySelector('aside input[aria-label="Période : au"]')!, { target: { value: '2002-10-31' } })
      fireEvent.change(container.querySelector('aside select')!, { target: { value: 'Par date' } })
      fireEvent.click(o.bouton('Éditer'))
      await waitFor(() => expect(o.annonce()).toBe(titre))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toEqual(
        [['EcranGenere.Geste', 'ECRAN_TEST', 'Chèque', 'Agir', 'Éditer', ['152', '7', '153'], { 'Période': '2002-10-29|2002-10-31', Tri: 'Par date' }]])
    })

    // Lot 13 du plan « RIG v3 100 % fonctionnel » : un geste ECRIT sur un Etat (« Lettrer » du lettrage) change les chiffres de la
    // base ; l'Etat les relit, comme une File. Mesure sur le bureau cache (27/11/2026) : « 6 lettrages écrits » s'affichait, et la
    // ligne restait a 0,00 € tant qu'on ne retouchait pas la recherche.
    it('un geste ecrit relit les lignes : les cellules et les totaux suivent la base', async () => {
      const lettrage = descripteur({
        forme: 'Etat', titre: 'Lettrage', gestes: ['Lettrer'], groupes: ['Compte hors PC'],
        colonnes: [champ('Client'), champ('Facturé', 'Montant')],
      })
      let facture = '0.00'
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([{ nom: 'Client A', precisions: '', groupe: 'Compte hors PC', etat: 'AFaire', faites: [], id: 'C-1',
                                            valeurs: ['Client A', facture] }]),
        'EcranGenere.Geste': () => { facture = '120.00'; return { fait: true, message: '2 lettrages écrits pour 1 client.', sansEcrivain: false, ids: ['C-1'] } },
      })
      const { container } = render(<EcranGenere descripteur={lettrage} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelector('tbody tr')?.textContent).toContain('0,00'))
      fireEvent.click(o.bouton('Lettrer'))
      await waitFor(() => expect(container.querySelector('tbody tr')?.textContent).toContain('120,00'))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Lignes')).toHaveLength(2)
    })

    // RIG v2 imprime le registre ENTIER avec le total de sa requete (OPE_RESULTATS.cs:35-39 et :65-73, « ALL » ;
    // OPE_REGISTRE_GENERAL.cs:221) : une liste coupee au plafond du serveur n'est pas le registre, son titre compterait faux.
    it('liste coupee au plafond : « Impression » refuse et ne part pas, le message dit pourquoi', async () => {
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([ligne('G-1', 'Répertoire général'), ligne('G-2', 'Répertoire général'), ligne('I-7', 'Injonctions de payer')],
          { message: 'Les 3 premières lignes sur 1194.', plafonnees: ['Répertoire général', 'Injonctions de payer'] }),
        'EcranGenere.Geste': () => ({ fait: true, message: 'placé dans la file des éditions.', sansEcrivain: false, ids: ['9'] }),
      })
      const { container } = render(<EcranGenere descripteur={registres} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(3))
      fireEvent.click(o.bouton('Impression'))
      await waitFor(() => expect(container.querySelector('.rig-eg-annonce[role="alert"]')?.textContent)
        .toBe('Refus « Impression » : la liste est coupée (Les 3 premières lignes sur 1194.). RIG v2 imprime toutes les lignes ; '
              + 'RIG v3 ne lit que celles-ci, la période et le filtre ne portent que sur elles : rien n’est enregistré.'))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toHaveLength(0)
    })

    // Controle final du lot 6 (26/11/2026) : l'Etat ne cherchait pas dans la base, et la periode ne filtrait que les lignes
    // chargees. Quand le lecteur cherche (vue.recherche), les PARAMETRES partent a la base -- les criteres de RIG v2, qui relance
    // sa requete a chaque saisie (proc_regjud.md:156-160) -- et la periode ne refiltre pas ce que la base a choisi.
    const parametres = descripteur({
      forme: 'Etat', titre: 'Registres', gestes: ['Impression'], groupes: ['Répertoire général', 'Injonctions de payer'],
      colonnes: [champ('Rôle'), champ("Date d'acte", 'Date')], parametres: [champ("Dates d'acte", 'Periode')],
    })
    it('lecteur qui cherche : les parametres cherchent dans la base, la periode ne refiltre pas ses lignes', async () => {
      // La date d'acte de la V2 (INSTN_DATE_ACTE) n'est pas la date montree (la saisine) : une ligne choisie par la base peut
      // montrer une date hors de la periode saisie.
      const trouvee: LigneEcranVue = { ...ligne('G-9', 'Répertoire général'), valeurs: ['2025R00009', '2025-03-05'] }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([ligne('G-1', 'Répertoire général')], { recherche: true }),
        'EcranGenere.Chercher': () => base([trouvee], { recherche: true, cherchees: ['Répertoire général', 'Injonctions de payer'] }),
      })
      const { container } = render(<EcranGenere descripteur={parametres} />)
      await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(1))
      expect(container.textContent).toContain('La recherche et les critères cherchent aussi dans la base du greffe.')
      fireEvent.change(container.querySelector('aside input[aria-label="Dates d\'acte : du"]')!, { target: { value: '2025-03-10' } })
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', '', { "Dates d'acte": '2025-03-10|' }))
      await waitFor(() => expect(container.querySelector('tbody tr td')?.textContent).toBe('2025R00009'))
    })

    it('lecteur qui cherche, registre coupe : le refus propose de resserrer les parametres ; un registre complet s’imprime par son onglet', async () => {
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([ligne('G-1', 'Répertoire général'), ligne('I-7', 'Injonctions de payer')],
          { recherche: true, message: 'Répertoire général : les 1 premières lignes sur 1194.', plafonnees: ['Répertoire général'] }),
        'EcranGenere.Geste': () => ({ fait: true, message: 'Registre des 1 décisions des IP : placé dans la file des éditions.', sansEcrivain: false, ids: ['9'] }),
      })
      const { container } = render(<EcranGenere descripteur={parametres} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelectorAll('tbody tr')).toHaveLength(2))
      fireEvent.click(o.bouton('Impression'))
      await waitFor(() => expect(container.querySelector('.rig-eg-annonce[role="alert"]')?.textContent)
        .toBe('Refus « Impression » : la liste est coupée (Répertoire général : les 1 premières lignes sur 1194.). RIG v2 imprime toutes '
              + 'les lignes ; resserrez les paramètres, qui cherchent dans la base, pour qu’elles soient toutes à l’écran : rien n’est enregistré.'))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toHaveLength(0)
      // Jumeau : l'onglet du registre complet n'envoie que ses lignes, et part.
      fireEvent.click([...container.querySelectorAll('button.rig-lm-vue')].find((b) => b.textContent?.startsWith('Injonctions de payer'))!)
      fireEvent.click(o.bouton('Impression'))
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Injonctions de payer', 'Agir', 'Impression', ['I-7'], {}))
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Geste')).toHaveLength(1)
    })
  })

  describe('Referentiel', () => {
    const ref = descripteur({
      forme: 'Referentiel', titre: 'Modes de paiement', groupes: ['Fond'],
      colonnes: [champ('Code'), champ('Libellé'), champ('Actif', 'Case')],
      sections: [{ titre: 'Mode', consigne: '', obligatoire: false, champs: [champ('Code'), champ('Libellé'), champ('Actif', 'Case'), champ('Compte comptable')] }],
    })
    const cheque: LigneEcranVue = { nom: 'Chèque', precisions: 'CHQ', groupe: 'Fond', etat: 'Validee', faites: [], id: '1200501' }
    const fiche = (partiel: Record<string, unknown> = {}) => ficheDe('1200501', 'Chèque',
      [{ titre: 'Mode', champs: [lu('Code', 'CHQ'), lu('Libellé', 'Chèque', true), lu('Actif', 'oui', true), lu('Compte comptable', '512000')] }], partiel)
    const edition = (container: HTMLElement) => container.querySelector('tr.rig-eg-ligne--edition:not(.rig-eg-edition-lu)')!

    it('une ligne de la base ouverte en modification lit sa fiche ; Modifier n’envoie que la colonne changee, puis relit', async () => {
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([cheque]),
        'EcranGenere.Fiche': () => fiche(),
        'EcranGenere.Geste': () => ({ fait: true, message: 'Mode « Chèque » modifié dans la base du greffe.', sansEcrivain: false, ids: ['1200501'] }),
      })
      const { container } = render(<EcranGenere descripteur={ref} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelector('tbody tr td')?.textContent).toBe('Chèque'))
      fireEvent.doubleClick(container.querySelector('tbody tr')!)
      await waitFor(() => expect((edition(container).querySelector('input[aria-label="Libellé"]') as HTMLInputElement).value).toBe('Chèque'))
      expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Fiche', 'ECRAN_TEST', 'Fond', '1200501')
      // Code ne s'enregistre pas : lu, pas saisi ; la fiche entiere se lit dessous (Compte comptable, hors colonnes).
      expect(edition(container).querySelector('input[aria-label="Code"]')).toBeNull()
      expect(edition(container).querySelector('[aria-label="Code"]')!.textContent).toBe('CHQ')
      expect(container.querySelector('tr.rig-eg-edition-lu')!.textContent).toContain('512000')
      fireEvent.change(edition(container).querySelector('input[aria-label="Libellé"]')!, { target: { value: 'Chèque bancaire' } })
      fireEvent.click(o.bouton('Enregistrer'))
      await waitFor(() => expect(o.annonce()).toBe('Mode « Chèque » modifié dans la base du greffe.'))
      expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Geste', 'ECRAN_TEST', 'Fond', 'Modifier', null, ['1200501'], { 'Libellé': 'Chèque bancaire' })
      expect(pont.appels.mock.calls.filter((c) => c[0] === 'EcranGenere.Lignes')).toHaveLength(2)
    })

    it('une ligne que RIG v2 verrouille : lecture seule, son message, Enregistrer grise', async () => {
      installerFauxPont({ 'EcranGenere.Lignes': () => base([cheque]), 'EcranGenere.Fiche': () => fiche({ lectureSeule: true, motif: 'Mode utilisé par une demande en cours' }) })
      const { container } = render(<EcranGenere descripteur={ref} />)
      const o = outils(container)
      await waitFor(() => expect(container.querySelector('tbody tr td')?.textContent).toBe('Chèque'))
      fireEvent.doubleClick(container.querySelector('tbody tr')!)
      await waitFor(() => expect(container.textContent).toContain('Ligne en lecture seule : Mode utilisé par une demande en cours.'))
      expect(edition(container).querySelector('input')).toBeNull()
      expect(o.bouton('Enregistrer').disabled).toBe(true)
    })

    it('la recherche part dans la base quand le lecteur sait chercher ; ses lignes ne sont pas refiltrees', async () => {
      const virement: LigneEcranVue = { nom: 'Virement', precisions: 'VIR', groupe: 'Fond', etat: 'Validee', faites: [], id: '1200502' }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([cheque], { recherche: true }),
        'EcranGenere.Chercher': () => base([virement], { recherche: true, cherchees: ['Fond'] }),
      })
      const { container } = render(<EcranGenere descripteur={ref} />)
      await waitFor(() => expect(container.querySelector('tbody tr td')?.textContent).toBe('Chèque'))
      expect(container.textContent).toContain('La recherche et les critères cherchent aussi dans la base du greffe.')
      fireEvent.change(filtreTexte(container), { target: { value: 'compte 512' } })
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', 'compte 512', {}))
      await waitFor(() => expect(container.querySelector('tbody tr td')?.textContent).toBe('Virement'))
    })
  })

  describe('Audience', () => {
    const aud = descripteur({
      forme: 'Audience', titre: 'Retour d’audience', groupes: ['Fond'], gestes: ['Renvoyer'],
      parametres: [champ('Date d’audience', 'Date'), champ('Chambre')],
    })
    const affaire: LigneEcranVue = { nom: 'DUPONT c/ MARTIN', precisions: 'Rôle 2026J01201', groupe: 'Fond', etat: 'AFaire', faites: [], id: '1200601' }

    it('les parametres de l’audience et le texte choisissent le role dans la base', async () => {
      const autre: LigneEcranVue = { ...affaire, nom: 'ALPHA c/ BETA', id: '1200602' }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([affaire], { recherche: true }),
        'EcranGenere.Chercher': () => base([autre], { recherche: true, cherchees: ['Fond'] }),
      })
      const { container } = render(<EcranGenere descripteur={aud} />)
      const o = outils(container)
      await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
      fireEvent.change(container.querySelector('aside input[aria-label="Date d’audience"]')!, { target: { value: '2026-03-10' } })
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', '', { 'Date d’audience': '2026-03-10' }))
      // La date ne se lit pas dans la ligne : la base a choisi, l'ecran ne la refiltre pas.
      await waitFor(() => expect(o.noms()).toEqual(['ALPHA c/ BETA']))
    })

    it('une affaire se deplie sur sa fiche lue', async () => {
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([affaire]),
        'EcranGenere.Fiche': () => ficheDe('1200601', 'DUPONT c/ MARTIN', [{ titre: 'Instance', champs: [lu('Code affaire', '2026J01201')] }]),
      })
      const { container, } = render(<EcranGenere descripteur={aud} />)
      const o = outils(container)
      await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
      fireEvent.click(o.ligne('DUPONT c/ MARTIN').querySelector('button[aria-label="Deplier la ligne"]')!)
      await waitFor(() => expect(container.querySelector('.rig-eg-detail [aria-label="Code affaire"]')?.textContent).toBe('2026J01201'))
      expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Fiche', 'ECRAN_TEST', 'Fond', '1200601')
      expect(container.querySelector('.rig-eg-detail section[aria-label="Parties"]')!.textContent).toContain('Non lu dans la base par la V3')
    })
  })

  describe('Operation', () => {
    const op = descripteur({ forme: 'Operation', titre: 'Supprimer une instance', groupes: ['Fond'], valider: 'Supprimer l’instance' })
    const instance: LigneEcranVue = { nom: 'DUPONT c/ MARTIN', precisions: 'Rôle 2026J01201', groupe: 'Fond', etat: 'AFaire', faites: [], id: '1200701' }

    it('la liste cherche dans la base, et un objet se deplie sur sa fiche lue', async () => {
      const trouvee: LigneEcranVue = { ...instance, nom: 'SANS LE MOT', id: '1200702' }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([instance], { recherche: true }),
        'EcranGenere.Chercher': () => base([trouvee], { recherche: true, cherchees: ['Fond'] }),
        'EcranGenere.Fiche': () => ficheDe('1200702', 'SANS LE MOT', [{ titre: 'Instance', champs: [lu('Code affaire', '2026J09999')] }]),
      })
      const { container } = render(<EcranGenere descripteur={op} />)
      const o = outils(container)
      await waitFor(() => expect(o.noms()).toEqual(['DUPONT c/ MARTIN']))
      fireEvent.change(container.querySelector('aside input[aria-label="Numéro"]')!, { target: { value: '9999' } })
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', '', { 'Numéro': '9999' }))
      await waitFor(() => expect(o.noms()).toEqual(['SANS LE MOT']))
      fireEvent.click(o.ligne('SANS LE MOT').querySelector('button[aria-label="Deplier la ligne"]')!)
      await waitFor(() => expect(container.querySelector('.rig-eg-detail [aria-label="Code affaire"]')?.textContent).toBe('2026J09999'))
    })

    it('sur UN objet : son numero se cherche dans la base, hors des lignes deja lues, et sa fiche lue s’affiche', async () => {
      const ia = descripteur({
        forme: 'Operation', valider: 'Lancer l’analyse', objetUnique: true, criteres: ['Numéro de liasse'], groupes: ['Fond'],
        sections: [{ titre: 'Liasse', consigne: '', obligatoire: false, champs: [champ('Forme juridique')] }],
      })
      const lointaine: LigneEcranVue = { nom: '2026-777', precisions: 'Liasse CFE', groupe: 'Fond', etat: 'AFaire', faites: [], id: '1200777' }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([{ ...lointaine, nom: '2026-001', id: '1200701' }], { recherche: true }),
        'EcranGenere.Chercher': () => base([lointaine], { recherche: true, cherchees: ['Fond'] }),
        'EcranGenere.Fiche': () => ficheDe('1200777', '2026-777', [{ titre: 'Liasse', champs: [lu('Forme juridique', 'SARL')] }]),
      })
      const { container } = render(<EcranGenere descripteur={ia} />)
      await waitFor(() => expect(container.querySelector('input[aria-label="Numéro de liasse"]')).not.toBeNull())
      fireEvent.change(container.querySelector('input[aria-label="Numéro de liasse"]')!, { target: { value: '2026-777' } })
      await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Chercher', 'ECRAN_TEST', '', { 'Numéro de liasse': '2026-777' }))
      await waitFor(() => expect(container.querySelector('[aria-label="Forme juridique"]')?.textContent).toBe('SARL'))
      expect(pont.appels).toHaveBeenCalledWith('EcranGenere.Fiche', 'ECRAN_TEST', 'Fond', '1200777')
    })

    // Lot 13 (PROC_XEX, edition interne d'un Kbis) : la section que le descripteur nomme apercuKbis se dessine en EXTRAIT KBIS
    // (composants/ApercuKbis.tsx, calcule par Pont/ApercuKbisDossier.cs sur la fiche lue), pas en champs.
    const xex = () => descripteur({
      forme: 'Operation', valider: 'Éditer le Kbis', objetUnique: true, criteres: ['Numéro de gestion du dossier RCS'], groupes: ['Fond'],
      sections: [{ titre: 'Dossier RCS', consigne: '', obligatoire: true, champs: [champ('Désignation')] },
                 { titre: 'Extrait Kbis', consigne: '', obligatoire: false, champs: [champ('Forme juridique')] }],
      apercuKbis: 'Extrait Kbis',
    })
    const dossierXex: LigneEcranVue = { nom: '2013B60001', precisions: 'KBIS ALPHA', groupe: 'Fond', etat: 'EnCours', faites: [], id: '2300601' }
    const saisirDossier = async (container: HTMLElement) => {
      await waitFor(() => expect(container.querySelector('input[aria-label="Numéro de gestion du dossier RCS"]')).not.toBeNull())
      fireEvent.change(container.querySelector('input[aria-label="Numéro de gestion du dossier RCS"]')!, { target: { value: '2013B60001' } })
    }

    it('sur UN dossier : la section apercuKbis est l’extrait Kbis du dossier lu, a la place de ses champs (lot 13)', async () => {
      const kbis = {
        greffeLibelle: 'Greffe du Tribunal de Commerce de Grenoble', greffeAdresse: '', gestion: '2013B60001', fraicheur: '27/11/2026',
        sections: [{ titre: 'IDENTIFICATION DE LA PERSONNE MORALE', lignes: [{ titre: 'Forme juridique', libelle: 'Société à responsabilité limitée' }] }],
        villeRcs: 'GRENOBLE', horodatage: '27/11/2026 10:00:00', numero: '2013B60001',
      }
      const pont = installerFauxPont({
        'EcranGenere.Lignes': () => base([dossierXex], { recherche: true }),
        'EcranGenere.Chercher': () => base([dossierXex], { recherche: true, cherchees: ['Fond'] }),
        'EcranGenere.Fiche': () => ficheDe('2300601', '2013B60001', [{ titre: 'Dossier RCS', champs: [lu('Désignation', 'KBIS ALPHA')] }]),
        'ApercuKbis.Dossier': () => ({ mode: 'base', message: null, kbis }),
      })
      const { container } = render(<EcranGenere descripteur={xex()} />)
      await saisirDossier(container)
      await waitFor(() => expect(container.querySelector('[aria-label="Extrait Kbis"] .rig-kbis')).not.toBeNull())
      expect(pont.appels).toHaveBeenCalledWith('ApercuKbis.Dossier', 'ECRAN_TEST', 'Fond', '2300601')
      expect(container.querySelector('.rig-kbis')!.textContent).toContain('Société à responsabilité limitée')
      expect(container.querySelector('.rig-kbis')!.textContent).toContain('Numero de gestion 2013B60001')
      // Dans SA feuille A4, comme l'apercu du plan de travail : .rig-kbis s'y place en absolu (rig-accueil.css).
      // fix-ok: mesure a l'ecran le 27/11/2026 (copie de dev, 2001D00693, capture k11) : sans la feuille, l'extrait recouvrait l'ecran.
      expect(container.querySelector('.rig-kbis')!.parentElement!.className).toBe('rig-apercu__feuille rig-apercu__feuille--kbis')
      // Les autres sections gardent leurs champs lus ; celle de l'extrait n'a plus de champ.
      expect(container.querySelector('[aria-label="Désignation"]')?.textContent).toBe('KBIS ALPHA')
      expect(container.querySelector('[aria-label="Forme juridique"]')).toBeNull()
    })

    it('sur UN dossier sans extrait lu (exemple, panne) : le motif, jamais un extrait invente (lot 13)', async () => {
      installerFauxPont({
        'EcranGenere.Lignes': () => base([dossierXex], { recherche: true }),
        'EcranGenere.Chercher': () => base([dossierXex], { recherche: true, cherchees: ['Fond'] }),
        'EcranGenere.Fiche': () => ficheDe('2300601', '2013B60001', []),
        'ApercuKbis.Dossier': () => ({ mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.', kbis: null }),
      })
      const { container } = render(<EcranGenere descripteur={xex()} />)
      await saisirDossier(container)
      await waitFor(() => expect(container.querySelector('[aria-label="Extrait Kbis"]')?.textContent).toContain('La base du greffe 3801 ne répond pas.'))
      expect(container.querySelector('.rig-kbis')).toBeNull()
    })
  })
})
