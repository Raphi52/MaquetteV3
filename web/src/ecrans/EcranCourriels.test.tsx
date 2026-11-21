import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCourriels, type CourrielSuivi, type VueDetailCourriel, type VueEcranCourriels } from './EcranCourriels'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// L'envoi, la trace et ses regles (modele du greffe, repli fichier, envoi ignore hors production, purge a 90 jours) sont
// tenus par le serveur et prouves par tests/SocleCourrielsCheck. Ici : l'ecran montre la liste, ses filtres et le detail.
function envoi(partiel: Partial<CourrielSuivi> = {}): CourrielSuivi {
  return {
    suivi: '6b1e0d5c-0000-4000-8000-000000000001', debut: '2026-11-21T09:15:00', etat: 'envoye', typeModele: 'ASS',
    expediteur: 'judiciaire@greffe-demo.fr', destinataires: 'societe@client-demo.fr', copie: null, sujet: 'Assignation enregistrée',
    nbPieces: 1, idCourriel: 7, erreur: null, agent: 'AMITEL\\agent.demo', fichierDepot: null, ...partiel,
  }
}

function vue(partiel: Partial<VueEcranCourriels> = {}): VueEcranCourriels {
  return {
    mode: 'serveur', message: null, greffe: '3801',
    filtre: { du: '2026-11-21', au: null, destinataire: null, modele: null, etat: null },
    envois: [
      envoi(),
      envoi({ suivi: '6b1e0d5c-0000-4000-8000-000000000003', etat: 'echec', typeModele: 'Convocation', idCourriel: null,
              sujet: 'Convocation', erreur: '5.1.1 adresse inconnue' }),
      envoi({ suivi: '6b1e0d5c-0000-4000-8000-000000000002', etat: 'ignore', typeModele: 'CA', idCourriel: 8,
              fichierDepot: 'C:\\depot\\6b1e0d5c-0000-4000-8000-000000000002.eml' }),
    ],
    ...partiel,
  }
}

const detailEchec: VueDetailCourriel = {
  mode: 'serveur', message: null,
  detail: {
    envoi: vue().envois[1]!, corps: null, corpsHtml: null,
    etapes: [
      { instant: '2026-11-21T11:02:00', etape: 'EMAIL_SEND_DEBUT', mode: 'MANUEL', succes: null, smtpHote: null, smtpPort: null,
        erreurType: null, erreurMessage: null, fichierDepot: null },
      { instant: '2026-11-21T11:02:01', etape: 'SMTP_APPEL', mode: 'MANUEL', succes: null, smtpHote: '127.0.0.1', smtpPort: 2525,
        erreurType: null, erreurMessage: null, fichierDepot: null },
      { instant: '2026-11-21T11:02:02', etape: 'EMAIL_SEND_ERREUR', mode: 'MANUEL', succes: false, smtpHote: '127.0.0.1', smtpPort: 2525,
        erreurType: 'System.Net.Mail.SmtpFailedRecipientException', erreurMessage: '5.1.1 adresse inconnue', fichierDepot: null },
    ],
  },
}

describe('EcranCourriels', () => {
  it('SOC-006 CA03 : chaque envoi avec son issue -- envoyé, refusé, non remis hors production', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue() })
    render(<EcranCourriels />)
    expect(await screen.findByText('3 courriel(s) — greffe 3801')).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('Envoyé')
    expect(lignes[1]!.textContent).toContain('Échec')
    expect(lignes[2]!.textContent).toContain('Non remis (hors production)')
    expect((screen.getByLabelText('Du') as HTMLInputElement).value).toBe('2026-11-21')
  })

  it('les filtres partent au serveur, comme ceux de l\'ecran V2 (periode, destinataire, modele) plus l\'issue', async () => {
    const pont = installerFauxPont({
      'EcranCourriels.Ouvrir': () => vue(),
      'EcranCourriels.Chercher': (f: unknown) => vue({ filtre: f as VueEcranCourriels['filtre'], envois: [vue().envois[1]!] }),
    })
    render(<EcranCourriels />)
    await screen.findByText('3 courriel(s) — greffe 3801')
    fireEvent.change(screen.getByLabelText('Destinataire'), { target: { value: 'societe' } })
    fireEvent.change(screen.getByLabelText('Issue'), { target: { value: 'echec' } })
    expect(await screen.findByText('1 courriel(s) — greffe 3801')).toBeTruthy()
    const appel = pont.appels.mock.calls.find((c) => c[0] === 'EcranCourriels.Chercher')!
    expect(appel[1]).toEqual({ du: '2026-11-21', au: null, destinataire: 'societe', modele: null, etat: 'echec' })
  })

  it('le detail d\'un envoi refuse : ses etapes en clair et la cause du refus', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue(), 'EcranCourriels.Detail': () => detailEchec })
    render(<EcranCourriels />)
    await screen.findByText('3 courriel(s) — greffe 3801')
    fireEvent.click(screen.getAllByRole('row')[2]!)
    const detail = await screen.findByLabelText('Détail du courriel')
    expect(detail.textContent).toContain('Remis au serveur de messagerie')
    expect(detail.textContent).toContain('Envoi refusé')
    expect(detail.textContent).toContain('5.1.1 adresse inconnue')
  })

  it('le texte d\'un courriel parti, « | » en sauts de ligne comme la V2', async () => {
    const parti: VueDetailCourriel = { mode: 'serveur', message: null,
      detail: { envoi: vue().envois[0]!, corps: 'Madame,|Votre assignation est enregistrée.', corpsHtml: false, etapes: [] } }
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue(), 'EcranCourriels.Detail': () => parti })
    render(<EcranCourriels />)
    await screen.findByText('3 courriel(s) — greffe 3801')
    fireEvent.click(screen.getAllByRole('row')[1]!)
    expect((await screen.findByLabelText('Texte du courriel')).textContent).toBe('Madame,\nVotre assignation est enregistrée.')
  })

  it('sans la fonction MAIL_LOT1 : le refus du serveur, sans liste', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue({ mode: 'refuse', envois: [], message: 'Refuse : le compte x n\'a pas la fonction MAIL_LOT1.' }) })
    render(<EcranCourriels />)
    const alerte = await screen.findByRole('alert')
    expect(alerte.textContent).toContain('Accès refusé')
    expect(alerte.textContent).toContain('MAIL_LOT1')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('sans serveur : la demonstration', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranCourriels />)
    expect(await screen.findAllByText('Démonstration')).toHaveLength(2)
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(3)
  })
})
