CREATE procedure [dbo].[CHERCHER_NUMERO]
        @TYPE_NUMERO_METIER varchar(5),
        @NUMERO_RETOURNE varchar(20) OUTPUT,
        @ANNEE smallint = 0, /* la seule façon de rendre un paramètre optionnel: lui donner une valeur par défaut, ici 0 */
        @QUANTIEME smallint =0, /* la seule façon de rendre un paramètre optionnel: lui donner une valeur par défaut, ici 0 */
        @MONERREUR int = 0
    as
    /* Déclaration des variables pour récupérer les données de la table */
    DECLARE @CHAINE_STRUCTURE varchar(5)
    DECLARE @CHAINE_CODAGE_ANNEE smallint
    DECLARE @CODE_GREFFE varchar(5)
    DECLARE @CHAINE_LETTRE varchar(3)
    DECLARE @CHRONO_DEBUT int
    DECLARE @CHRONO_COURANT int
    DECLARE @GESTION_NUMEROS_ERRONES bit
    DECLARE @TAILLE_CHRONO smallint
    DECLARE @LIBELLE varchar(50)
    DECLARE @FILE_REDIRECTION varchar(5)
    DECLARE @CHAINE_SEPARATEUR varchar(5)
    DECLARE @TABLE_AFFECTEE varchar(32)
    DECLARE @ATTRIBUT_AFFECTE varchar(32)
    
    /* Variables de construction du numéro retourné */
    DECLARE @CODE_ANNEE varchar(4)
    DECLARE @CODE_QUANTIEME varchar(3)
    DECLARE @CODE_CHRONO varchar(10)
    /* Servira à la gestion de l'ID des enregistrements dans la table NUMERO_METIER*/
    DECLARE @Mon_ID float
    
    /* Selection de la structure du numéro pour le type de numéro de métier */
    
    /* Requête de Sélection */
    /* si on n'a pas fourni d'année, on la calcule */
    if @ANNEE = 0
        select @ANNEE = year(getdate())
    /* Si on a pas le quantième, on le calcule */
    if @QUANTIEME = 0
        select @QUANTIEME =datediff(day, '01/01/' + ltrim(year(getdate())), getdate())+1
    /* on regarde si la file existe pour ce type de numéro métier*/
    if EXISTS (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER)
    begin
        /* si oui, est-elle redirigée ? */
        select @FILE_REDIRECTION = NMMTR_FILE_REDIRECTION from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
        if @FILE_REDIRECTION <>"" /* on redirige sur ce type */
        begin
            set @TYPE_NUMERO_METIER = @FILE_REDIRECTION
        end
    end
    else /* sinon c'est que le type de numéro métier doit être faux */
    begin
        /* on renvoie la valeur -2 */
        return -2
    end
    /* si on a une file pour le type de numéro métier et l'année */
    if EXISTS (select * from NUMERO_METIER  with (tablockx) where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
        and NMMTR_ANNEE = @ANNEE)
    begin
        select
            @Mon_ID = NMMTR_ID_NMMTR,
            @CHAINE_STRUCTURE = NMMTR_STRUCTURE_NUMERO, 
            @CHAINE_CODAGE_ANNEE = NMMTR_CODAGE_ANNEE,
            @CODE_GREFFE = NMMTR_CODE_GREFFE,
            @CHAINE_LETTRE = NMMTR_LETTRE_CATEGORIE,
            @CHRONO_DEBUT = NMMTR_CHRONO_DEBUT,
            @CHRONO_COURANT = NMMTR_CHRONO,
            @TAILLE_CHRONO = NMMTR_TAILLE_CHRONO,
            @GESTION_NUMEROS_ERRONES = NMMTR_GESTION_NUM_ERR,
            @LIBELLE = NMMTR_LIBELLE,
            @FILE_REDIRECTION = NMMTR_FILE_REDIRECTION,
            @CHAINE_SEPARATEUR  = NMMTR_SEPARATEUR,
            @TABLE_AFFECTEE = NMMTR_TABLE_AFFECTEE,
            @ATTRIBUT_AFFECTE = NMMTR_ATTRIBUT_AFFECTE
        from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ANNEE = @ANNEE
        /* Test si on tient compte du quantième ou non */
        if charindex('Q',@CHAINE_STRUCTURE) > 0 /* On tient compte du quantième */
        begin
            /* Existe-t-il une file pour le quantième? */
            if EXISTS (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER 
                and NMMTR_CHRONO<>0 and NMMTR_QUANTIEME = @QUANTIEME and NMMTR_ANNEE = @ANNEE)
            /* Il existe une file pour ce quantième */
            begin
                /* Existe t-il des numéros à attribution erronée */
                if EXISTS (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
                and NMMTR_ATTRIBUTION_ERRONEE = 1 and NMMTR_QUANTIEME = @QUANTIEME AND NMMTR_ANNEE = @ANNEE)
                begin /* Il existe des numéros à attribution erronée */
                    /* on prend le plus petit */
                    select @CHRONO_COURANT = min(NMMTR_CHRONO) from NUMERO_METIER 
                    where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ATTRIBUTION_ERRONEE = 1 
                    and NMMTR_QUANTIEME = @QUANTIEME and NMMTR_ANNEE = @ANNEE
 
                    /* on efface cet enregistrement */
                    delete from NUMERO_METIER where NMMTR_CHRONO = @CHRONO_COURANT
                    and NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ATTRIBUTION_ERRONEE = 1 
                    and NMMTR_QUANTIEME = @QUANTIEME and NMMTR_ANNEE = @ANNEE
                    /* Gestion des erreur */
                   set @MonErreur = @@Error
                   if @MonErreur != 0
                   begin
                       return
                   end
                    /* délock la table */
                end
                else /* Il n'existe pas de numéros à attribution erronée */
                begin
                     /* On prend le Chrono courant de ce quantième */
                    select @CHRONO_COURANT = NMMTR_CHRONO, @Mon_ID = NMMTR_ID_NMMTR from NUMERO_METIER 
                    where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_QUANTIEME = @QUANTIEME and NMMTR_ANNEE = @ANNEE
                    set @CHRONO_COURANT = @CHRONO_COURANT + 1
                    /* on met à jour la table : on met à jour le dernier chrono attribué */
                    update NUMERO_METIER set NMMTR_CHRONO = @CHRONO_COURANT, NMMTR_DATE_ATTRIBUTION = getdate() WHERE NMMTR_ID_NMMTR = @Mon_ID
                    /* Gestion des erreur */
                    set @MonErreur = @@Error
                    if @MonErreur != 0
                       begin
                           return
                    end
                    /* délock la table */
                end
            end
            else /* Il n'existe pas de file pour ce quantiSme */
            begin
                /* On crée alors la nouvelle file */                        
                insert into NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_CODE_GREFFE, 
                    NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, 
                    NMMTR_TAILLE_CHRONO, NMMTR_STRUCTURE_NUMERO, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_DATE_ATTRIBUTION,
                    NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR,
                    NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE)
                values(@TYPE_NUMERO_METIER, @LIBELLE, @CODE_GREFFE, 
                    @ANNEE, @CHAINE_CODAGE_ANNEE,@CHAINE_LETTRE, @CHRONO_DEBUT, @CHRONO_DEBUT, 
                    @TAILLE_CHRONO, @CHAINE_STRUCTURE, 0,getdate(),
                    @QUANTIEME, @GESTION_NUMEROS_ERRONES, @FILE_REDIRECTION, @CHAINE_SEPARATEUR,
                    @TABLE_AFFECTEE, @ATTRIBUT_AFFECTE)
                set @CHRONO_COURANT = @CHRONO_DEBUT
                    /* Gestion des erreur */
                    set @MonErreur = @@Error
                    if @MonErreur != 0
                       begin
                           return
                    end
                /* délock la table */
            end
        end
        else /* On ne tient pas compte du quantième */
        begin
            /* Gere-t'on  les numéros erronés et existe-t'il des numéros à attribution erronée pour ce type? */
            if exists (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER 
            and NMMTR_GESTION_NUM_ERR = 1 and NMMTR_ATTRIBUTION_ERRONEE = 1 and NMMTR_ANNEE = @ANNEE)
            begin
                /* on gère les numéros erronés et il y en a, on prend le plus petit */
                select @CHRONO_COURANT = min(NMMTR_CHRONO) from NUMERO_METIER 
                where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ATTRIBUTION_ERRONEE = 1 and NMMTR_ANNEE = @ANNEE
                /* on efface cet enregistrement */
                delete from NUMERO_METIER where NMMTR_CHRONO = @CHRONO_COURANT
                and NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ATTRIBUTION_ERRONEE = 1 and NMMTR_ANNEE = @ANNEE
                    /* Gestion des erreur */
                    set @MonErreur = @@Error
                    if @MonErreur != 0
                       begin
                           return
                    end
                /* délock la table */            
            end
            else
            begin
                /* On ne gère pas les numéros erronés ou il n'y en a pas */
                select @CHRONO_COURANT = NMMTR_CHRONO, @Mon_ID = NMMTR_ID_NMMTR from NUMERO_METIER 
                where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER and NMMTR_ANNEE = @ANNEE
                if @CHRONO_COURANT = 0 /* C'est l'enregistrement minimum, la sentinelle */
                    set @CHRONO_COURANT = @CHRONO_DEBUT /* alors on prend le chrono_debut */
                else /* On se sert du courant qu'on incrémente de 1 */
                    set @CHRONO_COURANT = @CHRONO_COURANT + 1
                /* on met . jour la table : on change le dernier chrono attribué */
                update NUMERO_METIER set NMMTR_CHRONO = @CHRONO_COURANT, NMMTR_DATE_ATTRIBUTION = getdate() WHERE NMMTR_ID_NMMTR = @Mon_ID
                    /* Gestion des erreur */
                    set @MonErreur = @@Error
                    if @MonErreur != 0
                       begin
                           return
                    end
                /* délock la table */
            end
        end
    end
    else /* on n'a pas de file pour l'année et le type de numéro métier */
    begin
    /* a-t-on une file pour le type de numéro métier ? */
        if EXISTS (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER)
        begin
            /* si c'est oui, c'est que l'on démarre une nouvelle année */
            select
                @Mon_ID = NMMTR_ID_NMMTR,
                @CHAINE_STRUCTURE = NMMTR_STRUCTURE_NUMERO, 
                @CHAINE_CODAGE_ANNEE = NMMTR_CODAGE_ANNEE,
                @CODE_GREFFE = NMMTR_CODE_GREFFE,
                @CHAINE_LETTRE = NMMTR_LETTRE_CATEGORIE,
                @CHRONO_DEBUT = NMMTR_CHRONO_DEBUT,
                @CHRONO_COURANT = NMMTR_CHRONO,
                @TAILLE_CHRONO = NMMTR_TAILLE_CHRONO,
                @GESTION_NUMEROS_ERRONES = NMMTR_GESTION_NUM_ERR,
                @LIBELLE = NMMTR_LIBELLE,
                @FILE_REDIRECTION = NMMTR_FILE_REDIRECTION,
				@CHAINE_SEPARATEUR = NMMTR_SEPARATEUR,
				@TABLE_AFFECTEE = NMMTR_TABLE_AFFECTEE,
				@ATTRIBUT_AFFECTE = NMMTR_ATTRIBUT_AFFECTE
            from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
           /* On crée alors la nouvelle file */                        
           insert into NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_CODE_GREFFE, 
               NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, 
               NMMTR_TAILLE_CHRONO, NMMTR_STRUCTURE_NUMERO, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_DATE_ATTRIBUTION,
               NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR, 
               NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE)
           values(@TYPE_NUMERO_METIER, @LIBELLE, @CODE_GREFFE,
               @ANNEE, @CHAINE_CODAGE_ANNEE,@CHAINE_LETTRE, @CHRONO_DEBUT, @CHRONO_DEBUT,
               @TAILLE_CHRONO, @CHAINE_STRUCTURE, 0,getdate(),@QUANTIEME, @GESTION_NUMEROS_ERRONES, @FILE_REDIRECTION, 
               @CHAINE_SEPARATEUR, @TABLE_AFFECTEE, @ATTRIBUT_AFFECTE)
                    /* Gestion des erreur */
                    set @MonErreur = @@Error
                    if @MonErreur != 0
                       begin
                           return
                    end
           set @CHRONO_COURANT = @CHRONO_DEBUT
            /* on délock la table */
            end
        else /* sinon c'est que le type de numéro métier doit être faux */
        begin
            /* on débloque la table */
            /* on renvoie la valeur -2 */
            return -2
        end
    end
    /* On passe . la construction de la chaine numéro */
    /* Calcul du code année */
    select @CODE_ANNEE = 
  case @CHAINE_CODAGE_ANNEE
   when '0' then ''
                        when '1' then right(rtrim(@ANNEE), 1)
                        when '2' then right(rtrim(@ANNEE), 2)
                        when '4' then ltrim(rtrim(@ANNEE))
   else 'XXXX'  /* si le champ NMMTR_CODAGE_ANNEE a été faussement renseigné */ 
                end
    /* Calcul du quantième */
    /* on force la valeur . une longueur 3 avec des zéros */
    if @QUANTIEME < 10 set @CODE_QUANTIEME = '00' + ltrim(rtrim(str(@QUANTIEME)))
    else if @QUANTIEME < 100 set @CODE_QUANTIEME = '0' + ltrim(rtrim(str(@QUANTIEME)))
         else set @CODE_QUANTIEME = ltrim(rtrim(str(@QUANTIEME)))
    /* Calcul du chrono */
    DECLARE @i int /*compteur*/
    set @CODE_CHRONO = ltrim(rtrim(str(@CHRONO_COURANT)))
    set @i = @TAILLE_CHRONO -len(@CHRONO_COURANT)
    while @i<>0
        begin    
            set @CODE_CHRONO = '0' + @CODE_CHRONO
            set @i = @i-1
        end
    set @NUMERO_RETOURNE = ''
    set @i = 1
    while @i<=len(@CHAINE_STRUCTURE)
        begin  
            if substring(@CHAINE_STRUCTURE,@i,1)='A' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CODE_ANNEE
            else if substring(@CHAINE_STRUCTURE,@i,1)='C' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CODE_CHRONO
            else if substring(@CHAINE_STRUCTURE,@i,1)='L' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CHAINE_LETTRE
            else if substring(@CHAINE_STRUCTURE,@i,1)='S' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CHAINE_SEPARATEUR
            else if substring(@CHAINE_STRUCTURE,@i,1)='Q' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CODE_QUANTIEME
            else if substring(@CHAINE_STRUCTURE,@i,1)='G' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + @CODE_GREFFE
            else if substring(@CHAINE_STRUCTURE,@i,1)='K' 
                set @NUMERO_RETOURNE = @NUMERO_RETOURNE + '*'
            set @i=@i + 1
        end
    /* Gestion des erreur */
    set @MonErreur = @@Error
    if @MonErreur != 0
    begin
         return
    end
    return 0 /* tout c'est bien passé */