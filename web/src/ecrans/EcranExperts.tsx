// Ecran de la procedure PROC_EXPERT.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_EXPERT) :
//   - Recherche/OPE_RECHERCHE_EXPERT : TextRecherche, DataListviewExpert
//   - Edition/OPE_EDITION_EXPERT     : ComboBoxCivilite, TextNom, TextAbrege,
//     TextCompNom, TextDateNaissance, TextAdresse, TextTel, TextFax, TextMail,
//     ComboBoxQualifProfession, CheckBoxActif, OpeMesureActif / OpeMesureInactif
//   - Edition/OPE_MESURE_EXPERT -> Rig_Ult_Metier/LISTVIEW/
//     ULT_LISTVIEW_MESURES_EXPERT.cs, dont InitColumns() donne les SIX colonnes
//     reprises ici : Num mesure, Civilite, Nom participant, Complement nom,
//     Qualif prof, Adresse.
//
// Porte depuis Components/Ecrans/EcranExperts.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses experts et
// leurs mesures de demonstration l'ont suivi ici.

import { useState } from 'react'
import { contientSansCasse, distinctsTries, estBlanc } from '../criteres'

interface Expert {
  abrege: string
  civilite: string
  nom: string
  complementNom: string
  naissance: string
  adresse: string
  tel: string
  fax: string
  mail: string
  qualif: string
  actif: boolean
}

// Colonnes de ULT_LISTVIEW_MESURES_EXPERT.InitColumns() (RIG v2) :
// MSINS_NUM_MESURE, PRTCP_CIVILITE, PRTCP_NOM, PRTCP_COMPLEMENT_NOM,
// PRTCP_QUALIF_PROFESSION, Adresse.AdresseComplete.
interface MesureExpert {
  expert: string
  numMesure: string
  civilite: string
  nomParticipant: string
  complementNom: string
  qualifProf: string
  adresse: string
  enCours: boolean
}

const Experts: Expert[] = [
  { abrege: 'GIRARD', civilite: 'M.', nom: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', naissance: '14/05/1971', adresse: '9 rue Gambetta, 51100 Reims', tel: '03 26 12 30 44', fax: '', mail: 'p.girard@exemple.fr', qualif: 'Expert-comptable', actif: true },
  { abrege: 'NOEL', civilite: 'Mme', nom: 'NOEL Julie', complementNom: '', naissance: '02/11/1980', adresse: '5 bd Foch, 51100 Reims', tel: '03 26 44 12 87', fax: '03 26 44 12 88', mail: 'j.noel@exemple.fr', qualif: 'Expert en evaluation', actif: true },
  { abrege: 'ROUX', civilite: 'M.', nom: 'ROUX Bernard', complementNom: 'SELARL Roux Expertise', naissance: '27/03/1958', adresse: '18 rue Talleyrand, 51100 Reims', tel: '03 26 05 76 21', fax: '', mail: 'b.roux@exemple.fr', qualif: 'Expert immobilier', actif: false },
]

const Mesures: MesureExpert[] = [
  { expert: 'GIRARD', numMesure: 'M-2026-0114', civilite: 'M.', nomParticipant: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', qualifProf: 'Expert-comptable', adresse: '9 rue Gambetta, 51100 Reims', enCours: true },
  { expert: 'GIRARD', numMesure: 'M-2024-0503', civilite: 'M.', nomParticipant: 'GIRARD Paul', complementNom: 'Cabinet Girard & Fils', qualifProf: 'Expert-comptable', adresse: '9 rue Gambetta, 51100 Reims', enCours: false },
  { expert: 'NOEL', numMesure: 'M-2026-0128', civilite: 'Mme', nomParticipant: 'NOEL Julie', complementNom: '', qualifProf: 'Expert en evaluation', adresse: '5 bd Foch, 51100 Reims', enCours: true },
  { expert: 'ROUX', numMesure: 'M-2025-0087', civilite: 'M.', nomParticipant: 'ROUX Bernard', complementNom: 'SELARL Roux Expertise', qualifProf: 'Expert immobilier', adresse: '18 rue Talleyrand, 51100 Reims', enCours: false },
]

const Qualifs = distinctsTries(Experts.map((e) => e.qualif))

export function EcranExperts() {
  const [recherche, setRecherche] = useState('')
  const [critQualif, setCritQualif] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  const [mesuresActives, setMesuresActives] = useState(true)
  const [selection, setSelection] = useState<Expert | null>(Experts[0]!)

  let trouves = Experts
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((e) => contientSansCasse(e.nom, t) || contientSansCasse(e.abrege, t))
  }
  if (critQualif !== '') trouves = trouves.filter((e) => e.qualif === critQualif)
  if (actifsSeuls) trouves = trouves.filter((e) => e.actif)
  const e = selection
  const mesures = e ? Mesures.filter((m) => m.expert === e.abrege && m.enCours === mesuresActives) : []

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche d'un expert</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom, abrege</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(ev) => setRecherche(ev.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Qualite / profession</span>
          <select className="rig-saisie-champ__saisie" value={critQualif} onChange={(ev) => setCritQualif(ev.target.value)}>
            <option value="">Toutes</option>
            {Qualifs.map((q) => <option key={q} value={q}>{q}</option>)}
          </select>
        </label>

        <button className={`rig-bascule${actifsSeuls ? ' rig-bascule--actif' : ''}`}
                onClick={() => setActifsSeuls(!actifsSeuls)}>
          <span className="rig-bascule__temoin"></span>
          <span>Experts actifs uniquement</span>
        </button>

      </aside>

      <section className="rig-resultats-proc">
        {/* Liste de recherche : les CINQ colonnes de DataListviewExpert en RIG v2
            (Recherche/OPE_RECHERCHE_EXPERT.cs, appels AddColumns) --
              Abrege   EXPRT_ABREGE
              Civilite EXPRT_CIVILITE
              Nom      EXPRT_NOM
              Mail     EXPRT_ADRESSE_MAIL
              Actif    EXPRT_EST_ACTIF
            Elles ne tenaient pas dans la colonne de criteres (320px) : la liste
            occupe donc le haut du plan de travail, comme en v2 ou la recherche
            precede l'edition. */}
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Liste des experts</div>
          <span className="rig-resultats-proc__compte">{trouves.length} expert(s)</span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                <th>Abrege</th>
                <th>Civilite</th>
                <th>Nom</th>
                <th>Mail</th>
                <th>Actif</th>
              </tr>
            </thead>
            <tbody>
              {trouves.map((ex) => (
                <tr key={ex.abrege} onClick={() => setSelection(ex)}
                    style={selection?.abrege === ex.abrege ? { boxShadow: 'inset 3px 0 0 0 var(--rig-focus)' } : undefined}>
                  <td className="rig-table__mono">{ex.abrege}</td>
                  <td>{ex.civilite}</td>
                  <td>{ex.nom}</td>
                  <td>{ex.mail}</td>
                  <td className="rig-table__centre">
                    <span className={`rig-etat rig-etat--${ex.actif ? 'ok' : 'attente'}`}>{ex.actif ? 'Actif' : 'Inactif'}</span>
                  </td>
                </tr>
              ))}
              {trouves.length === 0 && (
                <tr className="rig-table__ligne--inerte"><td colSpan={5} className="rig-table__vide">Aucun expert ne repond a ces criteres.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Edition d'un expert</div>
          <span className="rig-resultats-proc__compte">{selection?.abrege ?? 'aucun expert selectionne'}</span>
        </div>

        {e ? (
          <>
            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Identite</div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Civilite</span>
                  <input className="rig-saisie-champ__saisie" value={e.civilite} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Abrege</span>
                  <input className="rig-saisie-champ__saisie" value={e.abrege} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Nom</span>
                  <input className="rig-saisie-champ__saisie" value={e.nom} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Complement de nom</span>
                  <input className="rig-saisie-champ__saisie" value={e.complementNom} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Date de naissance</span>
                <input className="rig-saisie-champ__saisie" value={e.naissance} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Adresse</span>
                <input className="rig-saisie-champ__saisie" value={e.adresse} readOnly />
              </label>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Telephone</span>
                  <input className="rig-saisie-champ__saisie" value={e.tel} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Fax</span>
                  <input className="rig-saisie-champ__saisie" value={e.fax} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">E-mail</span>
                <input className="rig-saisie-champ__saisie" value={e.mail} readOnly />
              </label>
              <span className={`rig-etat rig-etat--${e.actif ? 'ok' : 'attente'}`}>{e.actif ? 'Actif' : 'Inactif'}</span>
            </div>

            <div className="rig-resultats-proc__entete">
              <div className="rig-onglets">
                <button className={`rig-onglet${mesuresActives ? ' rig-onglet--actif' : ''}`}
                        onClick={() => setMesuresActives(true)}>Mesures en cours</button>
                <button className={`rig-onglet${!mesuresActives ? ' rig-onglet--actif' : ''}`}
                        onClick={() => setMesuresActives(false)}>Mesures terminees</button>
              </div>
            </div>

            <div className="rig-table-cadre">
              <table className="rig-table">
                <thead>
                  <tr>
                    <th>Num mesure</th>
                    <th>Civilite</th>
                    <th>Nom participant</th>
                    <th>Complement nom</th>
                    <th>Qualif prof</th>
                    <th>Adresse</th>
                  </tr>
                </thead>
                <tbody>
                  {mesures.map((m) => (
                    <tr key={m.numMesure}>
                      <td className="rig-table__mono">{m.numMesure}</td>
                      <td>{m.civilite}</td>
                      <td>{m.nomParticipant}</td>
                      <td>{m.complementNom}</td>
                      <td>{m.qualifProf}</td>
                      <td>{m.adresse}</td>
                    </tr>
                  ))}
                  {mesures.length === 0 && (
                    <tr><td colSpan={6} className="rig-table__vide">Aucune mesure dans cette liste.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez un expert dans la liste de gauche.
          </div>
        )}
      </section>
    </div>
  )
}
