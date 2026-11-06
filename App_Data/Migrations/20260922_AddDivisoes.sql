SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.LutadorDivisao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LutadorDivisao
    (
        IdLutador INT NOT NULL,
        DivisaoAtual TINYINT NOT NULL,
        Sequencia INT NOT NULL CONSTRAINT DF_LutadorDivisao_Sequencia DEFAULT (0),
        PartidasDivisao INT NOT NULL CONSTRAINT DF_LutadorDivisao_Partidas DEFAULT (0),
        ColocadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorDivisao_ColocadoEm DEFAULT (SYSUTCDATETIME()),
        AtualizadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorDivisao_AtualizadoEm DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_LutadorDivisao PRIMARY KEY (IdLutador),
        CONSTRAINT FK_LutadorDivisao_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador(Id) ON DELETE CASCADE,
        CONSTRAINT CK_LutadorDivisao_Divisao CHECK (DivisaoAtual BETWEEN 0 AND 6),
        CONSTRAINT CK_LutadorDivisao_Sequencia CHECK (Sequencia BETWEEN -10 AND 10)
    );
END;

IF OBJECT_ID('dbo.PartidaDivisao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartidaDivisao
    (
        IdPartida INT NOT NULL,
        Tipo VARCHAR(20) NOT NULL,
        IdDesafiante INT NULL,
        DivisaoOrigem TINYINT NULL,
        DivisaoDestino TINYINT NULL,
        Resultado VARCHAR(20) NOT NULL,
        CriadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_PartidaDivisao_CriadoEm DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_PartidaDivisao PRIMARY KEY (IdPartida),
        CONSTRAINT FK_PartidaDivisao_Partida FOREIGN KEY (IdPartida) REFERENCES dbo.Partida(Id) ON DELETE CASCADE,
        CONSTRAINT FK_PartidaDivisao_Desafiante FOREIGN KEY (IdDesafiante) REFERENCES dbo.Lutador(Id),
        CONSTRAINT CK_PartidaDivisao_Tipo CHECK (Tipo IN ('REGULAR', 'COLOCACAO', 'PROMOCAO', 'REBAIXAMENTO')),
        CONSTRAINT CK_PartidaDivisao_Resultado CHECK (Resultado IN ('REGULAR', 'EM_COLOCACAO', 'COLOCADO', 'PROMOVIDO', 'REBAIXADO', 'MANTEVE'))
    );
END;

IF OBJECT_ID('dbo.MovimentacaoDivisao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimentacaoDivisao
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        IdLutador INT NOT NULL,
        IdPartida INT NULL,
        Tipo VARCHAR(20) NOT NULL,
        DivisaoAnterior TINYINT NULL,
        DivisaoNova TINYINT NOT NULL,
        RatingElo INT NOT NULL,
        SequenciaAnterior INT NOT NULL,
        IdAdversario INT NULL,
        CriadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_MovimentacaoDivisao_CriadoEm DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MovimentacaoDivisao PRIMARY KEY (Id),
        CONSTRAINT FK_MovimentacaoDivisao_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador(Id) ON DELETE CASCADE,
        CONSTRAINT CK_MovimentacaoDivisao_Tipo CHECK (Tipo IN ('COLOCACAO', 'PROMOCAO', 'REBAIXAMENTO'))
    );

    CREATE INDEX IX_MovimentacaoDivisao_Lutador_Data
        ON dbo.MovimentacaoDivisao (IdLutador, CriadoEmUtc DESC, Id DESC);
END;

IF OBJECT_ID('dbo.LutadorColocacao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LutadorColocacao
    (
        IdLutador INT NOT NULL,
        PartidasRealizadas INT NOT NULL CONSTRAINT DF_LutadorColocacao_Partidas DEFAULT (0),
        Vitorias INT NOT NULL CONSTRAINT DF_LutadorColocacao_Vitorias DEFAULT (0),
        Derrotas INT NOT NULL CONSTRAINT DF_LutadorColocacao_Derrotas DEFAULT (0),
        DivisaoTeste TINYINT NOT NULL CONSTRAINT DF_LutadorColocacao_DivisaoTeste DEFAULT (3),
        CriadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorColocacao_CriadoEm DEFAULT (SYSUTCDATETIME()),
        AtualizadoEmUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LutadorColocacao_AtualizadoEm DEFAULT (SYSUTCDATETIME()),
        FinalizadaEmUtc DATETIME2(0) NULL,
        CONSTRAINT PK_LutadorColocacao PRIMARY KEY (IdLutador),
        CONSTRAINT FK_LutadorColocacao_Lutador FOREIGN KEY (IdLutador) REFERENCES dbo.Lutador(Id) ON DELETE CASCADE,
        CONSTRAINT CK_LutadorColocacao_Partidas CHECK (PartidasRealizadas BETWEEN 0 AND 7),
        CONSTRAINT CK_LutadorColocacao_Resultados CHECK (Vitorias >= 0 AND Derrotas >= 0 AND Vitorias + Derrotas = PartidasRealizadas),
        CONSTRAINT CK_LutadorColocacao_DivisaoTeste CHECK (DivisaoTeste BETWEEN 0 AND 5)
    );
END;

INSERT INTO dbo.LutadorDivisao (IdLutador, DivisaoAtual, Sequencia, PartidasDivisao)
SELECT
    l.Id,
    CASE
        WHEN l.RatingElo >= 1300 THEN 0
        WHEN l.RatingElo >= 1200 THEN 1
        WHEN l.RatingElo >= 1100 THEN 2
        WHEN l.RatingElo >= 1000 THEN 3
        WHEN l.RatingElo >= 900 THEN 4
        ELSE 5
    END,
    0,
    0
FROM dbo.Lutador l
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.LutadorDivisao d
    WHERE d.IdLutador = l.Id
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.LutadorColocacao c
    WHERE c.IdLutador = l.Id
);

INSERT INTO dbo.MovimentacaoDivisao
    (IdLutador, IdPartida, Tipo, DivisaoAnterior, DivisaoNova, RatingElo, SequenciaAnterior, IdAdversario)
SELECT d.IdLutador, NULL, 'COLOCACAO', NULL, d.DivisaoAtual, l.RatingElo, 0, NULL
FROM dbo.LutadorDivisao d
INNER JOIN dbo.Lutador l ON l.Id = d.IdLutador
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.MovimentacaoDivisao m
    WHERE m.IdLutador = d.IdLutador
      AND m.Tipo = 'COLOCACAO'
);

COMMIT TRANSACTION;
