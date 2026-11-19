// Ecran de la procedure PROC_GESTEMAIL (RIG v2 : « Modele de mail »,
// etapes ETP_RECHERCHE_EMAILMODEL, ETP_EDIT_EMAILMODEL,
// ETP_EDIT_TEMPLATE_MAIL, ETP_APERCU_TEMPLATE_MAIL et
// ETP_RECHERCHE/EDIT_LISTEDISTRIBUTIONMAIL). Champs lus dans les
// formulaires v2 : TxtCode, TxtLib, TxtLibEcran, TxtType, TxtSujet,
// TxtMultiLineCorps, TxtExpediteurCourrierLib / Adr, et les domaines
// CheckBoxRCS, CheckBoxJud, CheckBoxCPT, CheckBoxINS.
//
// Porte depuis Components/Ecrans/EcranMessagerie.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// modeles et listes de distribution de demonstration l'ont suivi ici.

import { useState } from 'react'
import { contientSansCasse, estBlanc } from '../criteres'

interface ModeleMail {
  code: string
  libelle: string
  libelleEcran: string
  type: string
  sujet: string
  corps: string
  expediteurLib: string
  expediteurAdr: string
  domainesModele: string[]
  copie: string
  accuseReception: string
  envoiAuto: boolean
}

interface ListeDistribution {
  code: string
  libelle: string
  domaine: string
  adresses: string[]
}

// v2 (OPE_EDIT_EMAILMODEL) : CheckBoxRCS, CheckBoxJudiciaire, CheckBoxINS,
// CheckBoxCPT, CheckBoxAmitel.
const Domaines = ['RCS', 'Judiciaire', 'INS', 'CPT', 'Amitel']

const Modeles: ModeleMail[] = [
  {
    code: 'ACC_DEM', libelle: 'Accuse de reception de demande', libelleEcran: 'Accuse de reception', type: 'Transactionnel',
    sujet: 'Votre demande {NUM_DEMANDE} a bien ete recue',
    corps: 'Bonjour {CIVILITE} {NOM},\n\nNous avons bien recu votre demande {NUM_DEMANDE} du {DATE_DEMANDE}.\nElle est en cours de traitement par le greffe.\n\nCordialement,\nLe greffe du tribunal de commerce',
    expediteurLib: 'Greffe du tribunal de commerce', expediteurAdr: 'greffe@exemple.fr', domainesModele: ['RCS'], copie: '', accuseReception: '', envoiAuto: true,
  },
  {
    code: 'REJ_DEM', libelle: 'Rejet de demande', libelleEcran: 'Rejet', type: 'Transactionnel',
    sujet: 'Votre demande {NUM_DEMANDE} a ete rejetee',
    corps: 'Bonjour {CIVILITE} {NOM},\n\nVotre demande {NUM_DEMANDE} ne peut etre traitee : {MOTIF_REJET}.\nVous pouvez la deposer a nouveau apres correction.\n\nCordialement,\nLe greffe',
    expediteurLib: 'Greffe du tribunal de commerce', expediteurAdr: 'greffe@exemple.fr', domainesModele: ['RCS'], copie: '', accuseReception: '', envoiAuto: true,
  },
  {
    code: 'CONV_AUD', libelle: 'Convocation a une audience', libelleEcran: 'Convocation', type: 'Judiciaire',
    sujet: 'Convocation a l\'audience du {DATE_AUDIENCE}',
    corps: 'Maitre,\n\nVous etes convoque a l\'audience du {DATE_AUDIENCE} a {HEURE_AUDIENCE}, chambre {CHAMBRE}.\n\nLe greffe judiciaire',
    expediteurLib: 'Greffe judiciaire', expediteurAdr: 'judiciaire@exemple.fr', domainesModele: ['Judiciaire'], copie: 'greffe@exemple.fr', accuseReception: '', envoiAuto: false,
  },
  {
    code: 'REL_FAC', libelle: 'Relance de facture', libelleEcran: 'Relance', type: 'Comptabilite',
    sujet: 'Relance : facture {NUM_FACTURE}',
    corps: 'Bonjour,\n\nSauf erreur, la facture {NUM_FACTURE} du {DATE_FACTURE} reste impayee ({MONTANT} EUR).\n\nLe service comptable',
    expediteurLib: 'Service comptable', expediteurAdr: 'compta@exemple.fr', domainesModele: ['CPT'], copie: '', accuseReception: 'compta@exemple.fr', envoiAuto: true,
  },
]

const Distributions: ListeDistribution[] = [
  { code: 'LD_MAND', libelle: 'Mandataires judiciaires', domaine: 'Judiciaire', adresses: ['a.leroy@exemple.fr', 's.blanc@exemple.fr'] },
  { code: 'LD_COMPTA', libelle: 'Comptabilite interne', domaine: 'Comptabilite', adresses: ['compta@exemple.fr'] },
  { code: 'LD_RCS', libelle: 'Guichet RCS', domaine: 'RCS', adresses: ['rcs@exemple.fr', 'accueil@exemple.fr'] },
]

export function EcranMessagerie() {
  const [recherche, setRecherche] = useState('')
  const [critDomaines, setCritDomaines] = useState<ReadonlySet<string>>(new Set())
  const [apercu, setApercu] = useState(true)
  const [selection, setSelection] = useState<ModeleMail | null>(Modeles[0]!)

  function basculerDomaine(d: string) {
    setCritDomaines((avant) => {
      const s = new Set(avant)
      if (!s.delete(d)) s.add(d)
      return s
    })
  }

  let trouves = Modeles
  if (!estBlanc(recherche)) {
    const t = recherche.trim()
    trouves = trouves.filter((m) => contientSansCasse(m.code, t) || contientSansCasse(m.libelle, t))
  }
  if (critDomaines.size > 0) trouves = trouves.filter((m) => m.domainesModele.some((d) => critDomaines.has(d)))
  const s = selection

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>Modeles de mail</div>

        <label className="rig-saisie-champ">
          <span className="rig-saisie-champ__libelle">Code ou libelle</span>
          <input className="rig-saisie-champ__saisie" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </label>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Domaine</div>
          {Domaines.map((v) => (
            <button key={v} className={`rig-bascule${critDomaines.has(v) ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerDomaine(v)}>
              <span className="rig-bascule__temoin"></span><span>{v}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Resultats ({trouves.length})</div>
          {trouves.map((m) => (
            <button key={m.code} className={`rig-bascule${selection?.code === m.code ? ' rig-bascule--actif' : ''}`}
                    onClick={() => setSelection(m)}>
              <span className="rig-bascule__temoin"></span>
              <span>{m.code} — {m.libelle}</span>
            </button>
          ))}
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-onglets">
            <button className={`rig-onglet${apercu ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setApercu(true)}>Apercu</button>
            <button className={`rig-onglet${!apercu ? ' rig-onglet--actif' : ''}`}
                    onClick={() => setApercu(false)}>Listes de distribution</button>
          </div>
          <span className="rig-resultats-proc__compte">{selection?.code ?? 'aucun modele'}</span>
        </div>

        {apercu && s ? (
          <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Code</span>
                <input className="rig-saisie-champ__saisie" value={s.code} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Type</span>
                <input className="rig-saisie-champ__saisie" value={s.type} readOnly />
              </label>
            </div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Libelle ecran</span>
              <input className="rig-saisie-champ__saisie" value={s.libelleEcran} readOnly />
            </label>
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Expediteur (libelle)</span>
                <input className="rig-saisie-champ__saisie" value={s.expediteurLib} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Expediteur (adresse)</span>
                <input className="rig-saisie-champ__saisie" value={s.expediteurAdr} readOnly />
              </label>
            </div>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Sujet</span>
              <input className="rig-saisie-champ__saisie" value={s.sujet} readOnly />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Corps du message</span>
              <textarea className="rig-saisie-champ__saisie" rows={8} readOnly value={s.corps} />
            </label>

            {/* v2 (OPE_EDIT_EMAILMODEL) : CheckBoxRCS, CheckBoxJudiciaire,
                CheckBoxINS, CheckBoxCPT, CheckBoxAmitel. */}
            <div className="rig-criteres__bloc">
              <div className="rig-kicker">Domaines du modele</div>
              {s.domainesModele.map((d) => <span key={d} className="rig-chip">{d}</span>)}
            </div>

            {/* v2 : ope_edit_emailmodel_many_choices_CC et _RA. */}
            <div className="rig-criteres__paire">
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Copie (CC)</span>
                <input className="rig-saisie-champ__saisie" value={s.copie} readOnly />
              </label>
              <label className="rig-saisie-champ">
                <span className="rig-saisie-champ__libelle">Accuse de reception</span>
                <input className="rig-saisie-champ__saisie" value={s.accuseReception} readOnly />
              </label>
            </div>

            {/* v2 : ult_chkIsEnvoiAuto. */}
            <span className={`rig-etat rig-etat--${s.envoiAuto ? 'ok' : 'attente'}`}>
              {s.envoiAuto ? 'Envoi automatique actif' : 'Envoi manuel'}
            </span>
            <div>
              {s.domainesModele.map((d) => <span key={d} className="rig-chip">{d}</span>)}
            </div>
          </div>
        ) : apercu ? (
          <div className="rig-table__vide" style={{ padding: 'var(--rig-esp-4)' }}>
            Choisissez un modele dans la liste de gauche.
          </div>
        ) : (
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr>
                  <th>Liste</th>
                  <th>Libelle</th>
                  <th>Domaine</th>
                  <th className="rig-table__centre">Destinataires</th>
                  <th>Adresses</th>
                </tr>
              </thead>
              <tbody>
                {Distributions.map((l) => (
                  <tr key={l.code}>
                    <td className="rig-table__mono">{l.code}</td>
                    <td>{l.libelle}</td>
                    <td>{l.domaine}</td>
                    <td className="rig-table__centre">{l.adresses.length}</td>
                    <td className="rig-table__mono">{l.adresses.join(' ; ')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
