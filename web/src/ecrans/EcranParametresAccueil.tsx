// Ecran de la procedure PROC_ACCUEIL_PARAM (RIG v2 : parametres d'accueil).
// Les reglages sont ceux du formulaire v2 : chkAnimations, chkPleinEcran,
// chkColorier1LigneSur2, chkAutofocusOnClickIconEtpOpe, chkDateSaisieLibreCpp,
// chkInterdireMoletteComboCpp, txtIntervalSlideshowAlertes, txtMaxWidth,
// txtMaxHeight, plus « Colonne triee par defaut » / sens Ascendant-Descendant
// et la liste des imprimantes a cocher.
//
// Porte depuis Components/Ecrans/EcranParametresAccueil.razor (lot 4) : meme
// balisage, memes classes CSS. L'ecran ne lisait rien dans Metier/ : ses
// reglages et ses imprimantes de demonstration l'ont suivi ici.

//
// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026) : les reglages viennent du pont
// (Pont/EcranParametresAccueilBase.cs). En base, ce sont ceux que L'AGENT a enregistres dans RIG v2 (PARAMETRE_UTILISATEUR,
// cles ConsoleAccueil_* : Serveur/Socle/EcransMain/LectureParametresAccueil.cs), avec les gardes de profil de RIG v2
// (animations : administrateur ; molette et date libre : Amitel, FORM_SKIN_ACCUEIL_PARAM.cs:552-575). Ils ne sont PAS
// enregistres, et l'ecran le dit (SansEnregistrement) : ce sont des reglages du poste WinForms de RIG v2, que la V3
// n'applique pas. Non lus : « plein ecran » (hors PARAMETRE_UTILISATEUR), les imprimantes PaperCut (service externe), la
// colonne et le sens du tri (des index de RIG v2). Sans serveur, les reglages d'exemple ci-dessous, marques.

import { useEffect, useState } from 'react'
import { appeler } from '../pont'
import { ProvenanceEcran, type ModeLecture } from '../provenance'

/** Ce que rend le pont (Pont/EcranParametresAccueilBase.cs, VueParametresAccueil). */
export interface VueParametresAccueil {
  mode: ModeLecture
  message: string | null
  /** Les reglages RIG v2 de l'agent, par cle ConsoleAccueil_* : seulement ceux qu'il a enregistres. */
  valeurs: Record<string, string>
  estAdministrateur: boolean
  estAmitel: boolean
  /** Faux : l'agent n'a pas de compte RIG v2, rien a relire. */
  agentV2: boolean
}

/** Pourquoi un reglage change a l'ecran n'est pas enregistre, branche a la base. */
export const SansEnregistrement = "Non enregistré : RIG v2 range ces réglages de son poste dans PARAMETRE_UTILISATEUR à la " +
  "fermeture de l'onglet (FORM_SKIN_ACCUEIL_PARAM.cs:97-103, :620-648) ; la V3 ne les applique pas et ne les écrit pas."

/** Une case de RIG v2 (« True » / « False ») ; null quand l'agent ne l'a jamais enregistree. */
const caseV2 = (v: string | undefined): boolean | null => (v === undefined ? null : v.toLowerCase() === 'true')

interface Imprimante {
  nom: string
  recommandee: boolean
  cochee: boolean
}

const ImprimantesDeDepart: Imprimante[] = [
  { nom: 'Greffe - Accueil', recommandee: true, cochee: true },
  { nom: 'Greffe - Etage 1', recommandee: false, cochee: true },
  { nom: 'Greffe - Archives', recommandee: false, cochee: false },
  { nom: 'PDF (fichier)', recommandee: false, cochee: true },
]

export function EcranParametresAccueil() {
  const [imprimantes, setImprimantes] = useState<Imprimante[]>(ImprimantesDeDepart)
  const [animations, setAnimations] = useState(true)
  const [pleinEcran, setPleinEcran] = useState(false)
  const [autofocusBouton, setAutofocusBouton] = useState(true)
  const [uneLigneSurDeux, setUneLigneSurDeux] = useState(true)
  const [dateSaisieLibre, setDateSaisieLibre] = useState(false)
  const [moletteInterdite, setMoletteInterdite] = useState(true)
  // Le @bind d'un int : seul le texte saisi s'affiche, aucune autre partie de
  // l'ecran ne lisait ces valeurs.
  const [intervalAlertes, setIntervalAlertes] = useState('8')
  const [largeurMax, setLargeurMax] = useState('1600')
  const [hauteurMax, setHauteurMax] = useState('900')
  const [colonneTri, setColonneTri] = useState('Date de creation')
  const [sensTri, setSensTri] = useState('Descendant')

  // La vue du pont : null tant qu'il n'a pas repondu. En base, les reglages lus remplacent ceux de l'exemple.
  const [vue, setVue] = useState<VueParametresAccueil | null>(null)
  useEffect(() => {
    let perime = false
    void appeler<VueParametresAccueil>('EcranParametresAccueil.Reglages').then((v) => {
      if (perime) return
      setVue(v)
      if (v.mode !== 'serveur') return
      const lu = v.valeurs
      const poser = (cle: string, fixer: (b: boolean) => void) => { const b = caseV2(lu[cle]); if (b !== null) fixer(b) }
      poser('ConsoleAccueil_AllowAnimations', setAnimations)
      poser('ConsoleAccueil_AutofocusOnClickIconEtpOpe', setAutofocusBouton)
      poser('ConsoleAccueil_Colorier1LigneSur2', setUneLigneSurDeux)
      poser('ConsoleAccueil_DateSaisieLibreCpp', setDateSaisieLibre)
      poser('ConsoleAccueil_InterdireMoletteComboCpp', setMoletteInterdite)
      // L'intervalle de RIG v2 est en millisecondes (5000 = 5 s) ; l'ecran le montre en secondes.
      const ms = Number(lu.ConsoleAccueil_IntervalSlideshowAlertes)
      if (lu.ConsoleAccueil_IntervalSlideshowAlertes !== undefined && Number.isFinite(ms)) setIntervalAlertes(String(ms / 1000))
      if (lu.ConsoleAccueil_Form_MaxWidth !== undefined) setLargeurMax(lu.ConsoleAccueil_Form_MaxWidth)
      if (lu.ConsoleAccueil_Form_MaxHeight !== undefined) setHauteurMax(lu.ConsoleAccueil_Form_MaxHeight)
    })
    return () => { perime = true }
  }, [])
  const enBase = vue?.mode === 'serveur'
  // Les gardes de profil de RIG v2 : sans serveur, tout est montre (l'exemple).
  const voitAnimations = !enBase || vue.estAdministrateur
  const voitAmitel = !enBase || vue.estAmitel

  function basculerImprimante(n: number) {
    setImprimantes((avant) => avant.map((imp, i) => (i === n ? { ...imp, cochee: !imp.cochee } : imp)))
  }

  return (
    <div className="rig-proc__corps">

      <aside className="rig-criteres">
        <ProvenanceEcran mode={vue?.mode} message={vue?.message} quoi="Reglages" />
        {enBase && (
          <p className="rig-eg-note" role="note">
            {vue.agentV2 ? `Vos réglages RIG v2 (${Object.keys(vue.valeurs).length} enregistré(s) ; les autres gardent la valeur de l'écran). `
              : "Vous n'avez pas de compte RIG v2 : aucun réglage à relire. "}
            {SansEnregistrement}
          </p>
        )}
        <div className="rig-kicker" style={{ marginBottom: 'var(--rig-esp-3)' }}>General</div>

        {voitAnimations && (
          <button className={`rig-bascule${animations ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setAnimations(!animations)}>
            <span className="rig-bascule__temoin"></span><span>Animations</span>
          </button>
        )}
        <button className={`rig-bascule${pleinEcran ? ' rig-bascule--actif' : ''}`}
                onClick={() => setPleinEcran(!pleinEcran)}>
          <span className="rig-bascule__temoin"></span><span>Ouvrir en plein ecran</span>
        </button>
        <button className={`rig-bascule${autofocusBouton ? ' rig-bascule--actif' : ''}`}
                onClick={() => setAutofocusBouton(!autofocusBouton)}>
          <span className="rig-bascule__temoin"></span><span>Focus automatique au clic sur un bouton</span>
        </button>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Alertes</div>
          <label className="rig-saisie-champ">
            <span className="rig-saisie-champ__libelle">Interval de defilement (secondes)</span>
            <input className="rig-saisie-champ__saisie" type="number" min="1" max="120"
                   value={intervalAlertes} onChange={(e) => setIntervalAlertes(e.target.value)} />
          </label>
        </div>

        <div className="rig-criteres__bloc">
          <div className="rig-kicker">Ajuster la taille des controles</div>
          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Largeur max.</span>
              <input className="rig-saisie-champ__saisie" type="number" min="100" max="4000"
                     value={largeurMax} onChange={(e) => setLargeurMax(e.target.value)} />
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Hauteur max.</span>
              <input className="rig-saisie-champ__saisie" type="number" min="100" max="4000"
                     value={hauteurMax} onChange={(e) => setHauteurMax(e.target.value)} />
            </label>
          </div>
        </div>
      </aside>

      <section className="rig-resultats-proc">
        <div className="rig-resultats-proc__entete">
          <div className="rig-kicker">Grilles et tableaux</div>
          <span className="rig-resultats-proc__compte">{enBase ? 'Imprimantes non lues' : `${imprimantes.filter((i) => i.cochee).length} imprimante(s) cochee(s)`}</span>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <button className={`rig-bascule${uneLigneSurDeux ? ' rig-bascule--actif' : ''}`}
                  onClick={() => setUneLigneSurDeux(!uneLigneSurDeux)}>
            <span className="rig-bascule__temoin"></span><span>Colorier une ligne sur deux</span>
          </button>
          {voitAmitel && (
            <>
              <button className={`rig-bascule${dateSaisieLibre ? ' rig-bascule--actif' : ''}`}
                      onClick={() => setDateSaisieLibre(!dateSaisieLibre)}>
                <span className="rig-bascule__temoin"></span><span>Date en saisie libre</span>
              </button>
              <button className={`rig-bascule${moletteInterdite ? ' rig-bascule--actif' : ''}`}
                      onClick={() => setMoletteInterdite(!moletteInterdite)}>
                <span className="rig-bascule__temoin"></span><span>Interdire la molette dans les listes deroulantes</span>
              </button>
            </>
          )}

          <div className="rig-criteres__paire">
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Colonne de tri des alertes</span>
              <select className="rig-saisie-champ__saisie" value={colonneTri} onChange={(e) => setColonneTri(e.target.value)}>
                <option>Date de creation</option>
                <option>N Demande</option>
                <option>Client</option>
                <option>Etat</option>
              </select>
            </label>
            <label className="rig-saisie-champ">
              <span className="rig-saisie-champ__libelle">Sens du tri</span>
              <select className="rig-saisie-champ__saisie" value={sensTri} onChange={(e) => setSensTri(e.target.value)}>
                <option>Ascendant</option>
                <option>Descendant</option>
              </select>
            </label>
          </div>
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <div className="rig-kicker">Imprimantes autorisees</div>
          {enBase && (
            <p className="rig-eg-note" role="note">
              Non lues : la liste des imprimantes vient de PaperCut, un service externe que la V3 n'appelle pas.
            </p>
          )}
          {!enBase && imprimantes.map((imp, n) => (
            <button key={imp.nom} className={`rig-bascule${imp.cochee ? ' rig-bascule--actif' : ''}`}
                    onClick={() => basculerImprimante(n)}>
              <span className="rig-bascule__temoin"></span>
              <span>{imp.nom} {imp.recommandee ? '(recommande)' : ''}</span>
            </button>
          ))}
        </div>

        <div className="rig-criteres__bloc" style={{ padding: 'var(--rig-esp-3)' }}>
          <div className="rig-kicker">Apercu</div>
          <div className="rig-table-cadre">
            <table className="rig-table">
              <thead>
                <tr><th>{colonneTri} ({sensTri})</th><th>Client</th><th>Etat</th></tr>
              </thead>
              <tbody>
                {[0, 1, 2, 3].map((n) => (
                  <tr key={n} style={uneLigneSurDeux && n % 2 === 1 ? { background: 'rgba(255,255,255,.04)' } : undefined}>
                    <td className="rig-table__mono">Ligne {n + 1}</td>
                    <td>Client {n + 1}</td>
                    <td><span className="rig-etat rig-etat--ok">Validee</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </div>
  )
}
