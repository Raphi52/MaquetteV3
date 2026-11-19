import { describe, expect, it } from 'vitest'
import { COLONNES, ECART, HAUTEUR_LIGNE, deplacer, dimensionner, memePose, pasColonne } from './pose'

// Les bornes de la pose doivent etre CELLES du C# (SessionAccueil.PoserTuile et
// DimensionnerTuile) : colonne dans [1, 13 - largeur], ligne >= 1, largeur dans
// [3, 13 - colonne], hauteur >= 1. Sinon la tuile sauterait au retour de la vue.
const depart = { colonne: 4, ligne: 2, largeur: 6, hauteur: 2 }
const pasLigne = HAUTEUR_LIGNE + ECART

describe('pose des tuiles', () => {
  it('une grille de 12 colonnes : le pas d\'une colonne est sa largeur plus l\'ecart', () => {
    expect(COLONNES).toBe(12)
    expect(pasColonne(12 * 88 + 11 * 12)).toBe(100)
  })

  it('deplacer arrondit a la case la plus proche', () => {
    expect(deplacer(depart, 149, 0, 100)).toEqual({ ...depart, colonne: 5 })
    expect(deplacer(depart, 151, pasLigne * 1.6, 100)).toEqual({ ...depart, colonne: 6, ligne: 4 })
  })

  it('deplacer ne sort jamais de la grille : colonne dans [1, 13 - largeur], ligne >= 1', () => {
    expect(deplacer(depart, -10_000, -10_000, 100)).toEqual({ ...depart, colonne: 1, ligne: 1 })
    expect(deplacer(depart, 10_000, 0, 100).colonne).toBe(13 - depart.largeur)
    expect(deplacer({ ...depart, largeur: 12 }, 500, 0, 100).colonne).toBe(1)
  })

  it('dimensionner : 3 colonnes au moins, jamais au-dela du bord droit, 1 ligne au moins', () => {
    expect(dimensionner(depart, -10_000, -10_000, 100)).toEqual({ ...depart, largeur: 3, hauteur: 1 })
    expect(dimensionner(depart, 10_000, pasLigne * 3, 100)).toEqual({ ...depart, largeur: 13 - depart.colonne, hauteur: 5 })
    expect(dimensionner({ ...depart, colonne: 12, largeur: 3 }, 0, 0, 100).largeur).toBe(3)
  })

  it('memePose compare les quatre grandeurs', () => {
    expect(memePose(depart, { ...depart })).toBe(true)
    expect(memePose(depart, { ...depart, hauteur: 3 })).toBe(false)
  })
})
