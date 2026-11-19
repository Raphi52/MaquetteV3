// Ecran de la procedure PROC_RECHAFF — recherche d'affaires.
//
// Reference RIG v2 (D:/RigApplication/Source/RIG/DLL/Processus/PROC_RECHAFF) :
//   - Operations/OPE_CRITERES.cs : n° affaire, n° demande, n° facture, dates
//   - Ults/ULT_GRIDVIEW_RECHAFF.cs : colonnes N° Affaire, Libelle,
//     Date creation, Solde, Demandes, Factures.
//
// Les criteres d'INSTANCE (id instance, n° de role, code affaire, nom
// instance ou partie, etat, fenetre de saisine) et le panneau de suppression
// XXAFF viennent de l'ecran « Instances judiciaires » : la recherche
// d'affaire doit couvrir ces champs sans obliger a changer d'ecran.
//
// Deux differences ASSUMEES avec v2, parce que ce n'est pas un portage :
//   - aucun rattachement de demande a l'affaire. L'ecran ne fait que LIRE.
//   - noAffaire pre-remplit le critere et lance la recherche (tuile « Mes
//     affaires recentes »).
//
// Porte depuis Components/Ecrans/EcranRechAffaires.razor (lot 3) : meme
// balisage, memes classes CSS. Le fonds d'affaires, le filtre et le TRI (ordre
// des chaines .NET) sont faits par Pont/EcranRechAffaires.cs ; ici restent les
// criteres saisis, la colonne de tri choisie, la selection et la suppression
// simulee. La coquille .razor monte cet ecran et recoit ouvrir / ouvrirUnDossier.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { EnteteTriable } from '../composants/EnteteTriable'
import { dateSaisie } from '../saisie'

export interface NatureAffaire {
  code: string
  nom: string
}

export interface LigneAffaire {
  numero: string
  idInstance: string
  role: string
  codeAffaire: string
  nature: string
  nomNature: string
  libelle: string
  nom: string
  etat: string
  siren: string
  type: string
  saisine: string
  creation: string
  /** Le solde deja mis en forme (« 142,50 »), comme le ToString("N2") de Blazor. */
  solde: string
}

export interface AffaireChoisie {
  numero: string
  idInstance: string
  role: string
}

export interface VueAffaires {
  natures: NatureAffaire[]
  etats: string[]
  lignes: LigneAffaire[]
  /** « 7 instance, 3 procedure collective, … » */
  resume: string
  aucunCritere: boolean
  sirenCherche: string
  choisie: AffaireChoisie | null
}

export interface ProprietesEcranRechAffaires {
  /** Numero d'affaire pre-rempli a l'ouverture (tuile de l'accueil). */
  noAffaire?: string | null
  /** Remonte le numero choisi ; la page ouvre la visionneuse. */
  ouvrir: (numero: string) => void
  /**
   * Remonte un SIREN a immatriculer : la recherche n'a rien trouve, c'est donc
   * ICI que l'agent constate qu'il faut ouvrir le dossier. Le renvoyer au menu
   * lui ferait retaper le numero.
   */
  ouvrirUnDossier: (siren: string) => void
}

/** Les colonnes de la grille : libelle affiche et cle de tri comprise par le pont. */
const Colonnes: ReadonlyArray<readonly [libelle: string, cle: string]> = [
  ['N° Affaire', 'Numero'],
  ['Id instance', 'IdInstance'],
  ['N° de role', 'Role'],
  ['Code affaire', 'CodeAffaire'],
  ['Nature', 'Nature'],
  ['Libelle', 'Libelle'],
  ['Nom', 'Nom'],
  ['Etat', 'Etat'],
  ['SIREN', 'Siren'],
  ['Domaine', 'Type'],
  ['Saisine', 'Saisine'],
  ['Date creation', 'Creation'],
  ['Solde', 'Solde'],
]

export function EcranRechAffaires({ noAffaire, ouvrir, ouvrirUnDossier }: ProprietesEcranRechAffaires) {
  // Le critere n'est pose qu'a l'OUVERTURE : ensuite l'utilisateur est maitre
  // de ses criteres, un nouveau rendu ne doit pas les ecraser.
  const [critNumero, setCritNumero] = useState(() => (noAffaire ?? '').trim())
  const [critLibelle, setCritLibelle] = useState('')
  const [critNature, setCritNature] = useState('')
  const [critType, setCritType] = useState('')
  const [critIdInstance, setCritIdInstance] = useState('')
  const [critRole, setCritRole] = useState('')
  const [critCodeAffaire, setCritCodeAffaire] = useState('')
  const [critEtat, setCritEtat] = useState('')
  const [critNom, setCritNom] = useState('')
  const [critSaisineDu, setCritSaisineDu] = useState('')
  const [critSaisineAu, setCritSaisineAu] = useState('')

  const [selection, setSelection] = useState<string | null>(null)
  const [libererNumeros, setLibererNumeros] = useState(true)
  const [message, setMessage] = useState<string | null>(null)

  // La colonne sur laquelle la grille est rangee ; null = ordre d'origine
  // (du plus recent au plus ancien).
  const [cleTri, setCleTri] = useState<string | null>(null)
  const [triAscendant, setTriAscendant] = useState(true)

  // null tant que le pont n'a pas repondu : ni ligne ni « aucune affaire ».
  const [vue, setVue] = useState<VueAffaires | null>(null)

  useEffect(() => {
    let perime = false
    void appeler<VueAffaires>('EcranRechAffaires.Trouver', {
      critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu: dateSaisie(critSaisineDu), critSaisineAu: dateSaisie(critSaisineAu),
    }, cleTri, triAscendant, selection).then((v) => { if (!perime) setVue(v) })
    return () => { perime = true }
  }, [critNumero, critLibelle, critType, critNature, critIdInstance, critRole, critCodeAffaire,
      critNom, critEtat, critSaisineDu, critSaisineAu, cleTri, triAscendant, selection])

  const trouvees = vue?.lignes ?? []
  const choisie = vue?.choisie ?? null

  /** Premier clic : ordre croissant. Second clic sur la MEME colonne : ordre inverse. C'est le geste de RIG v2. */
  function basculerTri(cle: string) {
    if (cleTri === cle) setTriAscendant(!triAscendant)
    else { setCleTri(cle); setTriAscendant(true) }
  }

  /** Le filtrage est continu ; le bouton efface seulement le compte rendu. */
  function rechercher() {
    setMessage(null)
  }

  function vider() {
    setCritNumero('')
    setCritLibelle('')
    setCritType('')
    setCritNature('')
    setCritIdInstance('')
    setCritRole('')
    setCritCodeAffaire('')
    setCritNom('')
    setCritEtat('')
    setCritSaisineDu('')
    setCritSaisineAu('')
    setSelection(null)
    setMessage(null)
  }

  /**
   * Ecran de demonstration : la suppression XXAFF n'ecrit rien, elle
   * rend compte de ce qui serait supprime et du sort des numeros metiers.
   */
  function supprimer() {
    if (choisie === null) return
    setMessage(`Instance ${choisie.idInstance} (${choisie.numero}) : suppression simulee`
      + (libererNumeros ? ' — numeros metiers liberes.' : ' — numeros metiers conserves.'))
  }

  return (
    <div className="rig-proc__corps">

      {/* Entree dans le bloc des criteres = clic sur Rechercher. C'est le geste
          de RIG v2 : on tape, on valide, sans lacher le clavier. */}
      <aside className="rig-criteres" onKeyDown={(e) => { if (e.key === 'Enter') rechercher() }}>
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Criteres de recherche</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">N° Affaire</span>
          <input className="rig-saisie-champ__saisie" value={critNumero} onChange={(e) => setCritNumero(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Libelle, SIREN</span>
          <input className="rig-saisie-champ__saisie" value={critLibelle} onChange={(e) => setCritLibelle(e.target.value)} />
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Nature</span>
          <select className="rig-saisie-champ__saisie" value={critNature} onChange={(e) => setCritNature(e.target.value)}>
            <option value="">Toutes</option>
            {(vue?.natures ?? []).map((n) => (
              <option key={n.code} value={n.code}>{n.nom}</option>
            ))}
          </select>
        </label>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Domaine</span>
          <select className="rig-saisie-champ__saisie" value={critType} onChange={(e) => setCritType(e.target.value)}>
            <option value="">Tous</option>
            <option value="RCS">RCS</option>
            <option value="JUD">Judiciaire</option>
          </select>
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Instance judiciaire</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Id instance</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 55339"
                     value={critIdInstance} onChange={(e) => setCritIdInstance(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Numero de role</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 2024F00123"
                     value={critRole} onChange={(e) => setCritRole(e.target.value)} />
            </label>
          </div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Code affaire</span>
              <input className="rig-saisie-champ__saisie" placeholder="ex. 010"
                     value={critCodeAffaire} onChange={(e) => setCritCodeAffaire(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Etat</span>
              <select className="rig-saisie-champ__saisie" value={critEtat} onChange={(e) => setCritEtat(e.target.value)}>
                <option value="">Tous</option>
                {(vue?.etats ?? []).map((e) => (
                  <option key={e} value={e}>{e}</option>
                ))}
              </select>
            </label>
          </div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Nom (instance ou partie)</span>
            <input className="rig-saisie-champ__saisie" placeholder="ex. DUPONT"
                   value={critNom} onChange={(e) => setCritNom(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Saisine</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Du</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineDu} onChange={(e) => setCritSaisineDu(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Au</span>
              <input className="rig-saisie-champ__saisie" type="date"
                     value={critSaisineAu} onChange={(e) => setCritSaisineAu(e.target.value)} />
            </label>
          </div>
        </div>

        <div className="rig-criteres__actions">
          <div className="rig-criteres__paire">
            <button className="rig-bouton rig-bouton--fort" onClick={rechercher}>Rechercher</button>
            <button className="rig-bouton" onClick={vider}>Reinitialiser</button>
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Affaires</div>
          <span className="rig-resultats-proc__compte">
            {vue ? `Resultats (${trouvees.length}) — ${vue.resume}` : ''}
          </span>
        </div>

        <div className="rig-table-cadre">
          <table className="rig-table">
            <thead>
              <tr>
                {Colonnes.map(([libelle, cle]) => (
                  <EnteteTriable key={cle} libelle={libelle} cle={cle} cleActive={cleTri}
                                 ascendant={triAscendant} trier={basculerTri} />
                ))}
              </tr>
            </thead>
            <tbody>
              {trouvees.map((affaire, i) => (
                <tr key={`${i}:${affaire.numero}`}
                    className={affaire.numero === selection ? 'rig-ligne--valide' : undefined}
                    onClick={() => setSelection(affaire.numero)}
                    onDoubleClick={() => ouvrir(affaire.numero)} style={{ cursor: 'pointer' }}>
                  <td className="rig-table__mono">{affaire.numero}</td>
                  <td className="rig-table__mono">{affaire.idInstance}</td>
                  <td className="rig-table__mono">{affaire.role}</td>
                  <td className="rig-table__mono">{affaire.codeAffaire}</td>
                  <td>
                    <span className={`rig-nature rig-nature--${affaire.nature}`}>
                      <span className="rig-nature__filet"></span>
                      <span className="rig-nature__nom">{affaire.nomNature}</span>
                    </span>
                  </td>
                  <td>{affaire.libelle}</td>
                  <td>{affaire.nom}</td>
                  <td>{affaire.etat}</td>
                  <td className="rig-table__mono">{affaire.siren}</td>
                  <td>{affaire.type}</td>
                  <td className="rig-table__mono">{affaire.saisine}</td>
                  <td className="rig-table__mono">{affaire.creation}</td>
                  <td className="rig-table__mono">{affaire.solde} €</td>
                </tr>
              ))}
              {vue && trouvees.length === 0 && (
                <tr><td colSpan={13} className="rig-table__vide">
                  {vue.aucunCritere ? (
                    'Saisissez au moins un critere, puis lancez la recherche.'
                  ) : (
                    <>
                      <div>Aucune affaire ne repond a ces criteres.</div>
                      <div style={{ marginTop: 'var(--rig-esp-3)' }}>
                        <button className="rig-bouton rig-bouton--fort"
                                onClick={() => ouvrirUnDossier(vue.sirenCherche)}>
                          Ouvrir un dossier
                        </button>
                        {vue.sirenCherche.length > 0 && (
                          <>
                            {' '}
                            <span className="rig-resultats-proc__compte"
                                  style={{ marginLeft: 'var(--rig-esp-3)' }}>
                              pour le SIREN {vue.sirenCherche}
                            </span>
                          </>
                        )}
                      </div>
                    </>
                  )}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3) var(--rig-esp-4)' }}>
          <div className="rig-kicker">Suppression (XXAFF)</div>
          {choisie === null ? (
            <span className="rig-resultats-proc__compte">
              Selectionnez une instance dans les resultats.
            </span>
          ) : (
            <span className="rig-resultats-proc__compte">
              {`${choisie.numero} · instance ${choisie.idInstance} · role ${choisie.role}`}
            </span>
          )}
          <button className={`rig-bascule${libererNumeros ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setLibererNumeros(!libererNumeros)}>
            <span className="rig-bascule__temoin"></span>
            <span>Liberer les numeros metiers</span>
          </button>
          <button className="rig-bouton rig-bouton--fort" disabled={choisie === null}
                  onClick={supprimer}>Supprimer l'instance</button>
          {message !== null && message !== '' && (
            <span className="rig-resultats-proc__compte">{message}</span>
          )}
        </div>

        <div className="rig-resultats-proc__entete">
          <span className="rig-resultats-proc__compte">
            Double-clic sur une ligne pour ouvrir l'affaire dans la visionneuse.
          </span>
        </div>
      </section>
    </div>
  )
}
