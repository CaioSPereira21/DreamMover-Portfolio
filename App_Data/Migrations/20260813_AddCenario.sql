IF OBJECT_ID(N'dbo.Cenario', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cenario
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cenario PRIMARY KEY,
        Nome NVARCHAR(255) NOT NULL,
        ArquivoDef NVARCHAR(260) NOT NULL,
        Universo NVARCHAR(255) NULL,
        Autor NVARCHAR(255) NULL,
        Imagem NVARCHAR(255) NULL,
        Ativo BIT NOT NULL CONSTRAINT DF_Cenario_Ativo DEFAULT (1),
        Excluido BIT NOT NULL CONSTRAINT DF_Cenario_Excluido DEFAULT (0),
        DataCadastro DATETIME2(7) NOT NULL CONSTRAINT DF_Cenario_DataCadastro DEFAULT (SYSDATETIME()),
        DataAtualizacao DATETIME2(7) NULL,
        CONSTRAINT UQ_Cenario_ArquivoDef UNIQUE (ArquivoDef)
    );
END;
