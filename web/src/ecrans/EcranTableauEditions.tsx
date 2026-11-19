// ============ ECRAN : TABLEAU DES EDITIONS ============
// Ecran a part entiere, ouvert par un bouton depuis l'affaire : la colonne de
// droite de l'affaire est desormais la saisie guidee.
// Colonnes de PROC_TABLEAU_LOT3/Processus/TreeviewEditions.cs,
// champs de detail d'OPE_DESTINATAIRE.Designer.cs (v2).
//
// Porte depuis Components/Ecrans/EcranTableauEditions.razor (lot 3) : meme
// balisage, memes classes CSS. Les lignes arrivent par les proprietes de l'ile
// (parametre Editions de la coquille) ; la liste des types de papier vient de
// Pont/EcranTableauEditions.cs (ArbreDemo.TypesPapier). La coquille recoit fermer.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'

/** Le detail du destinataire (Metier/ArbreGed.cs, DetailEdition). */
export interface DetailEdition {
  designation: string
  attentionDe: string
  adressePostale: string
  typePapier: string
  telephone: string
  siren: string
  natureCoffreFort: string
  coffreFort: string
  natureTribunalDigital: string
  tribunalDigital: string
  erreurTribunalDigital: string
  courriel: string
  conserverPjOrigine: boolean
  observation: string
}

/** Une ligne du tableau (Metier/ArbreGed.cs, LigneEdition), telle que la coquille la serialise. */
export interface LigneEdition {
  destinataire: string
  nomDocument: string
  agrafage: boolean
  coffreFort: boolean
  tribunalDigital: boolean
  envoiParMail: boolean
  exemplaires: number
  imprimer: boolean
  imprimante: string
  fileImprimante: string
  affAvantImpression: boolean
  rectoVerso: boolean
  fileDifferee: string
  impressionSupplementaire: boolean
  niveau: number
  estGroupe: boolean
  detail: DetailEdition | null
}

export interface ProprietesEcranTableauEditions {
  /** Les lignes d'edition de l'affaire ouverte. */
  editions?: LigneEdition[] | null
  /** Numero et denomination de l'affaire, pour l'entete. */
  affaire?: string | null
  /** Referme l'ecran et revient a l'affaire. */
  fermer: () => void
}

/** `@(b ? "x" : "")` des colonnes drapeau. */
function croix(b: boolean): string {
  return b ? 'x' : ''
}

export function EcranTableauEditions({ editions, affaire, fermer }: ProprietesEcranTableauEditions) {
  const lignes = editions ?? []
  // La ligne choisie, par sa place dans le tableau (le .razor gardait la ligne elle-meme).
  const [selection, setSelection] = useState<number | null>(null)
  const [typesPapier, setTypesPapier] = useState<string[]>([])

  useEffect(() => {
    let perime = false
    void appeler<string[]>('EcranTableauEditions.TypesPapier').then((t) => { if (!perime) setTypesPapier(t) })
    return () => { perime = true }
  }, [])

  const choisie = selection === null ? null : lignes[selection] ?? null
  const d = choisie?.detail ?? null

  return (
    <div className="rig-editions-ecran">
      <div className="rig-editions-ecran__entete">
        <b>Tableau des editions</b>
        <span style={{ color: 'var(--rig-texte-3)', fontSize: '12px' }}>
          {affaire}{' · '}{lignes.filter((e) => !e.estGroupe).length}{' editions'}
        </span>
        <span style={{ marginLeft: 'auto' }}></span>
        <button className="rig-bouton" onClick={() => fermer()}>Retour a l'affaire</button>
      </div>
      <div className="rig-editions-ecran__corps">
        <div className="rig-editions__cadre">
          <table className="rig-editions">
            <thead>
              <tr>
                <th className="rig-editions__dest">Destinataire</th>
                <th className="rig-editions__drapeau" title="Agrafage">Ag</th>
                <th className="rig-editions__doc">Nom du document</th>
                <th className="rig-editions__drapeau" title="Envoi au coffre">Cf</th>
                <th className="rig-editions__drapeau" title="Envoi tribunal digital">Td</th>
                <th className="rig-editions__drapeau" title="Envoi par courriel">@</th>
                <th className="rig-editions__ex" title="Nombre d'exemplaires">Ex.</th>
                <th className="rig-editions__drapeau" title="Imprimer">Imp</th>
                <th className="rig-editions__imprimante">Imprimante</th>
                <th className="rig-editions__file">File ou imprimante</th>
                <th className="rig-editions__drapeau" title="Afficher avant impression">Av</th>
                <th className="rig-editions__drapeau" title="Recto/Verso">R/V</th>
                <th className="rig-editions__file">File differee</th>
                <th className="rig-editions__drapeau" title="Impression supplementaire au greffe">+</th>
              </tr>
            </thead>
            <tbody>
              {lignes.map((e, i) => e.estGroupe ? (
                <tr key={i} className="rig-editions__groupe">
                  <td colSpan={14} style={{ paddingLeft: `${6 + e.niveau * 14}px` }}>
                    <span className="rig-arbre__chevron rig-arbre__chevron--ouvert"></span>{e.destinataire}
                  </td>
                </tr>
              ) : (
                <tr key={i} className={selection === i ? 'rig-editions__ligne--actif' : ''}
                    onClick={() => setSelection(i)}>
                  <td className="rig-editions__dest" style={{ paddingLeft: `${6 + e.niveau * 14}px` }}>{e.destinataire}</td>
                  <td className="rig-editions__drapeau">{croix(e.agrafage)}</td>
                  <td className="rig-editions__doc">{e.nomDocument}</td>
                  <td className="rig-editions__drapeau">{croix(e.coffreFort)}</td>
                  <td className="rig-editions__drapeau">{croix(e.tribunalDigital)}</td>
                  <td className="rig-editions__drapeau">{croix(e.envoiParMail)}</td>
                  <td className="rig-editions__ex">{e.exemplaires}</td>
                  <td className="rig-editions__drapeau">{croix(e.imprimer)}</td>
                  <td className="rig-editions__imprimante">{e.imprimante}</td>
                  <td className="rig-editions__file">{e.fileImprimante}</td>
                  <td className="rig-editions__drapeau">{croix(e.affAvantImpression)}</td>
                  <td className="rig-editions__drapeau">{croix(e.rectoVerso)}</td>
                  <td className="rig-editions__file">{e.fileDifferee}</td>
                  <td className="rig-editions__drapeau">{croix(e.impressionSupplementaire)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="rig-editions__titre">Detail de l'edition selectionnee</div>
        <div className="rig-editions__detail">
          {d !== null ? (
            <>
              <label className="rig-champ-ed"><span>Designation</span>
                <input className="rig-saisie" value={d.designation ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>A l'attention de</span>
                <input className="rig-saisie" value={d.attentionDe ?? ''} readOnly /></label>
              <label className="rig-champ-ed rig-champ-ed--large"><span>Adresse postale</span>
                <textarea className="rig-saisie" rows={3} readOnly value={d.adressePostale ?? ''}></textarea></label>
              <label className="rig-champ-ed"><span>Type de papier</span>
                <select className="rig-saisie" disabled value={d.typePapier ?? ''}>
                  {typesPapier.map((tp) => (
                    <option key={tp} value={tp}>{tp}</option>
                  ))}
                </select></label>
              <label className="rig-champ-ed"><span>Telephone</span>
                <input className="rig-saisie" value={d.telephone ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Siren</span>
                <input className="rig-saisie" value={d.siren ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Coffre-fort : nature du document</span>
                <input className="rig-saisie" value={d.natureCoffreFort ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Coffre-fort</span>
                <input className="rig-saisie" value={d.coffreFort ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Tribunal digital : nature du document</span>
                <input className="rig-saisie" value={d.natureTribunalDigital ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Tribunal digital</span>
                <input className="rig-saisie" value={d.tribunalDigital ?? ''} readOnly /></label>
              <label className="rig-champ-ed rig-champ-ed--large"><span>Tribunal digital : Erreur</span>
                <input className="rig-saisie" value={d.erreurTribunalDigital ?? ''} readOnly /></label>
              <label className="rig-champ-ed"><span>Courriel</span>
                <input className="rig-saisie" value={d.courriel ?? ''} readOnly /></label>
              <label className="rig-champ-ed rig-champ-ed--case">
                <input type="checkbox" checked={d.conserverPjOrigine} disabled />
                <span>Conserver les PJ au format d'origine (ne pas convertir en PDF)</span></label>
              <label className="rig-champ-ed rig-champ-ed--large"><span>Observation</span>
                <textarea className="rig-saisie" rows={2} readOnly value={d.observation ?? ''}></textarea></label>
            </>
          ) : (
            <div style={{ fontSize: '11px', color: 'var(--rig-texte-3)' }}>
              Choisissez un destinataire dans le tableau des editions.
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
