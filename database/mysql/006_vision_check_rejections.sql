-- 006_vision_check_rejections.sql
-- Escaneos del escaner COM que la verificacion MES (api/mes/vision-check) no dejo pasar
-- a la prueba: EBR sin registro en Vision, ultima inspeccion NG o codigo que no es EBR.
-- Los "Sin respuesta del MES" no llegan al servidor y no se registran.
--
-- rejection_type: SIN_HISTORIAL | NG | NO_EBR
-- machine / inspected_at: la ultima inspeccion de Vision (solo en NG). inspected_at va en
-- hora local, igual que history_vision.log_datetime; rejected_at_utc en UTC como el resto
-- de las tablas _QA.

CREATE TABLE IF NOT EXISTS vision_check_rejections_QA (
    id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    rejected_at_utc   DATETIME(3)     NOT NULL,
    station_code      VARCHAR(64)     NOT NULL,
    line              VARCHAR(64)     NOT NULL DEFAULT '',
    code              VARCHAR(128)    NOT NULL,
    rejection_type    VARCHAR(16)     NOT NULL,
    reason            VARCHAR(512)    NOT NULL,
    machine           VARCHAR(64)     NULL,
    inspected_at      DATETIME(3)     NULL,
    PRIMARY KEY (id),
    KEY idx_vcr_rejected_at (rejected_at_utc),
    KEY idx_vcr_code (code),
    KEY idx_vcr_station (station_code, line, rejected_at_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
