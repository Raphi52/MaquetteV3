using Microsoft.Data.SqlClient;

namespace SocleDroitsCheck;

/// <summary>
/// LA REPONSE DE LA V2, recalculee ligne a ligne comme la V2 la calcule, sur les tables d'une base V2
/// lues PAR CE BANC (ses propres SELECT, en lecture seule) -- independamment de la traduction de la
/// reprise (Serveur\Socle\Comptes\RepriseDroits.cs), qui ne partage avec lui que les tables lues.
///
/// Portage fidele, instruction par instruction :
///  - Utilisateur.ALaFonctionnalite (D:\RigApplication\Source\Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:298-347) ;
///  - RigOldHabilitation.Habilitation.ALaFonctionnaliteAncienne (Habilitation.cs:8-16 : toute erreur = refus) ;
///  - GestionDroitRIG.VerifierHabilitationEnSQLOld, m_DeterminerDegreSQL, m_RecupererDegreSQL
///    (D:\RigApplication\Source\VB6\Bin Client\RIG_Client_HabilitationV2\GestionDroitRIG.cls:248-482) :
///    chaque requete SQL du VB6 devient un filtre sur les memes tables, comparant les textes comme SQL
///    Server (collation CI, espaces de fin ignores).
/// Ce n'est PAS l'execution de la DLL V2 (elle demande tout l'environnement RIG) : c'est son algorithme.
/// Une fonction inconnue n'est pas evaluee : la V2 l'ajouterait au catalogue (Utilisateur.cs:300-316),
/// ce que la V3 refuse a dessein (SOC-002 CA05).
/// </summary>
public sealed class OracleDroitsV2
{
    // ProcessusEtFonctionnalite.TypeProcessusEtFonctionnalite (ProcessusEtFonctionalite.cs:16-23), dans son ordre.
    private enum TypeProc { DLL, EXE, PROCVB6, FONC, FONCVB6, EXETAB }

    private sealed record Proc(string Code, string Type);
    private sealed record Categorie(int Id, string Reference, int Niveau, int? Domaine, int? Activite);

    private readonly List<Proc> _procs = [];
    private Dictionary<string, Proc>? _parCode;
    private readonly Dictionary<int, HashSet<string>> _processusAutorises = [];
    private readonly List<Categorie> _categories = [];
    private readonly List<(int Utrol, string Reference, int? Degre)> _composer = [];
    private readonly List<(int Utrol, int Utlst)> _aPourRole = [];
    private readonly Dictionary<int, List<string?>> _expresso = [];

    /// <summary>Les codes du catalogue des fonctions de la V2 (« chaque fonction reprise »).</summary>
    public IReadOnlyList<string> Fonctions => [.. _procs.Select(p => p.Code)];

    /// <summary>Les comptes actifs de la V2 (UTLST_DESACTIVE = 0) : identifiant et compte Windows.</summary>
    public List<(int Id, string Login)> ComptesActifs { get; } = [];

    public static async Task<OracleDroitsV2> LireAsync(string connexionV2)
    {
        var o = new OracleDroitsV2();
        var cs = new SqlConnectionStringBuilder(connexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var cn = new SqlConnection(cs.ConnectionString);
        await cn.OpenAsync();
        async Task Lire(string sql, Action<SqlDataReader> ligne)
        {
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 };
            await using var l = await cmd.ExecuteReaderAsync();
            while (await l.ReadAsync()) ligne(l);
        }
        await Lire("SELECT PROCF_CODE_PROC, PROCF_TYPE FROM PROCESSUS_ET_FONCTIONALITE ORDER BY PROCF_CODE_PROC",
            l => o._procs.Add(new Proc(l.GetString(0), l.GetString(1))));
        // RoleUsers.SelectMany(r => r.Processus) : HABILIT_UTILISATEUR_ROLE_USER -> HABILIT_ROLE_USER_PROCESSUS -> PROCESSUS_ET_FONCTIONALITE
        // (HabilitationRoleUser.cs:75-90, ProcessusEtFonctionalite.cs:333-340).
        await Lire("""
            SELECT u.HUROL_ID_UTLST, p.PROCF_CODE_PROC
            FROM HABILIT_UTILISATEUR_ROLE_USER u
            JOIN HABILIT_ROLE_USER_PROCESSUS r ON r.HRFON_NOM_ROLE = u.HUROL_NOM_ROLE
            JOIN PROCESSUS_ET_FONCTIONALITE p ON p.PROCF_CODE_PROC = r.HRFON_CODE_PROC
            """, l =>
            {
                if (!o._processusAutorises.TryGetValue(l.GetInt32(0), out var s)) o._processusAutorises[l.GetInt32(0)] = s = new(StringComparer.Ordinal);
                s.Add(l.GetString(1));
            });
        // « SELECT ... FROM CATEGORIE WHERE CTGR_REFERENCE = ... » sans ORDER BY : l'ordre de la cle primaire.
        await Lire("SELECT CTGR_ID_CTGR, CTGR_REFERENCE, CTGR_NIVEAU, CTGR_ID_CTGR_DOMAINE, CTGR_ID_CTGR_ACTIVITE FROM CATEGORIE ORDER BY CTGR_ID_CTGR",
            l => o._categories.Add(new Categorie(l.GetInt32(0), l.GetString(1), Convert.ToInt32(l.GetValue(2)),
                l.IsDBNull(3) ? null : Convert.ToInt32(l.GetValue(3)), l.IsDBNull(4) ? null : Convert.ToInt32(l.GetValue(4)))));
        await Lire("SELECT X_CPSHB_ID_UTROL, X_CPSHB_REFERENCE_CTGR, X_CPSHB_DEGRE FROM COMPOSER_HABILITATION",
            l => o._composer.Add((l.GetInt32(0), l.GetString(1), l.IsDBNull(2) ? null : Convert.ToInt32(l.GetValue(2)))));
        await Lire("SELECT X_UTARL_ID_UTROL, X_UTARL_ID_UTLST FROM UTILISATEUR_A_POUR_ROLE",
            l => o._aPourRole.Add((l.GetInt32(0), l.GetInt32(1))));
        // ExpressoMenu.GetAllExpressoMenuByUtilisateur (ExpressoMenu.cs:26-39).
        await Lire("SELECT d.EXPDR_ID_UTLST, m.EXPME_CLE FROM EXPRESSO_DROITS d JOIN EXPRESSO_MENU m ON m.EXPME_ID_EXPME = d.EXPDR_ID_EXPME",
            l =>
            {
                if (!o._expresso.TryGetValue(l.GetInt32(0), out var s)) o._expresso[l.GetInt32(0)] = s = [];
                s.Add(l.IsDBNull(1) ? null : l.GetString(1));
            });
        await Lire("SELECT UTLST_ID_UTLST, UTLST_LOGIN FROM UTILISATEUR WHERE UTLST_DESACTIVE = 0 ORDER BY UTLST_ID_UTLST",
            l => o.ComptesActifs.Add((l.GetInt32(0), l.GetString(1))));
        return o;
    }

    /// <summary>Utilisateur.ALaFonctionnalite (Utilisateur.cs:298-347), pour une fonction du catalogue.</summary>
    public bool ALaFonctionnalite(int idUtilisateur, string nomFonctionnalite)
    {
        // GetProcessusEtFonctionnalite : premiere fonction du meme code, CurrentCultureIgnoreCase (ProcessusEtFonctionalite.cs:326-327).
        _parCode ??= _procs.GroupBy(x => x.Code, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
        var proc = _parCode.GetValueOrDefault(nomFonctionnalite)
                   ?? throw new ArgumentException($"{nomFonctionnalite} : fonction hors catalogue, la V2 l'ajouterait (non evaluee).");
        // :317-320 -- « OLD » et « VB6 » deviennent FONCVB6.
        var type = proc.Type is "OLD" or "VB6" ? TypeProc.FONCVB6 : TypeDe(proc.Type);
        var ret = _processusAutorises.TryGetValue(idUtilisateur, out var autorises) && autorises.Contains(proc.Code);
        if (ret == false)
        {
            if (type == TypeProc.FONCVB6)
                return ALaFonctionnaliteAncienne(idUtilisateur, nomFonctionnalite);
            var liste = _expresso.TryGetValue(idUtilisateur, out var menus) ? menus : [];
            ret = liste.Count(x => x == nomFonctionnalite) > 0;
            if (ret == false)
            {
                string? oldNom = nomFonctionnalite switch
                {
                    "GESTDROITSEXP" => "DROITS",
                    "STAT_EXP" => "STATS",
                    "COMPTA_EDIT" => "EDITION",
                    "COMPTA_FOURN" => "FOURNISSEUR",
                    "REMISBQ" => "BORDEREAU",
                    "FIGER_EXP" => "FIGEAGE",
                    _ => null,
                };
                if (oldNom != null) ret = liste.Count(x => x == oldNom) > 0;
            }
        }
        return ret;
    }

    // TypeProc (ProcessusEtFonctionalite.cs:186-192) : Enum.TryParse, sinon PROCVB6.
    private static TypeProc TypeDe(string type) => Enum.TryParse(type, out TypeProc t) ? t : TypeProc.PROCVB6;

    // Habilitation.cs:8-16.
    private bool ALaFonctionnaliteAncienne(int idUtilisateur, string referenceProcessus)
    {
        try { return VerifierHabilitationEnSQLOld(referenceProcessus, idUtilisateur) > 2; }
        catch (Exception) { return false; }
    }

    // SQL Server, collation CI de RIG_DEV : majuscules et espaces de fin ignores.
    private static bool SqlEgal(string? a, string? b)
        => a is not null && b is not null && string.Equals(a.TrimEnd(), b.TrimEnd(), StringComparison.OrdinalIgnoreCase);

    // VB6 : « valeur & "#" » rend "" pour un Null.
    private static string Vb(int? v) => v?.ToString() ?? "";

    // VB6 : CLng("") leve « Type mismatch ».
    private static long CLng(string s) => long.TryParse(s, out var v) ? v : throw new InvalidCastException("Type mismatch");

    private int VerifierHabilitationEnSQLOld(string codeHabilitation, int idUtlst)
    {
        string strKey = "";
        int intNiveau = 0;
        var rstCtgr = _categories.Where(c => SqlEgal(c.Reference, codeHabilitation)).ToList();
        if (rstCtgr.Count > 0)
        {
            var c = rstCtgr[0];
            switch (c.Niveau)
            {
                case 1: strKey = c.Id + "#0#0"; break;
                case 2: strKey = Vb(c.Domaine) + "#" + Vb(c.Activite) + "#0"; break;
                case 4: strKey = Vb(c.Domaine) + "#" + Vb(c.Activite) + "#" + c.Id; break;
                case 5: strKey = Vb(c.Domaine) + "#" + c.Id + "#0"; break;
                case 6: strKey = Vb(c.Domaine) + "#" + c.Id + "#0"; break;
            }
            intNiveau = c.Niveau;
        }
        if (strKey == "") return 0;   // « Exit Function » : la valeur par defaut d'un Integer

        var rst = from a in _aPourRole
                  where a.Utlst == idUtlst
                  join h in _composer on a.Utrol equals h.Utrol
                  where SqlEgal(h.Reference, codeHabilitation)
                  select h.Degre;
        var intDegre = -1;
        foreach (var d in rst)
        {
            var intDegreTemp = d ?? -1;
            if (intDegreTemp > intDegre) intDegre = intDegreTemp;
        }
        return DeterminerDegreSQL(strKey, intNiveau, intDegre, idUtlst);
    }

    private int DeterminerDegreSQL(string key, int niveau, int degreInitial, int idUtlst)
    {
        var strIdCtgr = (key + "#").Split('#');
        int intDegre;
        if (degreInitial == 0) return 0;
        switch (niveau)
        {
            case 1:
                return degreInitial;
            case 2:
                intDegre = RecupererDegreSQL(CLng(strIdCtgr[0]), idUtlst);
                return intDegre == -1 ? degreInitial : intDegre;
            case 4:
            case 7:
            case 8:
            {
                intDegre = RecupererDegreSQL(CLng(strIdCtgr[0]), idUtlst);
                if (intDegre == -1) intDegre = degreInitial;
                var resultat = RecupererDegreSQL(CLng(strIdCtgr[1]), idUtlst);
                if (resultat == -1 && intDegre >= degreInitial) resultat = degreInitial;
                else if (resultat > intDegre) resultat = intDegre;
                return resultat;
            }
            case 5:
            case 6:
                intDegre = RecupererDegreSQL(CLng(strIdCtgr[0]), idUtlst);
                return intDegre == -1 ? degreInitial : intDegre;
            default:
                return 0;
        }
    }

    // « ... RIGHT OUTER JOIN CATEGORIE ON X_CPSHB_REFERENCE_CTGR = CTGR_REFERENCE WHERE CTGR_ID_CTGR = id AND X_UTARL_ID_UTLST = u »
    private int RecupererDegreSQL(long idCategorie, int idUtlst)
    {
        // Les filtres de la clause WHERE sont poses avant la jointure : meme resultat, sans parcourir
        // toutes les categories pour chaque ligne.
        var categorie = _categories.Where(c => c.Id == idCategorie).ToList();
        var rst = from a in _aPourRole
                  where a.Utlst == idUtlst
                  join h in _composer on a.Utrol equals h.Utrol
                  from c in categorie
                  where SqlEgal(h.Reference, c.Reference)
                  select h.Degre;
        var degre = -1;
        foreach (var d in rst)
        {
            var intDegre = d ?? -1;
            if (intDegre > degre) degre = intDegre;
        }
        return degre;
    }
}
