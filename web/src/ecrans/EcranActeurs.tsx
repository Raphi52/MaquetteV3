// Ecran de la procedure PROC_ACTEUR_JUD.
//
// Sources RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_ACTEUR_JUD) :
//   - Recherche/OPE_RECHERCHE_ACTEUR : textRecherche, comboBoxChambre,
//     comboBoxStatut, _ultActiveOnly, dataListviewActeurs
//   - Edition/ : v2 n a PAS une fiche unique mais SIX formulaires, un par
//     type d acteur, chacun avec ses champs propres :
//       OPE_EDITION_GJUGE     juge      -> chambre, section, signature
//                                          dematerialisee et ONZE roles
//                                          (juge-commissaire et suppleant,
//                                          expertise, injonction, ordonnance,
//                                          prevention, rapporteur, RCS civil,
//                                          RCS commercial, president d audience,
//                                          de juridiction, de refere)
//       OPE_EDITION_GMANDPC   mandataire-> SIREN, coffre-fort, et cinq roles
//                                          (administrateur judiciaire,
//                                          commissaire a l execution du plan,
//                                          commissaire priseur, mandataire
//                                          conciliation, RDC liquidation MDJ)
//       OPE_EDITION_GGREFFIER greffier  -> statut et compte utilisateur lie
//       OPE_EDITION_GPARQU    parquet   -> identite seule
//       OPE_EDITION_GINTERV   intervenant -> coffre-fort et date de demarrage
//       OPE_EDITION_EXPERT    expert    -> qualification professionnelle
// La maquette rend ce choix visible : le type commande les champs affiches.
//
// Porte depuis Components/Ecrans/EcranActeurs.razor (lot 4) : meme balisage,
// memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses acteurs de
// demonstration l'ont suivi ici.

import { useState } from 'react'
import { contientSansCasse, dateCourte, distinctsTries, estBlanc } from '../criteres'

interface Acteur {
  abrege: string
  civilite: string
  nom: string
  prenom: string
  complementNom: string
  adresse: string
  tel: string
  fax: string
  mail: string
  qualif: string
  statut: string
  chambre: string
  section: string
  /** aaaa-mm-jj */
  debut: string
  /** aaaa-mm-jj, ou null */
  fin: string | null
  bureau: string
  nomCoffre: string
  actif: boolean
  type: string
  siren: string
  utilisateur: string
}

// Roles declares dans OPE_EDITION_GJUGE.Designer.cs (RIG v2).
const RolesJuge = [
  'Juge-commissaire', 'Juge-commissaire suppleant', 'Juge expertise',
  'Juge injonction', 'Juge ordonnance', 'Juge prevention', 'Juge rapporteur',
  'Juge RCS civil', 'Juge RCS commercial', 'President d\'audience',
  'President de juridiction', 'President de refere',
]

// Roles declares dans OPE_EDITION_GMANDPC.Designer.cs (RIG v2).
const RolesMandataire = [
  'Administrateur judiciaire', 'Commissaire a l\'execution du plan',
  'Commissaire priseur', 'Mandataire conciliation', 'RDC liquidation MDJ',
]

const Acteurs: Acteur[] = [
  { abrege: 'LEROY', civilite: 'Maitre', nom: 'LEROY', prenom: 'Antoine', complementNom: 'Selarl LEROY & associes', adresse: '12 rue du Palais, 51100 Reims', tel: '03 26 40 11 22', fax: '03 26 40 11 23', mail: 'a.leroy@exemple.fr', qualif: 'Mandataire judiciaire', statut: 'En fonction', chambre: '1re', section: 'A', debut: '2018-01-08', fin: null, bureau: 'B-104', nomCoffre: 'COFFRE_LEROY', actif: true, type: 'Mandataire', siren: '402 118 553', utilisateur: '' },
  { abrege: 'BLANC', civilite: 'Maitre', nom: 'BLANC', prenom: 'Sophie', complementNom: '', adresse: '3 place Drouet, 51100 Reims', tel: '03 26 40 55 01', fax: '', mail: 's.blanc@exemple.fr', qualif: 'Administrateur judiciaire', statut: 'En fonction', chambre: '2e', section: 'B', debut: '2020-09-01', fin: null, bureau: 'B-210', nomCoffre: 'COFFRE_BLANC', actif: true, type: 'Mandataire', siren: '517 904 220', utilisateur: '' },
  { abrege: 'PETIT', civilite: 'M.', nom: 'PETIT', prenom: 'Jean', complementNom: '', adresse: '8 av. de Laon, 51100 Reims', tel: '03 26 88 90 12', fax: '', mail: 'j.petit@exemple.fr', qualif: 'Juge consulaire', statut: 'En fonction', chambre: '3e', section: 'A', debut: '2022-04-15', fin: null, bureau: 'A-011', nomCoffre: '', actif: true, type: 'Juge', siren: '', utilisateur: '' },
  { abrege: 'DUVAL', civilite: 'Mme', nom: 'DUVAL', prenom: 'Helene', complementNom: '', adresse: '1 rue du Greffe, 51100 Reims', tel: '03 26 12 00 40', fax: '', mail: 'h.duval@exemple.fr', qualif: 'Greffier', statut: 'En fonction', chambre: '1re', section: 'A', debut: '2019-03-04', fin: null, bureau: 'A-002', nomCoffre: '', actif: true, type: 'Greffier', siren: '', utilisateur: 'hduval' },
  { abrege: 'MOREAU', civilite: 'Mme', nom: 'MOREAU', prenom: 'Claire', complementNom: '', adresse: '22 rue Chanzy, 51100 Reims', tel: '03 26 77 43 09', fax: '', mail: 'c.moreau@exemple.fr', qualif: 'Expert-comptable', statut: 'Retiree', chambre: '', section: '', debut: '2012-02-01', fin: '2024-12-31', bureau: '', nomCoffre: '', actif: false, type: 'Expert', siren: '', utilisateur: '' },
]

const Statuts = distinctsTries(Acteurs.map((a) => a.statut))

export function EcranActeurs() {
  const [recherche, setRecherche] = useState('')
  const [critChambre, setCritChambre] = useState('')
  const [critStatut, setCritStatut] = useState('')
  const [actifsSeuls, setActifsSeuls] = useState(true)
  const [selection, setSelection] = useState<Acteur | null>(Acteurs[0]!)

  let trouves = Acteurs
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((a) => contientSansCasse(a.nom, t) || contientSansCasse(a.abrege, t))
  }
  if (critChambre !== '') trouves = trouves.filter((a) => a.chambre === critChambre)
  if (critStatut !== '') trouves = trouves.filter((a) => a.statut === critStatut)
  if (actifsSeuls) trouves = trouves.filter((a) => a.actif)
  const a = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Recherche d'un acteur</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nom ou abrege</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-criteres__paire">
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Chambre</span>
            <select className="rig-saisie-champ__saisie" value={critChambre} onChange={(e) => setCritChambre(e.target.value)}>
              <option value="">Toutes</option>
              <option>1re</option>
              <option>2e</option>
              <option>3e</option>
            </select>
          </label>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Statut</span>
            <select className="rig-saisie-champ__saisie" value={critStatut} onChange={(e) => setCritStatut(e.target.value)}>
              <option value="">Tous</option>
              {Statuts.map((st) => <option key={st} value={st}>{st}</option>)}
            </select>
          </label>
        </div>

        <button className={`rig-bascule${actifsSeuls ? ' rig-bascule--actif' : ''}`}
                onClick={() => setActifsSeuls(!actifsSeuls)}>
          <span className="rig-bascule__temoin"></span>
          <span>Acteurs actifs uniquement</span>
        </button>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Resultats ({trouves.length})</div>
          {trouves.map((acteur) => (
            <button key={acteur.abrege} className={`rig-bascule${selection?.abrege === acteur.abrege ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setSelection(acteur)}>
              <span className="rig-bascule__temoin"></span>
              <span>{acteur.abrege} — {acteur.nom}</span>
            </button>
          ))}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Edition d'un acteur</div>
          <span className="rig-resultats-proc__compte">{selection === null ? 'aucun acteur selectionne' : selection.abrege}</span>
        </div>

        {a ? (
          <>
            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Identite</div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Civilite</span>
                  <input className="rig-saisie-champ__saisie" value={a.civilite} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Abrege</span>
                  <input className="rig-saisie-champ__saisie" value={a.abrege} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Nom</span>
                  <input className="rig-saisie-champ__saisie" value={a.nom} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Prenom</span>
                  <input className="rig-saisie-champ__saisie" value={a.prenom} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Complement de nom</span>
                <input className="rig-saisie-champ__saisie" value={a.complementNom} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Adresse</span>
                <input className="rig-saisie-champ__saisie" value={a.adresse} readOnly />
              </label>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Telephone</span>
                  <input className="rig-saisie-champ__saisie" value={a.tel} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Fax</span>
                  <input className="rig-saisie-champ__saisie" value={a.fax} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">E-mail</span>
                <input className="rig-saisie-champ__saisie" value={a.mail} readOnly />
              </label>
            </div>

            <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
              <div className="rig-kicker">Fonction</div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Qualite / profession</span>
                  <input className="rig-saisie-champ__saisie" value={a.qualif} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Statut</span>
                  <input className="rig-saisie-champ__saisie" value={a.statut} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Chambre</span>
                  <input className="rig-saisie-champ__saisie" value={a.chambre} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Section</span>
                  <input className="rig-saisie-champ__saisie" value={a.section} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Debut de fonction</span>
                  <input className="rig-saisie-champ__saisie" value={dateCourte(a.debut)} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Fin de fonction</span>
                  <input className="rig-saisie-champ__saisie" value={a.fin === null ? '' : dateCourte(a.fin)} readOnly />
                </label>
              </div>
              <div className="rig-criteres__paire">
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Bureau</span>
                  <input className="rig-saisie-champ__saisie" value={a.bureau} readOnly />
                </label>
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Nom du coffre</span>
                  <input className="rig-saisie-champ__saisie" value={a.nomCoffre} readOnly />
                </label>
              </div>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Type d'acteur</span>
                <input className="rig-saisie-champ__saisie" value={a.type} readOnly />
              </label>

              {/* v2 : OPE_EDITION_GJUGE, onze roles propres au juge. */}
              {a.type === 'Juge' && (
                <div className="rig-criteres__bloc">
                  <div className="rig-kicker">Roles du juge</div>
                  {RolesJuge.map((role) => <span key={role} className="rig-chip">{role}</span>)}
                </div>
              )}

              {/* v2 : OPE_EDITION_GMANDPC, SIREN et cinq roles. */}
              {a.type === 'Mandataire' && (
                <>
                  <label className="rig-saisie-champ">
                    <span className="rig-saisie-champ__libelle">SIREN</span>
                    <input className="rig-saisie-champ__saisie" value={a.siren} readOnly />
                  </label>
                  <div className="rig-criteres__bloc">
                    <div className="rig-kicker">Roles du mandataire</div>
                    {RolesMandataire.map((role) => <span key={role} className="rig-chip">{role}</span>)}
                  </div>
                </>
              )}

              {/* v2 : OPE_EDITION_GGREFFIER, compte utilisateur lie. */}
              {a.type === 'Greffier' && (
                <label className="rig-saisie-champ">
                  <span className="rig-saisie-champ__libelle">Compte utilisateur lie</span>
                  <input className="rig-saisie-champ__saisie" value={a.utilisateur} readOnly />
                </label>
              )}

              <span className={`rig-etat rig-etat--${a.actif ? 'ok' : 'attente'}`}>{a.actif ? 'Actif' : 'Inactif'}</span>
            </div>
          </>
        ) : (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez un acteur dans la liste de gauche.
          </div>
        )}
      </section>
    </div>
  )
}
