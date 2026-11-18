import { describe, expect, it } from 'vitest'
import { contientSansCasse, dateCourte, distinctsTries, entierLisible, estBlanc } from './criteres'

describe('criteres (gestes communs des ecrans de recherche)', () => {
  it('dateCourte ecrit une date aaaa-mm-jj comme ToString("dd/MM/yyyy")', () => {
    expect(dateCourte('2026-09-02')).toBe('02/09/2026')
    expect(dateCourte('2024-11-08')).toBe('08/11/2024')
  })

  it('contientSansCasse ignore la casse comme OrdinalIgnoreCase, pas l\'ordre des lettres', () => {
    expect(contientSansCasse('SARL BOULANGERIE DUVAL', 'boulang')).toBe(true)
    expect(contientSansCasse('2026J00121', 'j001')).toBe(true)
    expect(contientSansCasse('SCI DU MOULIN', 'moulins')).toBe(false)
  })

  it('estBlanc vaut string.IsNullOrWhiteSpace', () => {
    expect(estBlanc('')).toBe(true)
    expect(estBlanc('  \t')).toBe(true)
    expect(estBlanc(' a ')).toBe(false)
  })

  it('distinctsTries garde chaque valeur une fois, dans l\'ordre fr-FR de Order()', () => {
    expect(distinctsTries(['Mme LEROY', 'M. DUVAL', 'Mme LEROY'])).toEqual(['M. DUVAL', 'Mme LEROY'])
    expect(distinctsTries(['Renvoi', 'Mise en delibere', 'Clos', 'Radiation', 'Renvoi']))
      .toEqual(['Clos', 'Mise en delibere', 'Radiation', 'Renvoi'])
    // Un accent ne renvoie pas le mot apres le Z, comme le ferait un tri par code.
    expect(distinctsTries(['Zebre', 'Été', 'Arbre'])).toEqual(['Arbre', 'Été', 'Zebre'])
  })

  it('entierLisible lit un entier saisi, et rien d\'autre', () => {
    expect(entierLisible('100')).toBe(100)
    expect(entierLisible(' 3 ')).toBe(3)
    expect(entierLisible('')).toBeNull()
    expect(entierLisible('1,5')).toBeNull()
    expect(entierLisible('abc')).toBeNull()
  })
})
