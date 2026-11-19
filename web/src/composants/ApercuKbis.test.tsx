import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render } from '@testing-library/react'
import { ApercuKbis, type VueKbis } from './ApercuKbis'
import { morceaux } from '../test/morceaux'

afterEach(cleanup)

// Les valeurs (greffe tire du code saisi, dates, capital N2 fr-FR, sections
// vides masquees) sont calculees par Pont/ApercuKbis.cs, execute par
// tests/PersistanceGedCheck. Ici : la mise en page nationale, a l'identique.
const vue: VueKbis = {
  greffeLibelle: "Greffe du Tribunal de Commerce d'Amiens",
  greffeAdresse: '14 rue Robert de Luzarches 80000 AMIENS',
  gestion: '2026B01067',
  fraicheur: '19/11/2026',
  sections: [
    { titre: 'IDENTIFICATION DE LA PERSONNE MORALE', lignes: [
      { titre: 'Immatriculation au RCS, numero', libelle: '812 445 901 R.C.S. AMIENS' },
      { titre: 'Capital social', libelle: '10 000,00 EUR' },
    ] },
    { titre: 'GESTION, DIRECTION, ADMINISTRATION, CONTROLE, ASSOCIES OU MEMBRES', lignes: [
      { titre: 'Qualite', libelle: 'Gerant' },
    ] },
  ],
  villeRcs: 'AMIENS',
  horodatage: '19/11/2026 08:30:12',
  numero: '004176',
}

describe('ApercuKbis', () => {
  it('rend l\'entete du greffe, le titre national et la fraicheur', () => {
    const { container } = render(<ApercuKbis vue={vue} />)
    const entete = container.querySelector('.rig-kbis > .rig-kbis__entete')!
    expect([...entete.querySelectorAll('.rig-kbis__greffe-libelle')].map((e) => e.textContent))
      .toEqual(["Greffe du Tribunal de Commerce d'Amiens", 'Registre du Commerce et des Societes'])
    expect(entete.querySelector('div:not([class])')!.textContent).toBe('14 rue Robert de Luzarches 80000 AMIENS')
    expect(morceaux(entete.querySelector('.rig-kbis__gestion'))).toEqual(['Numero de gestion ', '2026B01067'])
    expect(container.querySelector('.rig-kbis__kbis')!.textContent).toBe('Extrait Kbis')
    expect(container.querySelector('.rig-kbis__titre')!.textContent)
      .toBe("EXTRAIT D'IMMATRICULATION PRINCIPALE AU REGISTRE DU COMMERCE ET DES SOCIETES")
    expect(morceaux(container.querySelector('.rig-kbis__fraicheur'))).toEqual(['a jour au ', '19/11/2026'])
  })

  it('un greffe sans adresse n\'imprime pas de ligne vide', () => {
    const { container } = render(<ApercuKbis vue={{ ...vue, greffeAdresse: '' }} />)
    expect(container.querySelector('.rig-kbis__entete div:not([class])')).toBeNull()
  })

  it('le corps : colonne des titres, une ligne de section puis ses lignes', () => {
    const { container } = render(<ApercuKbis vue={vue} />)
    const table = container.querySelector('table.rig-kbis__corps')!
    expect(table.querySelector('colgroup col.rig-kbis__col-titre')).not.toBeNull()
    const lignes = [...table.querySelectorAll('tr')]
    expect(lignes.map((tr) => tr.className)).toEqual(['', 'rig-kbis__ligne', 'rig-kbis__ligne', '', 'rig-kbis__ligne'])
    const section = lignes[0]!.querySelector('td')!
    expect(section.className).toBe('rig-kbis__section')
    expect(section.getAttribute('colspan')).toBe('2')
    expect([...lignes[2]!.querySelectorAll('td')].map((td) => [td.className, td.textContent]))
      .toEqual([['rig-kbis__ligne-titre', 'Capital social'], ['', '10 000,00 EUR']])
  })

  it('le bas et le pied : greffier, fin de l\'extrait, R.C.S. horodate et numero en morceaux Razor', () => {
    const { container } = render(<ApercuKbis vue={vue} />)
    expect([...container.querySelectorAll('.rig-kbis__bas > div')].map((e) => [e.className, e.textContent]))
      .toEqual([['rig-kbis__signature', 'Le Greffier'], ['', "FIN DE L'EXTRAIT"]])
    const [gauche, droite] = [...container.querySelectorAll('.rig-kbis__pied > span')]
    expect(morceaux(gauche!)).toEqual(['R.C.S. ', 'AMIENS', ' - ', '19/11/2026 08:30:12', '    ', '004176'])
    expect(droite!.textContent).toBe('page 1/1')
  })
})
