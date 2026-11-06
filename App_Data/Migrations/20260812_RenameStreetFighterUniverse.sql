SET XACT_ABORT ON;

BEGIN TRANSACTION;

UPDATE dbo.Lutador
SET Universo = 'Street Fighter'
WHERE LOWER(LTRIM(RTRIM(Universo))) IN (
    'street fighter iii',
    'street figther iii'
);

COMMIT TRANSACTION;
