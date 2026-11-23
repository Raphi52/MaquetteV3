import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { EcranGenere } from './EcranGenere'
import type { DescripteurEcranVue } from '../plan/types'

afterEach(cleanup)

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
    ...partiel,
  }
}

function monter(d: DescripteurEcranVue = descripteur()) {
  const { container } = render(<EcranGenere descripteur={d} />)
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
  it('affiche une tuile par etat avec son compte, et les groupes avec leur nombre', () => {
    const { container, valeurs } = monter()
    expect(valeurs()).toEqual(['1', '1', '1'])
    expect([...container.querySelectorAll('.rig-tcw__groupe-titre')].map((t) => t.textContent)).toEqual(['Fond', 'Référé'])
    expect([...container.querySelectorAll('.rig-tcw__groupe-nombre')].map((t) => t.textContent)).toEqual(['2', '1'])
  })

  it('la pilule filtre la liste a chaque frappe, sans tenir compte de la casse', () => {
    const { container, noms } = monter()
    const champ = container.querySelector<HTMLInputElement>('.rig-recherche__saisie')!
    fireEvent.change(champ, { target: { value: 'bêta' } })
    expect(noms()).toEqual(['Bêta SAS'])
    fireEvent.change(champ, { target: { value: '' } })
    expect(noms()).toHaveLength(3)
  })

  it('une tuile filtre la liste sur son etat ; la presser a nouveau le relache', () => {
    const { container, noms } = monter()
    const tuiles = container.querySelectorAll<HTMLButtonElement>('.rig-tcw__tuile')
    fireEvent.click(tuiles[1]!)
    expect(tuiles[1]!.getAttribute('aria-pressed')).toBe('true')
    expect(noms()).toEqual(['Bêta SAS'])
    fireEvent.click(tuiles[1]!)
    expect(noms()).toHaveLength(3)
  })

  it('franchir les marches fait passer la ligne en cours, et Valider la valide une fois les obligatoires faites', () => {
    const { container, ligne, boutonsMarche, valider, valeurs } = monter()
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

  it('une ligne validee ne se modifie plus : ses marches sont figees', () => {
    const { ligne, boutonsMarche, valider } = monter()
    fireEvent.click(ligne('Gamma SARL'))
    expect(boutonsMarche().every((b) => b.disabled)).toBe(true)
    expect(valider().disabled).toBe(true)
  })

  it('un groupe replie garde le nom de ses lignes en resume, et ses lignes deviennent inertes', () => {
    const { container } = monter()
    fireEvent.click(container.querySelector('.rig-tcw__groupe-tete')!)
    const groupe = container.querySelector('.rig-tcw__groupe')!
    expect(groupe.classList.contains('rig-tcw__groupe--plie')).toBe(true)
    expect(groupe.querySelector('.rig-tcw__groupe-resume')!.textContent).toBe('Alpha SARL · Gamma SARL')
    expect(groupe.querySelector('.rig-tcw__groupe-corps')!.hasAttribute('inert')).toBe(true)
  })
})
