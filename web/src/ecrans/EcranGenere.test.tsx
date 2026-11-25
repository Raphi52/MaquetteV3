import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranGenere, type VueLignesEcran } from './EcranGenere'
import type { ChampEcranVue, DescripteurEcranVue, FormeEcranVue, GestePlan, LigneEcranVue, TypeChampVue } from '../plan/types'
import { Aiguillage } from '../plan/Aiguillage'
import { proc } from '../test/plan'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

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
      { nom: 'Chèque', precisions: '', groupe: 'Fond', etat: 'Validee', faites: [0] },
    ],
  })
  it('les colonnes de la fiche, les cellules de chaque ligne (sans valeurs : le nom en premiere colonne)', async () => {
    const { container } = await monter(ref())
    expect([...container.querySelectorAll('thead th')].map((t) => t.textContent)).toEqual(['Code', 'Libellé', 'Actif', ''])
    const rangees = [...container.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].slice(0, 3).map((td) => td.textContent))
    expect(rangees).toEqual([['ESP', 'Espèces', 'Oui'], ['Chèque', '—', '—']])
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
  it('un double-clic met la ligne en modification, avec ses valeurs', async () => {
    const { container } = await monter(ref())
    fireEvent.doubleClick(container.querySelector('tbody tr')!)
    expect([...container.querySelectorAll('tr.rig-eg-ligne--edition input[type=text]')].map((i) => (i as HTMLInputElement).value)).toEqual(['ESP', 'Espèces'])
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

  // PROC_IA (controle final du lot 1, 25/11/2026) : la fiche RIG v2 recoit UNE liasse en parametre (proc_ia.md:192). Une
  // operation sur un objet unique n'en garde qu'un : cocher une autre ligne remplace la premiere, sans « Tout cocher ».
  it('Operation sur UN objet : cocher une autre ligne remplace la premiere, pas de « Tout cocher »', async () => {
    // Deux lignes du MEME groupe : changer de groupe repart deja de zero (une procedure a la fois), ce n'est pas la regle testee.
    const lignes = [
      { nom: 'Liasse 2026-001', precisions: 'CFE', groupe: 'Fond', etat: 'AFaire', faites: [] },
      { nom: 'Liasse 2026-002', precisions: 'CFE', groupe: 'Fond', etat: 'AFaire', faites: [] },
    ]
    const { container, ligne, bouton, annonce } = await monter(descripteur({ forme: 'Operation', valider: 'Lancer l’analyse', objetUnique: true, lignes }))
    expect(container.querySelector('input[aria-label="Tout cocher"]')).toBeNull()
    const [premiere, seconde] = ['Liasse 2026-001', 'Liasse 2026-002']
    fireEvent.click(ligne(premiere).querySelector('input[type=checkbox]')!)
    fireEvent.click(ligne(seconde).querySelector('input[type=checkbox]')!)
    expect([...container.querySelectorAll<HTMLInputElement>('tbody input[type=checkbox]')].filter((c) => c.checked)).toHaveLength(1)
    expect((ligne(seconde).querySelector('input[type=checkbox]') as HTMLInputElement).checked).toBe(true)
    fireEvent.click(bouton('Suivant'))
    fireEvent.change(container.querySelector('input[aria-label="Code affaire"]')!, { target: { value: '2026J00201' } })
    fireEvent.click(bouton('Suivant'))
    expect(container.querySelector('.rig-eg-recap')!.textContent).toContain(seconde)
    expect(container.querySelector('.rig-eg-recap')!.textContent).not.toContain(premiere)
    fireEvent.click(bouton('Lancer l’analyse'))
    expect(annonce()).toBe('Démonstration : rien n’est enregistré — « Lancer l’analyse » sur 1 objet : rien n’est modifié dans la base.')
  })
})

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
