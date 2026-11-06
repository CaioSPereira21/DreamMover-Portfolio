SET XACT_ABORT ON;

IF COL_LENGTH('dbo.Lutador', 'RatingElo') IS NULL
    THROW 50001, 'Execute primeiro 20260812_AddRatingElo.sql.', 1;

BEGIN TRANSACTION;

UPDATE dbo.Lutador
SET RatingElo = 1000;

DECLARE @VencedorId int;
DECLARE @PerdedorId int;
DECLARE @RatingVencedor int;
DECLARE @RatingPerdedor int;
DECLARE @ExpectativaVencedor float;
DECLARE @Variacao int;

DECLARE partidas_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT IdVencedor, idPerdedor
    FROM dbo.Partida
    WHERE IdVencedor IS NOT NULL
      AND idPerdedor IS NOT NULL
      AND IdVencedor <> idPerdedor
      AND PontosVencedor = 2
      AND PontosPerdedor BETWEEN 0 AND 1
    ORDER BY ISNULL(DataPartida, '19000101'), Id;

OPEN partidas_cursor;
FETCH NEXT FROM partidas_cursor INTO @VencedorId, @PerdedorId;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @RatingVencedor = RatingElo FROM dbo.Lutador WHERE Id = @VencedorId;
    SELECT @RatingPerdedor = RatingElo FROM dbo.Lutador WHERE Id = @PerdedorId;

    IF @RatingVencedor IS NOT NULL AND @RatingPerdedor IS NOT NULL
    BEGIN
        SET @ExpectativaVencedor = 1.0 / (1.0 + POWER(10.0, (@RatingPerdedor - @RatingVencedor) / 400.0));
        SET @Variacao = ROUND(32.0 * (1.0 - @ExpectativaVencedor), 0);

        UPDATE dbo.Lutador
        SET RatingElo = RatingElo + @Variacao
        WHERE Id = @VencedorId;

        UPDATE dbo.Lutador
        SET RatingElo = CASE
            WHEN RatingElo - @Variacao < 100 THEN 100
            ELSE RatingElo - @Variacao
        END
        WHERE Id = @PerdedorId;
    END;

    FETCH NEXT FROM partidas_cursor INTO @VencedorId, @PerdedorId;
END;

CLOSE partidas_cursor;
DEALLOCATE partidas_cursor;

COMMIT TRANSACTION;
