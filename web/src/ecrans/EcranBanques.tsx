// Ecran de la procedure PROC_GESTBANQ (RIG v2 : « Gestion des banques »,
// blocs OPE_BANQIDENTITE, OPE_BANQADRESSE, OPE_BANQCHEQUE (parametres des
// lettres cheques) et OPE_PARAMCOMPTABLE). Colonnes lues dans la grille v2 :
// Code (BANQU_CODE), Designation (BANQU_NOM), Compte (BANQU_COMPTE),
// Modes associes (GetListeModePaiementBanque).
//
// Porte depuis Components/Ecrans/EcranBanques.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses banques de
// demonstration l'ont suivi ici.
//
// LISTE MODERNE « Tout reuni » (24/11/2026, plan des listes modernes, lot 4) : la liste prend les briques de
// composants/ListeModerne.tsx -- tete, une ligne par banque (avatar, designation, code), modes de paiement en
// pastilles, pied. Ni montant, ni critere d'etat, ni plus de quelques banques par greffe : pas de carte, pas de
// vue, pas de « Aller a… ». Le clic qui montre la fiche ne change pas.

import { useState } from 'react'
import { contientSansCasse, estBlanc } from '../criteres'
import { CelluleIdentite, EnteteListe, ListeModerne, Pastille, PiedListe, type Teinte } from '../composants/ListeModerne'

// v2 : OPE_BANQIDENTITE (ult_BanqCode, ult_BanqNom), OPE_BANQADRESSE,
// OPE_PARAMCOMPTABLE, et OPE_BANQCHEQUE (ult_CBModel, ult_NumLettreCheque,
// ult_ChFournisseur, ult_ChNotification, ult_ChTropPercu).
interface Banque {
  code: string
  designation: string
  compte: string
  journal: string
  adresse: string
  maquetteCheque: string
  modes: string[]
  numLettreCheque: string
  chequeFournisseur: boolean
  chequeNotification: boolean
  chequeTropPercu: boolean
}

const Banques: Banque[] = [
  { code: 'CA', designation: 'CREDIT AGRICOLE', compte: '512100', journal: 'BQ1', adresse: '25 rue de Vesle\n51100 Reims', maquetteCheque: 'LC_STANDARD', modes: ['Cheque', 'Virement'], numLettreCheque: 'LC-2026-0450', chequeFournisseur: true, chequeNotification: true, chequeTropPercu: true },
  { code: 'BP', designation: 'BANQUE POPULAIRE', compte: '512200', journal: 'BQ2', adresse: '8 place d\'Erlon\n51100 Reims', maquetteCheque: 'LC_STANDARD', modes: ['Cheque'], numLettreCheque: 'LC-2026-0120', chequeFournisseur: true, chequeNotification: false, chequeTropPercu: false },
  { code: 'LBP', designation: 'LA BANQUE POSTALE', compte: '512300', journal: 'BQ3', adresse: '1 rue Cerf-Volant\n51100 Reims', maquetteCheque: 'LC_POSTALE', modes: ['Cheque', 'Virement', 'Carte bancaire'], numLettreCheque: 'LC-2026-0088', chequeFournisseur: false, chequeNotification: true, chequeTropPercu: true },
  { code: 'CAI', designation: 'CAISSE (especes)', compte: '531000', journal: 'CA1', adresse: '', maquetteCheque: '', modes: ['Especes'], numLettreCheque: '', chequeFournisseur: false, chequeNotification: false, chequeTropPercu: false },
]

const TeinteMode: Record<string, Teinte> = { Cheque: 'bleu', Virement: 'violet', 'Carte bancaire': 'orange', Especes: 'vert' }

export function EcranBanques() {
  const [recherche, setRecherche] = useState('')
  const [selection, setSelection] = useState<Banque | null>(Banques[0]!)

  let trouvees = Banques
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouvees = Banques.filter((b) => contientSansCasse(b.code, t) || contientSansCasse(b.designation, t))
  }
  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Banques</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Code ou designation</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Liste ({trouvees.length})</div>
          {trouvees.map((b) => (
            <button key={b.code} className={`rig-bascule${selection?.code === b.code ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setSelection(b)}>
              <span className="rig-bascule__temoin"></span>
              <span>{b.code} — {b.designation}</span>
            </button>
          ))}
        </div>
      </aside>

      <ListeModerne etiquette="Banques declarees">
        <EnteteListe titre="Banques declarees" synthese={`${trouvees.length}${trouvees.length > 1 ? ' banques' : ' banque'}`} />

        <div className="rig-table-cadre">
          <table className="rig-table rig-lm-table">
            <colgroup><col /><col style={{ width: '16%' }} /><col style={{ width: '44%' }} /></colgroup>
            <thead>
              <tr>
                <th>Banque</th>
                <th>Compte</th>
                <th>Modes associes</th>
              </tr>
            </thead>
            <tbody>
              {trouvees.map((banque) => (
                <tr key={banque.code} onClick={() => setSelection(banque)}>
                  <CelluleIdentite nom={banque.designation} objet={banque.code} titre={`${banque.designation} · ${banque.code}`} />
                  <td className="rig-table__mono">{banque.compte}</td>
                  <td title={banque.modes.join(', ')}>
                    {banque.modes.map((m, i) => (
                      <span key={m}>{i > 0 && ' '}<Pastille teinte={TeinteMode[m] ?? 'gris'}>{m}</Pastille></span>
                    ))}
                  </td>
                </tr>
              ))}
              {trouvees.length === 0 && (
                <tr><td colSpan={3} className="rig-table__vide">Aucune banque ne repond a cette recherche.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {s && (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-kicker">Identite</div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Code</span>
                <input className="rig-saisie-champ__saisie" value={s.code} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Designation</span>
                <input className="rig-saisie-champ__saisie" value={s.designation} readOnly />
              </label>
            </div>
            <div className="rig-kicker">Adresse</div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Adresse de l'agence</span>
              <textarea className="rig-saisie-champ__saisie" rows={2} readOnly value={s.adresse} />
            </label>
            <div className="rig-kicker">Parametre comptable</div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Compte comptable</span>
                <input className="rig-saisie-champ__saisie" value={s.compte} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Journal</span>
                <input className="rig-saisie-champ__saisie" value={s.journal} readOnly />
              </label>
            </div>
            <div className="rig-kicker">Parametres des lettres cheques</div>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Maquette de lettre cheque</span>
                <input className="rig-saisie-champ__saisie" value={s.maquetteCheque} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Num lettre cheque</span>
                <input className="rig-saisie-champ__saisie" value={s.numLettreCheque} readOnly />
              </label>
            </div>

            {/* v2 (OPE_BANQCHEQUE) : ult_ChFournisseur, ult_ChNotification,
                ult_ChTropPercu. */}
            <div className="rig-criteres__bloc">
              <div className="rig-kicker">Emissions autorisees</div>
              <span className="rig-chip">{s.chequeFournisseur ? 'Fournisseur : oui' : 'Fournisseur : non'}</span>
              <span className="rig-chip">{s.chequeNotification ? 'Notification : oui' : 'Notification : non'}</span>
              <span className="rig-chip">{s.chequeTropPercu ? 'Trop-percu : oui' : 'Trop-percu : non'}</span>
            </div>

            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Modes de paiement</span>
              <input className="rig-saisie-champ__saisie" value={s.modes.join(', ')} readOnly />
            </label>
          </div>
        )}

        <PiedListe aide="Clic sur une ligne pour voir la fiche de la banque.">
          <span><b>{trouvees.length}</b>{trouvees.length > 1 ? ' banques affichees' : ' banque affichee'}</span>
          <span>Fichier <b>{Banques.length}</b></span>
        </PiedListe>
      </ListeModerne>
    </div>
  )
}
