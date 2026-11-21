namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Un numero manquant du registre (NUM-013 CA02, CA03 ; <see cref="RoutesCompteur.Manquants"/>) : un numero donne qui
/// ne sera jamais utilise, avec son motif. Lu dans la vue num.NumeroManquant (lot 6), qui lit le journal des numeros :
/// un numero rendu depuis (NUM-004) n'y est plus.
/// </summary>
/// <param name="Serie">La serie (RC1, RC5, RC6, RGCH...).</param>
/// <param name="Numero">Le numero tel qu'il a ete donne (« F24/008948 »).</param>
/// <param name="Motif">Pourquoi il manque ; « manquant constaté par la V2, motif inconnu » pour un numero repris de
/// la table NUMERO_CHRONO_MANQUANT de la V2, qui n'avait pas de motif (CA02).</param>
/// <param name="Logiciel">« V2 » (repris ou declare par l'ancien logiciel) ou « V3 ».</param>
/// <param name="DeclareLe">Quand il a ete declare manquant (ou repris).</param>
public sealed record NumeroManquant(
    string Serie,
    int Annee,
    int Jour,
    int Chrono,
    string Numero,
    string? Motif,
    string Logiciel,
    DateTime DeclareLe);
