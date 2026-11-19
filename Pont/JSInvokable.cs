namespace RigV3Desktop.Pont;

/// <summary>
/// Une methode STATIQUE que la page peut appeler par son identifiant
/// « &lt;NomEcran&gt;.&lt;Methode&gt; » (web/src/pont.ts : <c>appeler&lt;T&gt;(identifiant, ...args)</c>).
/// Lue par <see cref="Repartiteur"/>, qui la trouve par reflexion.
///
/// C'est l'attribut de CE projet, pas celui de Blazor (retire le 19/11/2026) :
/// meme nom et meme ecriture, pour que les ponts deja ecrits n'aient rien a
/// changer. Seul dans son fichier : les bancs de tests/ qui compilent un
/// Pont/&lt;NomEcran&gt;.cs a part l'incluent sans tirer tout le pont.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class JSInvokableAttribute(string identifiant) : Attribute
{
    public string Identifiant { get; } = identifiant;
}
