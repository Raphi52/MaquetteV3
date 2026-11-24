import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, ChoixTri, EnteteListe, LigneDetail, ListeModerne, MenuLigne, MontantBarre,
  Pastille, PiedListe, PointEtat, TeintesAvatar, VuesListe, initiales, montant, rangAvatar, texteCsv,
} from './ListeModerne'
import { EcranVisible } from '../plan/visibilite'

afterEach(cleanup)

/** Une cellule du socle se pose dans une vraie ligne de tableau ; la ligne note ses clics. */
function dansUneLigne(cellules: ReactNode, surClic = vi.fn(), surDouble = vi.fn()) {
  return render(<table><tbody><tr onClick={surClic} onDoubleClick={surDouble}>{cellules}</tr></tbody></table>)
}

describe('ListeModerne', () => {
  it('le cadre : la colonne des resultats en carte de surface, nommee', () => {
    render(<ListeModerne etiquette="Affaires"><p>contenu</p></ListeModerne>)
    const cadre = screen.getByRole('region', { name: 'Affaires' })
    expect(cadre.tagName).toBe('SECTION')
    expect(cadre.className).toBe('rig-resultats-proc rig-lm')
    expect(cadre.textContent).toBe('contenu')
  })
})

describe('outils de texte', () => {
  it('initiales : les deux premiers mots qui comptent, sans forme juridique ni mot de liaison (avatars de la maquette)', () => {
    expect(initiales('AMIENS BILLARDS')).toBe('AB')
    expect(initiales('BATIPRO SARL')).toBe('B')
    expect(initiales('SCI DU PARC')).toBe('P')
    expect(initiales('MENUISERIE PICARDE')).toBe('MP')
    expect(initiales('MARTIN EURL')).toBe('M')
    expect(initiales('COMPTOIR DU NORD')).toBe('CN')
    expect(initiales('SOMME LOGISTIQUE')).toBe('SL')
    expect(initiales('LEROY Michel')).toBe('LM')
    expect(initiales('Société Générale')).toBe('SG')
    // Un nom fait SEULEMENT de mots sans initiale en garde quand meme une ; un nom vide, un « ? ».
    expect(initiales('SA')).toBe('S')
    expect(initiales('')).toBe('?')
  })

  it('rangAvatar : une teinte stable par nom, toujours dans la palette declaree', () => {
    const noms = ['AMIENS BILLARDS', 'BATIPRO SARL', 'SCI DU PARC', 'MARTIN EURL', 'COMPTOIR DU NORD', 'X', '']
    for (const nom of noms) {
      const rang = rangAvatar(nom)
      expect(rang).toBeGreaterThanOrEqual(1)
      expect(rang).toBeLessThanOrEqual(TeintesAvatar)
      expect(rangAvatar(nom)).toBe(rang)
    }
    expect(rangAvatar('amiens billards ')).toBe(rangAvatar('AMIENS BILLARDS'))
    expect(new Set(noms.map(rangAvatar)).size).toBeGreaterThan(1)
  })

  it('montant : a la francaise, deux decimales, euro', () => {
    expect(montant(1379.3).replace(/\s/g, ' ')).toBe('1 379,30 €')
    expect(montant(0)).toBe('0,00 €')
  })

  it('texteCsv : point-virgule, guillemets doubles seulement quand il le faut', () => {
    expect(texteCsv(['N°', 'Libelle'], [['004176', 'Modification - SCI "DU" PARC'], ['1', 'a;b']]))
      .toBe('N°;Libelle\r\n004176;"Modification - SCI ""DU"" PARC"\r\n1;"a;b"')
  })
})

describe('EnteteListe', () => {
  it('titre, synthese et champ « Aller a… » qui filtre ; Echap vide le champ sans aller plus loin', () => {
    const changer = vi.fn()
    const valider = vi.fn()
    render(<EnteteListe titre="Affaires" synthese={<>15 resultats</>} detail="7 instance"
                        filtre={{ valeur: 'sci', changer, indication: 'Aller a une affaire…', valider }} />)
    expect(screen.getByText('Affaires').className).toBe('rig-lm-titre')
    expect(screen.getByText('15 resultats').title).toBe('7 instance')
    const champ = screen.getByLabelText('Aller a une affaire…')
    fireEvent.change(champ, { target: { value: 'scib' } })
    expect(changer).toHaveBeenLastCalledWith('scib')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(valider).toHaveBeenCalledTimes(1)
    expect(fireEvent.keyDown(champ, { key: 'Escape' })).toBe(false)
    expect(changer).toHaveBeenLastCalledWith('')
  })

  it('Echap dans un champ deja vide n\'est pas retenu : l\'application le recoit', () => {
    render(<EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />)
    expect(fireEvent.keyDown(screen.getByLabelText('Aller a…'), { key: 'Escape' })).toBe(true)
  })

  it('Ctrl K pose le curseur dans le champ et garde la touche (la palette ne s\'ouvre pas)', () => {
    render(<EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />)
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(screen.getByLabelText('Aller a…'))
  })

  it('ecran cache (onglet non montre) : Ctrl K n\'est ni pris ni detourne', () => {
    render(<EcranVisible.Provider value={false}>
      <EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />
    </EcranVisible.Provider>)
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(false)
    expect(document.activeElement).not.toBe(screen.getByLabelText('Aller a…'))
  })

  it('sans filtre, ni champ ni Ctrl K', () => {
    render(<EnteteListe titre="Factures" />)
    expect(screen.queryByRole('textbox')).toBeNull()
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(false)
  })
})

describe('CartesChiffrees', () => {
  it('clic carte, segment ou legende : chacun son filtre, le segment ne declenche pas la carte', () => {
    const carte = vi.fn()
    const instance = vi.fn()
    const mesure = vi.fn()
    const { container } = render(<CartesChiffrees cartes={[
      { cle: 'solde', titre: 'Solde total', valeur: '1 379,30 €', legende: true, choisir: carte, actif: false, segments: [
        { cle: 'instance', libelle: 'Instance', valeur: 334.3, teinte: 'bleu', texte: '334,30 €', actif: true, choisir: instance },
        { cle: 'mesure', libelle: 'Mesure', valeur: 0, teinte: 'vert', texte: '0,00 €', choisir: mesure },
      ] },
      { cle: 'en-cours', titre: 'En cours', valeur: 12, complement: 'sur 15', anneau: { part: 12, total: 15 } },
    ]} />)

    // Un segment a zero ne se dessine pas (largeur nulle), sa legende reste et filtre.
    const segments = container.querySelectorAll('.rig-lm-segment')
    expect(segments).toHaveLength(1)
    expect(segments[0]!.className).toContain('rig-lm-t-bleu')
    expect(segments[0]!.className).toContain('rig-lm-segment--actif')
    fireEvent.click(segments[0]!)
    expect(instance).toHaveBeenCalledTimes(1)
    expect(carte).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Mesure 0,00 €' }))
    expect(mesure).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Instance 334,30 €', pressed: true })).toBeTruthy()

    fireEvent.click(screen.getByText('Solde total'))
    expect(carte).toHaveBeenCalledTimes(1)
    fireEvent.keyDown(screen.getByText('Solde total').parentElement!, { key: 'Enter' })
    expect(carte).toHaveBeenCalledTimes(2)

    // La carte sans filtre n'est pas un bouton ; son anneau dit 12 sur 15.
    const enCours = screen.getByText('En cours').parentElement!
    expect(enCours.getAttribute('role')).toBeNull()
    expect(enCours.textContent).toBe('En cours12 sur 15')
    const plein = enCours.querySelector('.rig-lm-anneau__plein')!.getAttribute('stroke-dasharray')!
    expect(Number(plein.split(' ')[0]) / Number(plein.split(' ')[1])).toBeCloseTo(12 / 15, 2)
  })
})

describe('VuesListe et ChoixTri', () => {
  it('puces avec compte, puce active, choix d\'une vue', () => {
    const choisir = vi.fn()
    render(<VuesListe vues={[{ cle: 'toutes', libelle: 'Toutes', compte: 15 }, { cle: 'clo', libelle: 'Cloturees', compte: 2 },
                             { cle: 'neuve', libelle: 'Sans compte' }]} active="toutes" choisir={choisir} />)
    expect(screen.getByRole('button', { name: 'Toutes 15', pressed: true })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cloturees 2', pressed: false })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sans compte' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Cloturees 2' }))
    expect(choisir).toHaveBeenCalledWith('clo')
    // Sans « enregistrer », pas de « + Nouvelle vue ».
    expect(screen.queryByText('+ Nouvelle vue')).toBeNull()
  })

  it('« + Nouvelle vue » demande un nom : Entree enregistre, Echap ou un nom vide n\'enregistrent rien', () => {
    const enregistrer = vi.fn()
    render(<VuesListe vues={[]} active={null} choisir={vi.fn()} enregistrer={enregistrer}>
      <button>Exporter</button>
    </VuesListe>)
    expect(screen.getByText('Exporter').parentElement!.className).toBe('rig-lm-outils')

    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Escape' })
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(enregistrer).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.change(screen.getByLabelText('Nom de la nouvelle vue'), { target: { value: '  Mes RCS ' } })
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(enregistrer).toHaveBeenCalledWith('Mes RCS')
    expect(screen.queryByLabelText('Nom de la nouvelle vue')).toBeNull()
  })

  it('« Trier » : l\'ordre d\'origine puis les cles de l\'ecran ; le sens s\'inverse seulement sur une cle', () => {
    const choisir = vi.fn()
    const inverser = vi.fn()
    const { rerender } = render(<ChoixTri cles={[['Nom', 'Nom'], ['Solde', 'Solde']]} cleActive={null} ascendant
                                          choisir={choisir} inverser={inverser} origine="Plus recentes" />)
    const liste = screen.getByLabelText('Trier') as HTMLSelectElement
    expect([...liste.options].map((o) => o.text)).toEqual(['Plus recentes', 'Nom', 'Solde'])
    expect((screen.getByRole('button', { name: /inverser/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(liste, { target: { value: 'Solde' } })
    expect(choisir).toHaveBeenLastCalledWith('Solde')

    rerender(<ChoixTri cles={[['Nom', 'Nom'], ['Solde', 'Solde']]} cleActive="Solde" ascendant={false}
                       choisir={choisir} inverser={inverser} origine="Plus recentes" />)
    fireEvent.click(screen.getByRole('button', { name: 'Ordre decroissant, inverser' }))
    expect(inverser).toHaveBeenCalledTimes(1)
    fireEvent.change(screen.getByLabelText('Trier'), { target: { value: '' } })
    expect(choisir).toHaveBeenLastCalledWith(null)
  })
})

describe('cellules du tableau', () => {
  it('identite, pastille, point d\'etat : avatar d\'initiales a teinte stable, nom gras, objet gris', () => {
    const { container } = dansUneLigne(<>
      <CelluleIdentite nom="SCI DU PARC" objet="Modification · 004176" titre="tout" apres={<span className="rig-badge">V2</span>} />
      <td><Pastille teinte="orange">Procedure collective</Pastille></td>
      <td><PointEtat teinte="rouge">Radiee</PointEtat></td>
    </>)
    const avatar = container.querySelector('.rig-lm-avatar')!
    expect(avatar.textContent).toBe('P')
    expect(avatar.className).toContain(`rig-lm-avatar--${rangAvatar('SCI DU PARC')}`)
    expect(screen.getByText('SCI DU PARC').tagName).toBe('B')
    expect(screen.getByText('Modification · 004176').className).toBe('rig-lm-identite__objet')
    expect(container.querySelector('td')!.title).toBe('tout')
    expect(screen.getByText('Procedure collective').className).toBe('rig-lm-pastille rig-lm-t-orange')
    expect(screen.getByText('Radiee').className).toBe('rig-lm-etat rig-lm-t-rouge')
  })

  it('MontantBarre : barre proportionnelle au plus grand, zero gris sans barre', () => {
    const { container } = render(<>
      <MontantBarre valeur={142.5} max={415} texte="142,50 €" />
      <MontantBarre valeur={0} max={415} texte="0,00 €" />
      <MontantBarre valeur={415} max={415} texte="415,00 €" />
    </>)
    const barres = [...container.querySelectorAll<HTMLElement>('.rig-lm-montant__barre b')].map((b) => b.style.width)
    expect(barres).toEqual(['34%', '0%', '100%'])
    expect(screen.getByText('0,00 €').className).toContain('rig-lm-montant__texte--zero')
    expect(screen.getByText('142,50 €').className).not.toContain('--zero')
  })

  it('le chevron deplie sans choisir la ligne ni l\'ouvrir', () => {
    const basculer = vi.fn()
    const surClic = vi.fn()
    const surDouble = vi.fn()
    dansUneLigne(<CelluleChevron ouverte={false} basculer={basculer} />, surClic, surDouble)
    const chevron = screen.getByRole('button', { name: 'Deplier la ligne' })
    expect(chevron.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(chevron)
    fireEvent.doubleClick(chevron)
    expect(basculer).toHaveBeenCalledTimes(1)
    expect(surClic).not.toHaveBeenCalled()
    expect(surDouble).not.toHaveBeenCalled()
  })

  it('LigneDetail couvre toutes les colonnes : champs (vide = tiret), actions', () => {
    const ouvrir = vi.fn()
    const { container } = render(<table><tbody>
      <LigneDetail colonnes={7} champs={[{ libelle: 'Id instance', valeur: '55210' }, { libelle: 'SIREN', valeur: '' }]}
                   actions={[{ libelle: 'Ouvrir', fort: true, agir: ouvrir }, { libelle: 'Historique', agir: vi.fn(), desactive: true }]}>
        <span>compte rendu</span>
      </LigneDetail>
    </tbody></table>)
    expect(container.querySelector('tr.rig-lm-detail > td')!.getAttribute('colspan')).toBe('7')
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent)).toEqual(['Id instance55210', 'SIREN—'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Ouvrir' }).className).toContain('rig-lm-outil--fort')
    expect((screen.getByRole('button', { name: 'Historique' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('compte rendu')).toBeTruthy()
  })

  it('MenuLigne : « … » ouvre les actions sans toucher a la ligne ; une action ferme le menu ; Echap ne va pas plus loin', () => {
    const ouvrir = vi.fn()
    const surClic = vi.fn()
    const surDouble = vi.fn()
    dansUneLigne(<td><MenuLigne libelle="Actions de 004176" actions={[{ libelle: 'Ouvrir', agir: ouvrir }, { libelle: 'Historique', agir: vi.fn() }]} /></td>,
                 surClic, surDouble)
    const bouton = screen.getByRole('button', { name: 'Actions de 004176' })
    expect(screen.queryByRole('menu')).toBeNull()
    fireEvent.click(bouton)
    fireEvent.doubleClick(bouton)
    expect(bouton.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Ouvrir', 'Historique'])
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Ouvrir' }))
    expect(fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Ouvrir' }), { key: 'Escape' })).toBe(false)
    expect(screen.queryByRole('menu')).toBeNull()

    fireEvent.click(bouton)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).toBeNull()
    expect(surClic).not.toHaveBeenCalled()
    expect(surDouble).not.toHaveBeenCalled()

    // Un clic ailleurs referme le menu.
    fireEvent.click(bouton)
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('PiedListe : les totaux puis l\'aide', () => {
    const { container } = render(<PiedListe aide="Double-clic…"><span><b>15</b> affaires</span></PiedListe>)
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('15 affairesDouble-clic…')
    expect(screen.getByText('Double-clic…').className).toBe('rig-lm-pied__aide')
  })
})
