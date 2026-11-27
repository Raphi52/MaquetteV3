import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranStatistiques, moisPrecedent, type VueStatistiques } from './EcranStatistiques'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le calcul (les requetes de RIG v2 dans la base du greffe) est fait par Serveur/Socle/EcransMain/LectureStatistiques.cs et
// verifie par tests/EcransMainBaseCheck. Ici : l'ecran DEMANDE le bon volet et la bonne periode, et AFFICHE ce que le pont rend.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

function vue(volet: unknown): VueStatistiques {
  if (volet === 'Semestrielles')
    return { tableaux: [{ titre: 'Administrateurs judiciaires', colonnes: ['Nom', 'Nb affaires'], lignes: [{ libelle: 'Total', valeurs: ['0'] }] }], nombrePc: '7', mode: 'serveur' }
  if (volet === 'Web')
    return { tableaux: [{ titre: 'Ouvertures, transformations et plans', colonnes: ['Famille', 'CA < 153', 'Total'],
      lignes: [{ libelle: 'Ouvertures RJ', valeurs: ['non calculé : la requête de RIG v2 lit CHIFFRES_PC (absente de la base V3)'] }] }], mode: 'serveur' }
  return {
    tableaux: [
      { titre: 'Statistiques judiciaires', colonnes: ['Indicateur', 'Nombre'], lignes: [{ libelle: 'Affaires enrôlées', valeurs: ['42'] }] },
      { titre: 'Procédures collectives - Clôtures', colonnes: ['Code de clôture', 'Nombre'], lignes: [], cause: 'non calculé : CODE_CLOTURE (absente de la base V3)' },
    ],
    mode: 'serveur',
  }
}

describe('EcranStatistiques', () => {
  it('calcule le volet des statistiques internes sur le mois precedent (OPE_DATES) et affiche les tableaux du pont', async () => {
    const pont = installerFauxPont({ 'EcranStatistiques.Calculer': vue })
    const { container } = render(<EcranStatistiques />)
    const { debut, fin } = moisPrecedent(new Date())
    await waitFor(() => expect(lignes(container)).toEqual([['Affaires enrôlées', '42']]))
    expect(pont.appels).toHaveBeenCalledWith('EcranStatistiques.Calculer', 'Internes', debut, fin, null)
    expect((screen.getByLabelText('Date de debut') as HTMLInputElement).value).toBe(debut)
    expect([...container.querySelectorAll('.rig-table-cadre > .rig-kicker')].map((e) => e.textContent))
      .toEqual(['Statistiques judiciaires', 'Procédures collectives - Clôtures'])
    // Un tableau non calcule dit sa cause ; branche au serveur, aucune mention « exemple ».
    expect(screen.getByText('non calculé : CODE_CLOTURE (absente de la base V3)')).toBeTruthy()
    expect(container.querySelector('.rig-tcw__exemple')).toBeNull()
    // Le niveau n'existe que dans les volets d'activites (OPE_NIVEAU).
    expect(screen.queryByLabelText('Niveau')).toBeNull()
  })

  it('les activites envoient le niveau NAF 1 a 4 ; le semestriel affiche le nombre de PC calcule', async () => {
    const pont = installerFauxPont({ 'EcranStatistiques.Calculer': vue })
    const { container } = render(<EcranStatistiques />)
    const { debut, fin } = moisPrecedent(new Date())
    fireEvent.click(screen.getByRole('button', { name: 'Ouverture par activites : par chiffre d\'affaire' }))
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranStatistiques.Calculer', 'ActivitesCa', debut, fin, '1'))
    const niveau = screen.getByLabelText('Niveau') as HTMLSelectElement
    expect([...niveau.options].map((o) => o.value)).toEqual(['1', '2', '3', '4'])
    fireEvent.change(niveau, { target: { value: '3' } })
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranStatistiques.Calculer', 'ActivitesCa', debut, fin, '3'))

    fireEvent.click(screen.getByRole('button', { name: 'Statistiques semestrielles' }))
    await waitFor(() => expect(screen.getByLabelText('Nombre de PC').textContent).toBe('7'))
    expect(screen.queryByLabelText('Niveau')).toBeNull()

    // Une ligne non calculee etale sa cause sur les colonnes de chiffres.
    fireEvent.click(screen.getByRole('button', { name: 'Statistiques web' }))
    await waitFor(() => expect(lignes(container)).toEqual([['Ouvertures RJ', 'non calculé : la requête de RIG v2 lit CHIFFRES_PC (absente de la base V3)']]))
    expect(container.querySelector('tbody td[colspan="2"]')).toBeTruthy()
  })

  it('sans serveur : les tableaux de la demonstration, marques « exemple » ; une periode refusee, le message de RIG v2', async () => {
    let refuse = false
    installerFauxPont({
      'EcranStatistiques.Calculer': (): VueStatistiques => refuse
        ? { tableaux: [], refus: 'La date de début ne peut pas être supérieur à la date de fin', mode: 'serveur' }
        : { tableaux: [{ titre: 'Statistiques judiciaires', colonnes: ['Indicateur', 'Nombre'], lignes: [{ libelle: 'Affaires enrôlées', valeurs: ['42'] }] }], mode: 'demo' },
    })
    const { container } = render(<EcranStatistiques />)
    await waitFor(() => expect(container.querySelector('[role="note"]')?.textContent).toContain('Statistiques de démonstration'))
    refuse = true
    fireEvent.change(screen.getByLabelText('Date de debut'), { target: { value: '2099-01-01' } })
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('La date de début ne peut pas être supérieur à la date de fin'))
    expect(container.querySelectorAll('tbody tr')).toHaveLength(0)
  })
})
