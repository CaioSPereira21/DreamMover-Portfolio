SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Torre', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Torre
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Torre PRIMARY KEY,
        IdLutador INT NOT NULL,
        UniversoAlvo NVARCHAR(255) NOT NULL,
        Tipo VARCHAR(20) NOT NULL,
        Status VARCHAR(20) NOT NULL CONSTRAINT DF_Torre_Status DEFAULT ('EmAndamento'),
        AndarAtual INT NOT NULL CONSTRAINT DF_Torre_AndarAtual DEFAULT (1),
        TotalAndares INT NOT NULL CONSTRAINT DF_Torre_TotalAndares DEFAULT (5),
        QuantidadeElencoNoInicio INT NOT NULL,
        DataInicio DATETIME2(7) NOT NULL CONSTRAINT DF_Torre_DataInicio DEFAULT (SYSDATETIME()),
        DataConclusao DATETIME2(7) NULL,
        CONSTRAINT FK_Torre_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador (Id),
        CONSTRAINT CK_Torre_Tipo CHECK (Tipo IN ('Prova', 'Invasao')),
        CONSTRAINT CK_Torre_Status CHECK (Status IN ('EmAndamento', 'Concluida', 'Fracassada', 'Abandonada')),
        CONSTRAINT CK_Torre_Andares CHECK (AndarAtual >= 1 AND TotalAndares >= 1)
    );

    CREATE INDEX IX_Torre_Lutador_Status
        ON dbo.Torre (IdLutador, Status, DataInicio DESC);

    CREATE INDEX IX_Torre_Universo
        ON dbo.Torre (UniversoAlvo, Status);
END;

IF OBJECT_ID(N'dbo.TorreAndar', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TorreAndar
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TorreAndar PRIMARY KEY,
        IdTorre INT NOT NULL,
        Ordem INT NOT NULL,
        IdAdversario INT NOT NULL,
        RatingEloNoSorteio INT NOT NULL,
        NivelNoSorteio INT NOT NULL,
        Funcao VARCHAR(20) NOT NULL CONSTRAINT DF_TorreAndar_Funcao DEFAULT ('Comum'),
        Status VARCHAR(20) NOT NULL CONSTRAINT DF_TorreAndar_Status DEFAULT ('Pendente'),
        IdPartida INT NULL,
        DataConclusao DATETIME2(7) NULL,
        CONSTRAINT FK_TorreAndar_Torre FOREIGN KEY (IdTorre) REFERENCES dbo.Torre (Id),
        CONSTRAINT FK_TorreAndar_Adversario FOREIGN KEY (IdAdversario) REFERENCES dbo.Lutador (Id),
        CONSTRAINT FK_TorreAndar_Partida FOREIGN KEY (IdPartida) REFERENCES dbo.Partida (Id),
        CONSTRAINT UQ_TorreAndar_Ordem UNIQUE (IdTorre, Ordem),
        CONSTRAINT UQ_TorreAndar_Adversario UNIQUE (IdTorre, IdAdversario),
        CONSTRAINT CK_TorreAndar_Ordem CHECK (Ordem >= 1),
        CONSTRAINT CK_TorreAndar_Funcao CHECK (Funcao IN ('Comum', 'Forte', 'Subchefe', 'Chefe')),
        CONSTRAINT CK_TorreAndar_Status CHECK (Status IN ('Pendente', 'Executando', 'Vencido', 'Derrotado'))
    );

    CREATE INDEX IX_TorreAndar_Torre_Status
        ON dbo.TorreAndar (IdTorre, Status, Ordem);
END;

IF OBJECT_ID(N'dbo.ConquistaUniverso', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConquistaUniverso
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConquistaUniverso PRIMARY KEY,
        IdLutador INT NOT NULL,
        Universo NVARCHAR(255) NOT NULL,
        NivelTrofeu TINYINT NOT NULL CONSTRAINT DF_ConquistaUniverso_Nivel DEFAULT (1),
        IdTorreOrigem INT NOT NULL,
        QuantidadeElencoNaConquista INT NOT NULL,
        DataPrimeiraConquista DATETIME2(7) NOT NULL CONSTRAINT DF_ConquistaUniverso_Data DEFAULT (SYSDATETIME()),
        DataAtualizacao DATETIME2(7) NOT NULL CONSTRAINT DF_ConquistaUniverso_Atualizacao DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_ConquistaUniverso_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador (Id),
        CONSTRAINT FK_ConquistaUniverso_Torre FOREIGN KEY (IdTorreOrigem) REFERENCES dbo.Torre (Id),
        CONSTRAINT UQ_ConquistaUniverso_LutadorUniverso UNIQUE (IdLutador, Universo),
        CONSTRAINT CK_ConquistaUniverso_Nivel CHECK (NivelTrofeu BETWEEN 1 AND 4)
    );

    CREATE INDEX IX_ConquistaUniverso_Universo
        ON dbo.ConquistaUniverso (Universo, NivelTrofeu);
END;

COMMIT TRANSACTION;
