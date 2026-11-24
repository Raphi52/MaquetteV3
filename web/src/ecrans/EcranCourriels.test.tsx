import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranCourriels, type CourrielSuivi, type VueDetailCourriel, type VueEcranCourriels } from './EcranCourriels'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// L'envoi, la trace et ses regles (modele du greffe, repli fichier, envoi ignore hors production, purge a 90 jours) sont
// tenus par le serveur et prouves par tests/SocleCourrielsCheck. Ici : l'ecran montre la liste, ses filtres et le detail.
// fix-ok: tests suivis de la liste moderne (lot 5) ecrits en plusieurs pas -- ajout, pas un correctif.
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

/** La synthese de la tete de liste, attendue a la reponse du pont : « 3 courriels · 1 échec · greffe 3801 ». */
const synthese = async () => (await screen.findByText(/ · /, { selector: '.rig-lm-synthese' })).textContent
const puce = (nom: string) => screen.getByRole('button', { name: new RegExp(`^${nom.replace(/[()]/g, '\\$&')}( \\d+)?$`) })

describe('EcranCourriels', () => {
  it('SOC-006 CA03 : chaque envoi avec son issue -- envoyé, refusé, non remis hors production', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue() })
    const { container } = render(<EcranCourriels />)
    expect(await synthese()).toBe('3 courriels · 1 échec · greffe 3801')
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('Envoyé')
    expect(lignes[1]!.textContent).toContain('Échec')
    expect(lignes[2]!.textContent).toContain('Non remis (hors production)')
    // L'issue en point colore, le modele en pastille, le destinataire avec son avatar et l'objet du courriel.
    expect(lignes.map((l) => l.querySelector('.rig-lm-etat')!.className))
      .toEqual(['rig-lm-etat rig-lm-t-vert', 'rig-lm-etat rig-lm-t-rouge', 'rig-lm-etat rig-lm-t-orange'])
    expect(lignes[0]!.querySelector('.rig-lm-pastille')!.textContent).toBe('ASS')
    expect(lignes[0]!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('societe@client-demo.fr')
    expect(lignes[0]!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('Assignation enregistrée')
    // La cause du refus, au survol de la ligne refusee.
    expect(lignes[1]!.querySelector('.rig-lm-identite')!.closest('td')!.title).toBe('societe@client-demo.fr — 5.1.1 adresse inconnue')
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('3 courriels affichés')
    expect((screen.getByLabelText('Du') as HTMLInputElement).value).toBe('2026-11-21')
  })

  it('les filtres partent au serveur, comme ceux de l\'ecran V2 (periode, destinataire, modele) plus l\'issue', async () => {
    const pont = installerFauxPont({
      'EcranCourriels.Ouvrir': () => vue(),
      'EcranCourriels.Chercher': (f: unknown) => vue({ filtre: f as VueEcranCourriels['filtre'], envois: [vue().envois[1]!] }),
    })
    render(<EcranCourriels />)
    await synthese()
    fireEvent.change(screen.getByLabelText('Destinataire'), { target: { value: 'societe' } })
    fireEvent.change(screen.getByLabelText('Issue'), { target: { value: 'echec' } })
    await waitFor(async () => expect(await synthese()).toBe('1 courriel · 1 échec · greffe 3801'))
    const appel = pont.appels.mock.calls.find((c) => c[0] === 'EcranCourriels.Chercher')!
    expect(appel[1]).toEqual({ du: '2026-11-21', au: null, destinataire: 'societe', modele: null, etat: 'echec' })
  })

  it('les vues sont le critere « Issue » : une puce part au serveur comme la liste deroulante', async () => {
    const pont = installerFauxPont({
      'EcranCourriels.Ouvrir': () => vue(),
      'EcranCourriels.Chercher': (f: unknown) => {
        const filtre = f as VueEcranCourriels['filtre']
        return vue({ filtre, envois: vue().envois.filter((e) => filtre.etat === null || e.etat === filtre.etat) })
      },
    })
    const { container } = render(<EcranCourriels />)
    await synthese()
    // Toutes les issues dans la liste : chaque puce a son compte.
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes 3', 'Envoyé 1', 'Non remis (hors production) 1', 'Échec 1', 'Sans issue 0'])
    expect(puce('Toutes').getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(puce('Échec'))
    await waitFor(async () => expect(await synthese()).toBe('1 courriel · 1 échec · greffe 3801'))
    expect(pont.appels).toHaveBeenCalledWith('EcranCourriels.Chercher', { du: '2026-11-21', au: null, destinataire: null, modele: null, etat: 'echec' })
    expect((screen.getByLabelText('Issue') as HTMLSelectElement).value).toBe('echec')
    expect(puce('Échec').getAttribute('aria-pressed')).toBe('true')
    // La liste ne porte plus que les echecs : seul le compte de la puce choisie est connu.
    expect([...container.querySelectorAll('.rig-lm-vue')].map((v) => v.textContent))
      .toEqual(['Toutes', 'Envoyé', 'Non remis (hors production)', 'Échec 1', 'Sans issue'])
  })

  it('« Aller à un courriel… » filtre la liste affichee ; Entree ouvre le detail du courriel reste seul', async () => {
    const pont = installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue(), 'EcranCourriels.Detail': () => detailEchec })
    render(<EcranCourriels />)
    await synthese()
    const champ = screen.getByLabelText('Aller à un courriel…')
    fireEvent.change(champ, { target: { value: 'convocation' } })
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(1)
    expect(await synthese()).toBe('1 courriel · 1 échec · greffe 3801')
    expect(pont.appels).not.toHaveBeenCalledWith('EcranCourriels.Chercher', expect.anything())
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect((await screen.findByLabelText('Détail du courriel')).textContent).toContain('Envoi refusé')
    expect(pont.appels).toHaveBeenCalledWith('EcranCourriels.Detail', '6b1e0d5c-0000-4000-8000-000000000003')
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 3 courriels ne contient « zzz ».')).toBeTruthy()
  })

  it('le detail d\'un envoi refuse : ses etapes en clair et la cause du refus', async () => {
    installerFauxPont({ 'EcranCourriels.Ouvrir': () => vue(), 'EcranCourriels.Detail': () => detailEchec })
    render(<EcranCourriels />)
    await synthese()
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
    await synthese()
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
    // La tete le dit, sans greffe ; le bandeau l'explique.
    expect(await synthese()).toBe('Démonstration · 3 courriels · 1 échec')
    expect(screen.getByText('Démonstration', { selector: '.rig-etat' }).parentElement!.textContent).toContain('Sans serveur, trois envois d\'exemple')
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(3)
  })
})
