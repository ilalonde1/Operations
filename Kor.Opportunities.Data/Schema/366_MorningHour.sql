-- Morning hours have up to twelve globally ranked moves (four per role).
-- Source only: apply before importing a Morning hour or recording moves 8-12.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE opportunities.DirectorHour DROP CONSTRAINT CK_DirectorHour_Kind;
ALTER TABLE opportunities.DirectorHour WITH CHECK ADD CONSTRAINT CK_DirectorHour_Kind
    CHECK (Kind IN (N'BD', N'Operations', N'Morning'));

ALTER TABLE opportunities.DirectorHourMove DROP CONSTRAINT CK_DirectorHourMove_Rank;
ALTER TABLE opportunities.DirectorHourMove WITH CHECK ADD CONSTRAINT CK_DirectorHourMove_Rank
    CHECK (MoveRank BETWEEN 1 AND 12);

COMMIT TRANSACTION;
