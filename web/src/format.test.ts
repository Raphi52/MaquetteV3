import { describe, expect, it } from 'vitest'
import { centimes, euros, nombre } from './format'

// Valeurs attendues = ce que les ecrans Blazor affichaient (ToString("N2") /
// ("N1") sous fr-FR), relues sur la capture de Factures du 18/11/2026.
describe('format', () => {
  it('ecrit les decimales a la francaise, comme ToString("N2")', () => {
    expect(nombre(37.45, 2)).toBe('37,45')
    expect(nombre(0, 2)).toBe('0,00')
    expect(nombre(8, 2)).toBe('8,00')
    expect(nombre(812.4, 2)).toBe('812,40')
  })

  it('ecrit une decimale pour ToString("N1")', () => {
    expect(nombre(20, 1)).toBe('20,0')
  })

  it('groupe les milliers par l\'espace fine insecable U+202F, comme .NET sous ICU', () => {
    expect(nombre(1234.5, 2)).toBe('1 234,50')
  })

  it('compte en centimes entiers : un solde exactement couvert vaut 0, pas 1e-13', () => {
    const total = [194.85, 162.72, 53.52, 32.9, 14.64].reduce((s, x) => s + centimes(x), 0)
    expect(total).toBe(45863)
    expect(total - centimes(458.63)).toBe(0)
    expect(euros(total)).toBe('458,63')
  })
})
