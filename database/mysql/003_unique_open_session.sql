-- QualityLock v1 - Migration 003
-- Enforce "at most one OPEN session per station" at the database level.
--
-- MySQL has no partial/filtered indexes, but a UNIQUE index ignores NULLs, so we
-- add a generated column that equals station_id while the session is open
-- (ended_at_utc IS NULL) and NULL once it is closed. A UNIQUE index over that
-- column then permits many closed sessions but only one open session per station,
-- closing the concurrency race where two requests could both pass the application
-- "is there an open session?" check and each insert one.
--
-- Target DB: mes_production   MySQL 8.0

SET NAMES utf8mb4;

ALTER TABLE station_sessions_QA
    ADD COLUMN open_station_id INT UNSIGNED
        GENERATED ALWAYS AS (IF(ended_at_utc IS NULL, station_id, NULL)) STORED;

ALTER TABLE station_sessions_QA
    ADD UNIQUE KEY uq_ssqa_open_station (open_station_id);
