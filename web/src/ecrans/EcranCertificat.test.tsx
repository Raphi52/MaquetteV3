import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCertificat, pastilleCertificat, type Certificat, type EtatCertificat, type VueEcranCertificat } from './EcranCertificat'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La regle de validite (organisme accredite, dates, version couverte) vit dans Contrats/Socle/Certification.cs et le serveur
// la joue (tests/SocleBetaCheck, SOC-019 CA04). Ici : l'ecran montre ce que le pont lui rend, et ne montre jamais un
// certificat qu'il n'a pas recu.
function certificat(partiel: Partial<Certificat> = {}): Certificat {
  return {
    logiciel: 'V3', organisme: 'AFNOR Certification', accreditation: 'COFRAC 5-0012', numero: 'NF525-V3-2027-001',
    referentiel: 'NF525 version 2', versionCouverte: '2026.1', delivreLe: '2026-12-01', valideJusquau: '2029-11-30', ...partiel,
  }
}

function etat(partiel: Partial<EtatCertificat> = {}): EtatCertificat {
  return {
    greffe: '4302', etat: 'valide', certificat: certificat(), versionEnService: '2026.1.0', aujourdhui: '2026-12-15',
    motifs: [], enregistreLe: '2026-12-02T09:00:00Z', ...partiel,
  }
}

function vue(partiel: Partial<VueEcranCertificat> = {}): VueEcranCertificat {
  return { mode: 'serveur', message: null, etat: etat(), ...partiel }
}

describe('EcranCertificat', () => {
  it('SOC-019 CA04 : le greffe retrouve le certificat de la V3, avec sa date de validite', async () => {
    installerFauxPont({ 'EcranCertificat.Ouvrir': () => vue() })
    render(<EcranCertificat />)
    expect((await screen.findByLabelText('État du certificat')).textContent).toBe('Valide')
    const fiche = screen.getByRole('table', { name: 'Le certificat' })
    const lignes = [...fiche.querySelectorAll('tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
    expect(lignes).toEqual([
      ['Organisme', 'AFNOR Certification'],
      ['Accréditation', 'COFRAC 5-0012'],
      ['Numéro du certificat', 'NF525-V3-2027-001'],
      ['Référentiel', 'NF525 version 2'],
      ['Version couverte', '2026.1'],
      ['Délivré le', '01/12/2026'],
      ['Valide jusqu\'au', '30/11/2029'],
    ])
    expect(screen.getByText('2026.1.0').tagName).toBe('B')
  })

  it('SOC-019 CA04 : sans certificat enregistre, « certification en cours », et aucune fiche de certificat', async () => {
    installerFauxPont({
      'EcranCertificat.Ouvrir': () => vue({
        etat: etat({ etat: 'certification en cours', certificat: null, enregistreLe: null,
                     motifs: ['Aucun certificat n\'est encore enregistré par Amitel : la certification de la V3 est en cours.'] }),
      }),
    })
    render(<EcranCertificat />)
    expect((await screen.findByLabelText('État du certificat')).textContent).toBe('Certification en cours')
    expect(screen.queryByRole('table', { name: 'Le certificat' })).toBeNull()
    expect(screen.getByRole('note').textContent).toContain('n\'en affiche jamais un d\'exemple')
    expect(screen.getByText('Aucun certificat enregistré')).toBeTruthy()
  })

  it('SOC-019 CA04 : un certificat expire ou d\'une autre version est signale, avec ce qui manque', async () => {
    installerFauxPont({
      'EcranCertificat.Ouvrir': () => vue({
        etat: etat({ etat: 'expire', certificat: certificat({ valideJusquau: '2026-12-01' }),
                     motifs: ['expiré le 01/12/2026', 'couvre la version 2025.9, pas la version 2026.1.0 en service'] }),
      }),
    })
    render(<EcranCertificat />)
    expect((await screen.findByLabelText('État du certificat')).textContent).toBe('Expiré')
    const manques = screen.getByRole('list', { name: 'Ce qui manque' })
    expect([...manques.querySelectorAll('li')].map((li) => li.textContent)).toEqual([
      'expiré le 01/12/2026', 'couvre la version 2025.9, pas la version 2026.1.0 en service'])
    // Le certificat reste lisible : c'est un vrai certificat, seulement plus valide.
    expect(screen.getByRole('table', { name: 'Le certificat' }).textContent).toContain('01/12/2026')
  })

  it('en demonstration, l\'ecran dit « certification en cours » et ne montre aucun certificat d\'exemple', async () => {
    installerFauxPont({
      'EcranCertificat.Ouvrir': () => vue({
        mode: 'demo', message: 'Démonstration : sans serveur de RIG, le certificat enregistré par Amitel ne se lit pas ; l\'écran n\'en montre aucun d\'exemple.',
        etat: etat({ greffe: null, etat: 'certification en cours', certificat: null, enregistreLe: null, motifs: ['…'] }),
      }),
    })
    render(<EcranCertificat />)
    expect((await screen.findByLabelText('État du certificat')).textContent).toBe('Certification en cours')
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getAllByText('Démonstration').length).toBeGreaterThan(0)
  })

  it('un refus du serveur s\'affiche en alerte, sans certificat', async () => {
    installerFauxPont({ 'EcranCertificat.Ouvrir': () => vue({ mode: 'refuse', message: 'Vous n\'avez pas accès à la V3 du greffe 4302 : le certificat ne se lit pas.', etat: null }) })
    render(<EcranCertificat />)
    expect((await screen.findByRole('alert')).textContent).toContain('Accès refusé')
    expect(screen.queryByRole('table')).toBeNull()
  })

  // Lot 13 du plan « RIG v3 complet » : Amitel enregistre le certificat depuis l'ecran (SOC-019 T03) ; un agent de greffe ne le peut pas.
  // fix-ok: aucun ecran n'offrait l'enregistrement (PUT joue par tests/SocleBetaCheck seul) ; ces tests tiennent le formulaire.
  it('un agent de greffe ne voit pas le formulaire d enregistrement', async () => {
    installerFauxPont({ 'EcranCertificat.Ouvrir': () => vue({ peutEnregistrer: false }) })
    render(<EcranCertificat />)
    await screen.findByLabelText('État du certificat')
    expect(screen.queryByRole('button', { name: 'Enregistrer le certificat' })).toBeNull()
  })

  it('Amitel enregistre le certificat recu ; un refus du serveur est dit, la fiche reste', async () => {
    const sans = vue({ peutEnregistrer: true, etat: etat({ etat: 'certification en cours', certificat: null, enregistreLe: null }) })
    const reponses: VueEcranCertificat[] = [
      vue({ mode: 'refuse', message: 'Le certificat n\'est pas enregistré : Certificat incomplet (accréditation).', etat: null }),
      vue({ peutEnregistrer: true, message: 'Certificat n°NF525-V3-2027-001 enregistré : chaque greffe le retrouve ici.' }),
    ]
    const pont = installerFauxPont({ 'EcranCertificat.Ouvrir': () => sans, 'EcranCertificat.Enregistrer': () => reponses.shift() })
    render(<EcranCertificat />)
    const bouton = await screen.findByRole('button', { name: 'Enregistrer le certificat' }) as HTMLButtonElement
    expect(bouton.disabled).toBe(true)
    const saisir = (libelle: string, valeur: string) => fireEvent.change(screen.getByLabelText(libelle), { target: { value: valeur } })
    saisir('Organisme', 'AFNOR Certification'); saisir('Accréditation', 'COFRAC 5-0012'); saisir('Numéro du certificat', 'NF525-V3-2027-001')
    saisir('Délivré le', '2026-12-01'); saisir('Valide jusqu\'au', '2029-11-30')
    expect(bouton.disabled).toBe(false)

    fireEvent.click(bouton)
    expect((await screen.findByText(/Certificat incomplet/)).textContent).toContain('accréditation')
    expect(screen.getByLabelText('État du certificat').textContent).toBe('Certification en cours')

    fireEvent.click(bouton)
    await vi.waitFor(() => expect(screen.getByLabelText('État du certificat').textContent).toBe('Valide'))
    expect(screen.getByText(/enregistré : chaque greffe le retrouve ici/)).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranCertificat.Enregistrer', {
      logiciel: 'V3', organisme: 'AFNOR Certification', accreditation: 'COFRAC 5-0012', numero: 'NF525-V3-2027-001',
      referentiel: 'NF525', versionCouverte: '2026.1', delivreLe: '2026-12-01', valideJusquau: '2029-11-30',
    })
  })

  it('chaque etat a sa pastille et sa phrase', () => {
    expect(pastilleCertificat('valide').classe).toBe('ok')
    expect(pastilleCertificat('certification en cours').classe).toBe('attente')
    for (const e of ['expire', 'autre version', 'incomplet'] as const) expect(pastilleCertificat(e).classe).toBe('alerte')
  })
})
