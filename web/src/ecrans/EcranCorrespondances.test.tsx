import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AideTouchesEcran, EcranCorrespondances, RETOUR_A_LA_LIGNE, repond, type LigneCorrespondance, type VueEcranCorrespondances } from './EcranCorrespondances'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La fiche elle-meme (inventaire de la V2, regles de la V3, ecrans qui existent) est construite et prouvee en C#
// (Pont/EcranCorrespondances.cs, tests/SoclePreferencesCheck). Ici : l'ecran la montre, la cherche, ouvre l'ecran
// de la V3 d'une ligne, et l'aide « ? » d'un ecran met l'ancienne touche a cote de la nouvelle.
function ligne(partiel: Partial<LigneCorrespondance> = {}): LigneCorrespondance {
  return {
    numero: 1, ecranV2: 'Réquisitions', ancienneTouche: 'F7', geste: 'Lancer la recherche', nouvelleTouche: 'Entrée',
    statut: 'nouvelle-touche', explication: 'F7 n\'est pas repris : Entrée dans un critère lance la recherche.',
    ecranV3: 'CODE_REQUI', nomEcranV3: 'Requisitions - commande de documents', ouvrable: true, construit: true, source: 'frmREQUI.cs:424',
    ...partiel,
  }
}

function vue(): VueEcranCorrespondances {
  return {
    etatFiche: 'avant-beta', retoursBeta: [],
    lignes: [
      ligne(),
      ligne({ numero: 2, ecranV2: 'Facture', ancienneTouche: 'F10', geste: 'Masquer ou montrer les encaissements repris', nouvelleTouche: null,
              statut: 'supprimee', explication: 'L\'écran de la V3 n\'a pas cette fonction.', ecranV3: 'CODE_FACTURE', nomEcranV3: 'Factures' }),
      ligne({ numero: 3, ecranV2: 'Suivi des preventions', ancienneTouche: 'F2', geste: 'Remplir la saisie automatiquement', nouvelleTouche: null,
              statut: 'a-venir', explication: 'L\'écran n\'est pas encore construit dans la V3.', ecranV3: 'CODE_IPSUIVI',
              nomEcranV3: 'Suivi des preventions', construit: false }),
      ligne({ numero: 4, ecranV2: 'Dossier RCS', ancienneTouche: 'Échap', geste: 'Fermer ou annuler', nouvelleTouche: 'Échap',
              statut: 'identique', explication: 'Échap ferme ce qui est ouvert.', ecranV3: 'DOSSIER', nomEcranV3: 'Page du dossier', ouvrable: false }),
    ],
  }
}

describe('EcranCorrespondances', () => {
  it('SOC-027 CA02 : chaque touche de la V2 a sa nouvelle touche, son geste ou sa raison', async () => {
    installerFauxPont({ 'EcranCorrespondances.Ouvrir': () => vue() })
    render(<EcranCorrespondances ouvrir={() => {}} />)
    expect(await screen.findByText('4 raccourci(s)')).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(4)
    expect(lignes[0]!.textContent).toContain('F7')
    expect(lignes[0]!.textContent).toContain('Entrée')
    expect(lignes[0]!.textContent).toContain('Nouvelle touche')
    expect(lignes[1]!.textContent).toContain('Supprimée')
    expect(lignes[1]!.textContent).toContain('n\'a pas cette fonction')
    expect(lignes[2]!.textContent).toContain('Écran à venir')
    expect(screen.getByText('Avant les retours des greffes bêta')).toBeTruthy()
  })

  it('se lit en entier : aucune cellule coupee, chaque texte revient a la ligne (retour du chef, 21/11/2026)', async () => {
    const longue = 'Un texte d\'explication long, qui ne tient pas sur une ligne de la colonne et doit se lire en entier, jusqu\'au dernier mot.'
    installerFauxPont({ 'EcranCorrespondances.Ouvrir': () => ({ ...vue(), lignes: [ligne({ explication: longue, statut: 'nouveau' })] }) })
    const { container } = render(<EcranCorrespondances ouvrir={() => {}} />)
    await screen.findByText('1 raccourci(s)')
    // Le texte entier est dans la cellule (pas seulement dans une infobulle), et rien ne le coupe.
    expect(screen.getByText(longue).tagName).toBe('TD')
    for (const cellule of container.querySelectorAll<HTMLElement>('.rig-fiche-touches th, .rig-fiche-touches td'))
      expect([cellule.style.textOverflow, cellule.style.overflow, cellule.style.whiteSpace]).toEqual(['', '', ''])
    // La regle de la page : pour les en-tetes, les cellules, les pastilles (« Nouveau dans la V3 ») et les boutons.
    expect(container.querySelector('style')!.textContent).toBe(RETOUR_A_LA_LIGNE)
    expect(RETOUR_A_LA_LIGNE).toMatch(/\.rig-table\.rig-fiche-touches thead th, \.rig-table\.rig-fiche-touches tbody td \{ white-space: normal; overflow: visible; text-overflow: clip; max-width: none;/)
    expect(RETOUR_A_LA_LIGNE).toMatch(/\.rig-fiche-touches \.rig-etat, \.rig-table\.rig-fiche-touches \.rig-bouton \{ white-space: normal;/)
    expect(screen.getByText('Nouveau dans la V3').className).toContain('rig-etat')
  })

  it('se cherche : un mot de l\'ecran, de la touche ou du geste filtre la fiche', async () => {
    installerFauxPont({ 'EcranCorrespondances.Ouvrir': () => vue() })
    render(<EcranCorrespondances ouvrir={() => {}} />)
    await screen.findByText('4 raccourci(s)')
    fireEvent.change(screen.getByLabelText('Chercher une touche, un écran, un geste'), { target: { value: 'f10' } })
    expect(screen.getByText('1 sur 4')).toBeTruthy()
    expect(screen.getAllByRole('row').slice(1)[0]!.textContent).toContain('Facture')
    fireEvent.change(screen.getByLabelText('Chercher une touche, un écran, un geste'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Dans la V3'), { target: { value: 'identique' } })
    expect(screen.getByText('1 sur 4')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Dans la V3'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Chercher une touche, un écran, un geste'), { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun raccourci ne répond à cette recherche.')).toBeTruthy()
  })

  it('repond lit tous les textes de la ligne, sans tenir compte de la casse', () => {
    expect(repond(ligne(), 'requisitions')).toBe(true)
    expect(repond(ligne(), 'ENTRÉE')).toBe(true)
    expect(repond(ligne(), 'nouvelle touche')).toBe(true)
    expect(repond(ligne(), 'kbis')).toBe(false)
    expect(repond(ligne(), '  ')).toBe(true)
  })

  it('SOC-027 CA04 : une ligne ouvre l\'ecran de la V3 ou essayer la touche ; la page du dossier ne s\'ouvre pas sans affaire', async () => {
    installerFauxPont({ 'EcranCorrespondances.Ouvrir': () => vue() })
    const ouvrir = vi.fn()
    render(<EcranCorrespondances ouvrir={ouvrir} />)
    await screen.findByText('4 raccourci(s)')
    fireEvent.click(screen.getByRole('button', { name: 'Requisitions - commande de documents' }))
    expect(ouvrir).toHaveBeenCalledWith('CODE_REQUI')
    expect(screen.queryByRole('button', { name: 'Page du dossier' })).toBeNull()
    expect(screen.getByText('Page du dossier')).toBeTruthy()
  })
})

describe('AideTouchesEcran', () => {
  it('SOC-027 CA03 : « ? » montre les touches de l\'ecran ouvert, l\'ancienne touche a cote ; Echap la ferme', async () => {
    const pont = installerFauxPont({ 'EcranCorrespondances.DeLEcran': () => [ligne()] })
    render(<AideTouchesEcran code="CODE_REQUI" actif />)
    fireEvent.keyDown(document.body, { key: '?' })
    const aide = await screen.findByRole('dialog', { name: 'Touches de cet écran' })
    expect(pont.appels).toHaveBeenCalledWith('EcranCorrespondances.DeLEcran', 'CODE_REQUI')
    const cellules = aide.querySelectorAll('tbody tr td')
    expect(cellules[0]!.textContent).toBe('Entrée')
    expect(cellules[1]!.textContent).toBe('F7')
    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  // L'aide s'ouvre a la reponse du pont (une promesse), pas pendant la frappe : son ecoute d'Echap ne doit pas
  // attendre la fin du rendu. Echap est presse ici a l'instant ou la fenetre entre dans la page ; c'etait la
  // cause du rouge intermittent du test ci-dessus sous la charge de tests.ps1 (chef du plan, 2026-11-21).
  it('Echap ferme l\'aide des qu\'elle s\'affiche', async () => {
    installerFauxPont({ 'EcranCorrespondances.DeLEcran': () => [ligne()] })
    render(<AideTouchesEcran code="CODE_REQUI" actif />)
    const echapAffichage = new Promise<void>((pret) => {
      const veille = new MutationObserver(() => {
        if (!document.querySelector('[role="dialog"]')) return
        veille.disconnect()
        fireEvent.keyDown(document.body, { key: 'Escape' })
        pret()
      })
      veille.observe(document.body, { childList: true, subtree: true })
    })
    fireEvent.keyDown(document.body, { key: '?' })
    await echapAffichage
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull(), { timeout: 300 })
  })

  it('ne repond pas quand l\'ecran est cache, ni dans un champ', () => {
    const pont = installerFauxPont({ 'EcranCorrespondances.DeLEcran': () => [ligne()] })
    const { rerender } = render(<><input aria-label="champ" /><AideTouchesEcran code="CODE_REQUI" actif={false} /></>)
    fireEvent.keyDown(document.body, { key: '?' })
    rerender(<><input aria-label="champ" /><AideTouchesEcran code="CODE_REQUI" actif /></>)
    fireEvent.keyDown(screen.getByLabelText('champ'), { key: '?' })
    expect(pont.appels).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
