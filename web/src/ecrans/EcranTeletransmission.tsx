// Ecran PROC_TELETRANS : le suivi des flux TELETRANSMIS (EDI) recus au
// greffe. Destination de l'alerte « Teletransmission » de l'accueil. Les lignes
// viennent du courrier arrive (JeuDemo, table ARRIVEE), calculees par
// Pont/EcranTeletransmission.cs : un flux EDI est une arrivee de mode « EDI ».
//
// Porte depuis Components/Ecrans/EcranTeletransmission.razor (lot 3) : meme
// balisage, memes classes CSS. La coquille .razor monte cet ecran et recoit
// ouvrirDossier.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 3) : les flux prennent les briques de
// composants/ListeModerne.tsx. Tete (synthese, « Aller a un flux… » Ctrl K qui filtre la liste affichee, Entree
// ouvre l'affaire si un seul flux reste), deux vues = les deux valeurs du critere « Mode » (EDI, tous), un
// tableau a une ligne par flux (Emetteur avec reference et numero d'arrivee, Mode en pastille, Demande,
// Numerise en point d'etat, Recu le) et un pied. Le mode, la bascule « Dossier … seulement », l'etat du canal
// et le clic qui ouvre l'affaire ne changent pas.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import type { ContexteDossierVue } from '../plan/types'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, PointEtat, VuesListe } from '../composants/ListeModerne'
import { teinteModeArrivee } from './EcranArrivee'

export interface LigneFlux {
  numero: string
  horodatage: string
  modeLisible: string
  nom: string
  references: string
  numDemande: string
  numerise: boolean
}

export interface VueTeletransmission {
  flux: LigneFlux[]
  /** « dernier flux : 09/09/2026 09:12 », ou le conseil quand rien n'est arrive. */
  dernier: string
}

export interface ProprietesEcranTeletransmission {
  /** Remonte le numero de l'affaire rattachee au flux clique. */
  ouvrirDossier: (numero: string) => void
  /** L'affaire d'ou l'ecran a ete ouvert (page du dossier) ; absent ou null depuis l'accueil. */
  dossier?: ContexteDossierVue | null
}

/** Les valeurs du critere « Mode », dans l'ordre de sa liste : ce sont aussi les vues de la liste. */
const Modes: ReadonlyArray<readonly [code: string, libelle: string]> = [['EDI', 'Teletransmis (EDI)'], ['TOUS', 'Tous les modes']]

/** Les colonnes du tableau : Emetteur, Mode, Demande, Numerise, Recu le. */
const NbColonnes = 5

/**
 * « Aller a… » compare sans casse ni accents, comme la Recherche d'affaires : « infogréffe » trouve « INFOGREFFE ».
 * fix-ok: contientSansCasse (criteres.ts) garde les accents ; test « Aller a un flux… » rouge sur « infogréffe », vert ici.
 */
function normaliser(texte: string): string {
  return texte.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '')
}

export function EcranTeletransmission({ ouvrirDossier, dossier = null }: ProprietesEcranTeletransmission) {
  const [mode, setMode] = useState('EDI')
  // Ouvert depuis une affaire : seuls les flux rattaches a elle ; la bascule,
  // montree seulement alors, rend la liste entiere.
  const [surLeDossier, setSurLeDossier] = useState(dossier !== null)
  const filtre = surLeDossier ? dossier : null
  // null tant que le pont n'a pas repondu : ni « Canal au vert » ni « Aucun
  // flux recu », qui seraient faux pendant cet instant.
  const [vue, setVue] = useState<VueTeletransmission | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueTeletransmission>('EcranTeletransmission.Flux', mode)
      .then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [mode])

  // Le compte de chaque vue : les flux de chaque mode, redemandes avec ceux du mode montre.
  const [fluxParMode, setFluxParMode] = useState<Record<string, LigneFlux[]> | null>(null)
  useEffect(() => {
    let perime = false
    Promise.all(Modes.map(([code]) => appeler<VueTeletransmission>('EcranTeletransmission.Flux', code).then((v) => [code, v.flux] as const)))
      .then((r) => { if (!perime) setFluxParMode(Object.fromEntries(r)) },
            // Sans reponse, les puces restent sans compte.
            () => { if (!perime) setFluxParMode(null) })
    return () => { perime = true }
  }, [mode])

  // L'etat du canal se lit sur TOUS les flux recus, la liste sur ceux de l'affaire.
  const recus = vue?.flux ?? []
  const surAffaire = (liste: LigneFlux[]) => (filtre ? liste.filter((f) => f.numDemande === filtre.numero) : liste)
  const flux = surAffaire(recus)

  // « Aller a un flux… » (Ctrl K) : filtre la liste AFFICHEE.
  const [filtreRapide, setFiltreRapide] = useState('')
  const cherche = filtreRapide.trim()
  const chercheNormal = normaliser(cherche)
  const affiches = cherche === '' ? flux : flux.filter((f) => normaliser(
    [f.numero, f.nom, f.references, f.numDemande, f.modeLisible].join(' ')).includes(chercheNormal))

  /** Un flux sans demande rattachee ne mene nulle part. */
  function aller(f: LigneFlux) {
    if (f.numDemande.trim() === '') return
    ouvrirDossier(f.numDemande)
  }

  const aNumeriser = affiches.filter((f) => !f.numerise).length
  const sansDemande = affiches.filter((f) => f.numDemande.trim() === '').length

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Flux recus</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Mode</span>
          <select className="rig-saisie-champ__saisie" value={mode} onChange={(e) => setMode(e.target.value)}>
            <option value="EDI">EDI (teletransmission)</option>
            <option value="TOUS">Tous les modes</option>
          </select>
        </label>

        {dossier && (
          <button className={`rig-bascule${surLeDossier ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setSurLeDossier(!surLeDossier)}>
            <span className="rig-bascule__temoin"></span>
            <span>Dossier {dossier.numero} seulement</span>
          </button>
        )}

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Etat du canal</div>
          {vue && (
            <div className="rig-alerte">
              <span className={`rig-filet-v rig-filet-v--${recus.length > 0 ? 'ok' : 'action'}`}></span>
              <span className="rig-alerte__corps">
                <span className="rig-alerte__titre">{recus.length > 0 ? 'Canal au vert' : 'Aucun flux recu'}</span><br />
                <span className="rig-alerte__detail">{vue.dernier}</span>
              </span>
            </div>
          )}
        </div>
      </aside>

      <ListeModerne etiquette="Teletransmissions">
        <EnteteListe titre="Teletransmissions"
                     synthese={vue && <>{affiches.length}{' flux · '}<b>{aNumeriser}</b>{' a numeriser · '}<b>{sansDemande}</b>{' sans demande'}</>}
                     filtre={{ valeur: filtreRapide, changer: setFiltreRapide, indication: 'Aller a un flux…',
                               valider: () => { if (affiches.length === 1) aller(affiches[0]!) } }} />

        <VuesListe vues={Modes.map(([code, libelle]) => {
                     const liste = fluxParMode?.[code]
                     return { cle: code, libelle, compte: liste ? surAffaire(liste).length : undefined }
                   })}
                   active={mode} choisir={setMode} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup>
              <col /><col style={{ width: '14%' }} /><col style={{ width: '16%' }} /><col style={{ width: '14%' }} />
              <col style={{ width: '20%' }} />
            </colgroup>
            <thead>
              <tr>
                <th>Emetteur</th>
                <th>Mode</th>
                <th>Demande</th>
                <th>Numerise</th>
                <th>Recu le</th>
              </tr>
            </thead>
            <tbody>
              {affiches.map((f, i) => (
                <tr key={`${i}:${f.numero}`} onClick={() => aller(f)}
                    title={f.numDemande.length > 0 ? `Ouvrir l'affaire ${f.numDemande}` : 'Aucune affaire rattachee'}>
                  <CelluleIdentite nom={f.nom}
                                   objet={<>{f.references}{f.references !== '' ? ' · ' : ''}<span>{f.numero}</span></>} />
                  <td><Pastille teinte={teinteModeArrivee(f.modeLisible)}>{f.modeLisible}</Pastille></td>
                  <td className="rig-table__mono">{f.numDemande}</td>
                  <td>{f.numerise ? <PointEtat teinte="vert">oui</PointEtat> : <PointEtat teinte="orange">non</PointEtat>}</td>
                  <td className="rig-table__mono">{f.horodatage}</td>
                </tr>
              ))}
              {vue && flux.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {filtre ? `Aucun flux pour le dossier ${filtre.numero} dans ce mode.` : 'Aucun flux pour ce mode.'}
                </td></tr>
              )}
              {flux.length > 0 && affiches.length === 0 && (
                <tr><td colSpan={NbColonnes} className="rig-table__vide">
                  {`Aucun des ${flux.length} flux ne contient « ${cherche} ».`}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <PiedListe aide="Clic sur un flux pour ouvrir son affaire.">
          <span><b>{affiches.length}</b>{affiches.length > 1 ? ' flux affiches' : ' flux affiche'}</span>
          <span>A numeriser <b>{aNumeriser}</b></span>
          <span>Sans demande <b>{sansDemande}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
