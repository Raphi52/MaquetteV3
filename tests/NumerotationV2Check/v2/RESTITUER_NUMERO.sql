CREATE procedure [dbo].[RESTITUER_NUMERO]
        @TYPE_NUMERO_METIER varchar(5),
        @NUMERO_RETOURNE varchar(20),
        @ANNEE smallint = 0, 
        @RAISON varchar(50)="",
        @MonErreur int = 0 OUTPUT
    as
    
    DECLARE @CHAINE_STRUCTURE varchar(5)
    DECLARE @CHAINE_CODAGE_ANNEE smallint
    DECLARE @CODE_GREFFE varchar(5)
    DECLARE @CHAINE_LETTRE varchar(16)
    DECLARE @CHRONO_DEBUT int
    DECLARE @CHRONO_RETOURNE int
    DECLARE @GESTION_NUMEROS_ERRONES bit
    DECLARE @TAILLE_CHRONO smallint
    DECLARE @LIBELLE varchar(50)
    DECLARE @CHAINE_SEPARATEUR varchar(5)
    
    DECLARE @i smallint
    DECLARE @Mon_ID int
    DECLARE @QUANTIEME smallint
    DECLARE @FILE_REDIRECTION varchar(5)
    DECLARE @TABLE_AFFECTEE varchar(32)
    DECLARE @ATTRIBUT_AFFECTE varchar(32)
    set @MonErreur = 0
    
    
    if EXISTS (select * from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER)
    begin
        
        select @FILE_REDIRECTION = NMMTR_FILE_REDIRECTION from NUMERO_METIER where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
        if @FILE_REDIRECTION <>"" 
        begin
            set @TYPE_NUMERO_METIER = @FILE_REDIRECTION
        end
    end
    else 
    begin
        
        return -2
    end
    select
        @LIBELLE = NMMTR_LIBELLE,
        @CODE_GREFFE = NMMTR_CODE_GREFFE,
        @CHAINE_CODAGE_ANNEE = NMMTR_CODAGE_ANNEE,
        @CHAINE_LETTRE = NMMTR_LETTRE_CATEGORIE,
        @CHRONO_DEBUT = NMMTR_CHRONO_DEBUT,
        @TAILLE_CHRONO = NMMTR_TAILLE_CHRONO,
        @CHAINE_STRUCTURE = NMMTR_STRUCTURE_NUMERO, 
        @GESTION_NUMEROS_ERRONES = NMMTR_GESTION_NUM_ERR,
        @CHAINE_SEPARATEUR = NMMTR_SEPARATEUR,
        @TABLE_AFFECTEE = NMMTR_TABLE_AFFECTEE,
        @ATTRIBUT_AFFECTE = NMMTR_ATTRIBUT_AFFECTE
    from NUMERO_METIER
    where NMMTR_TYPE_METIER = @TYPE_NUMERO_METIER
    
    set @MonErreur = @@Error
    if @MonErreur != 0
        begin
            return
        end
    if @GESTION_NUMEROS_ERRONES = 0
        
        begin
            return 
        end
    else
    begin TRAN
        
 
        
        if @ANNEE = 0
            select @ANNEE = year(getdate())
        
        set @i = 1
        while @i<=len(@CHAINE_STRUCTURE)
            begin  
                if substring(@CHAINE_STRUCTURE,@i,1)='A'
                    
                    set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- @CHAINE_CODAGE_ANNEE)
                else if substring(@CHAINE_STRUCTURE,@i,1)='C'
                    begin
                        
                        set @CHRONO_RETOURNE = left(@NUMERO_RETOURNE, @TAILLE_CHRONO)
                        set @i = len(@CHAINE_STRUCTURE) +1 
                    end
                else if substring(@CHAINE_STRUCTURE,@i,1)='L' 
                    set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- LEN(@CHAINE_LETTRE))
               else if substring(@CHAINE_STRUCTURE,@i,1)='S' 
                    set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- LEN(@CHAINE_SEPARATEUR))
                else if substring(@CHAINE_STRUCTURE,@i,1)='Q'
                    begin
                        
                        set @QUANTIEME = left(@NUMERO_RETOURNE, 3)
                        set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- 3)
                    end
                else if substring(@CHAINE_STRUCTURE,@i,1)='G'
                    
                    set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- LEN(@CODE_GREFFE))
                else if substring(@CHAINE_STRUCTURE,@i,1)='K'
                    
                    set @NUMERO_RETOURNE = right(@NUMERO_RETOURNE, LEN(@NUMERO_RETOURNE)- 1)
                set @i=@i + 1
            end
        
        set @MonErreur = @@Error
        if @MonErreur != 0
            begin
                return
            end
        
        begin
            insert into NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_CODE_GREFFE, 
                NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, 
                NMMTR_TAILLE_CHRONO, NMMTR_STRUCTURE_NUMERO, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_DATE_ATTRIBUTION,
                NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_EXPLICATION, NMMTR_SEPARATEUR, 
                NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE)
            values(@TYPE_NUMERO_METIER, @LIBELLE, @CODE_GREFFE, 
                @ANNEE, @CHAINE_CODAGE_ANNEE,@CHAINE_LETTRE, @CHRONO_DEBUT, @CHRONO_RETOURNE, 
                @TAILLE_CHRONO, @CHAINE_STRUCTURE, 1, getdate(),
                @QUANTIEME, @GESTION_NUMEROS_ERRONES, @RAISON, @CHAINE_SEPARATEUR, 
                @TABLE_AFFECTEE,@ATTRIBUT_AFFECTE)
        
        set @MonErreur = @@Error
        if @MonErreur != 0
            begin
                rollback TRAN
                return
            end
    end
    commit TRAN
    return