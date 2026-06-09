-- QualityLock v1 - Schema initialization  (tables suffixed _QA)
-- Target DB: mes_production   MySQL 8.0

SET NAMES utf8mb4;
SET time_zone = '+00:00';

-- ──────────────────────────────────────────
-- operators_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS operators_QA (
    id                INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    badge_code        VARCHAR(64)      NOT NULL,
    employee_number   VARCHAR(32)      NOT NULL,
    display_name      VARCHAR(128)     NOT NULL,
    is_active         TINYINT(1)       NOT NULL DEFAULT 1,
    is_admin          TINYINT(1)       NOT NULL DEFAULT 0,
    created_at_utc    DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc    DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_opqa_badge   (badge_code),
    UNIQUE KEY uq_opqa_emp_num (employee_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- stations_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS stations_QA (
    id              INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    station_code    VARCHAR(32)      NOT NULL,
    station_name    VARCHAR(128)     NOT NULL,
    station_type    VARCHAR(16)      NOT NULL COMMENT 'ICT | FCT | Packing',
    host_name       VARCHAR(128)     NOT NULL DEFAULT '',
    is_active       TINYINT(1)       NOT NULL DEFAULT 1,
    created_at_utc  DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc  DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_stqa_code (station_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- station_permissions_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS station_permissions_QA (
    operator_id     INT UNSIGNED  NOT NULL,
    station_id      INT UNSIGNED  NOT NULL,
    can_operate     TINYINT(1)    NOT NULL DEFAULT 1,
    created_at_utc  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (operator_id, station_id),
    CONSTRAINT fk_spqa_operator FOREIGN KEY (operator_id) REFERENCES operators_QA (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_spqa_station  FOREIGN KEY (station_id)  REFERENCES stations_QA  (id) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- station_sessions_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS station_sessions_QA (
    id              CHAR(36)         NOT NULL,
    station_id      INT UNSIGNED     NOT NULL,
    operator_id     INT UNSIGNED     NOT NULL,
    started_at_utc  DATETIME         NOT NULL,
    ended_at_utc    DATETIME             NULL,
    status          VARCHAR(24)      NOT NULL COMMENT 'Open | Closed | ForcedClosed | OfflinePending',
    started_online  TINYINT(1)       NOT NULL DEFAULT 1,
    ended_online    TINYINT(1)       NOT NULL DEFAULT 0,
    correlation_id  VARCHAR(64)      NOT NULL DEFAULT '',
    PRIMARY KEY (id),
    KEY idx_ssqa_station_open (station_id, ended_at_utc),
    KEY idx_ssqa_operator     (operator_id),
    KEY idx_ssqa_started      (started_at_utc),
    CONSTRAINT fk_ssqa_station  FOREIGN KEY (station_id)  REFERENCES stations_QA  (id) ON DELETE RESTRICT,
    CONSTRAINT fk_ssqa_operator FOREIGN KEY (operator_id) REFERENCES operators_QA (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- station_events_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS station_events_QA (
    id              CHAR(36)         NOT NULL,
    station_id      INT UNSIGNED     NOT NULL,
    operator_id     INT UNSIGNED         NULL,
    session_id      CHAR(36)             NULL,
    event_type      VARCHAR(32)      NOT NULL,
    event_at_utc    DATETIME         NOT NULL,
    details_json    TEXT                 NULL,
    source          VARCHAR(32)      NOT NULL DEFAULT 'API',
    correlation_id  VARCHAR(64)      NOT NULL DEFAULT '',
    PRIMARY KEY (id),
    KEY idx_seqa_station    (station_id, event_at_utc),
    KEY idx_seqa_operator   (operator_id),
    KEY idx_seqa_session    (session_id),
    KEY idx_seqa_event_type (event_type),
    CONSTRAINT fk_seqa_station  FOREIGN KEY (station_id)  REFERENCES stations_QA       (id) ON DELETE RESTRICT,
    CONSTRAINT fk_seqa_operator FOREIGN KEY (operator_id) REFERENCES operators_QA      (id) ON DELETE RESTRICT,
    CONSTRAINT fk_seqa_session  FOREIGN KEY (session_id)  REFERENCES station_sessions_QA (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- admin_overrides_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS admin_overrides_QA (
    id                  CHAR(36)      NOT NULL,
    station_id          INT UNSIGNED  NOT NULL,
    admin_operator_id   INT UNSIGNED  NOT NULL,
    target_operator_id  INT UNSIGNED      NULL,
    reason              VARCHAR(32)   NOT NULL,
    comments            TEXT          NOT NULL,
    approved            TINYINT(1)    NOT NULL DEFAULT 1,
    created_at_utc      DATETIME      NOT NULL,
    PRIMARY KEY (id),
    KEY idx_aoqa_station (station_id),
    KEY idx_aoqa_admin   (admin_operator_id),
    KEY idx_aoqa_created (created_at_utc),
    CONSTRAINT fk_aoqa_station FOREIGN KEY (station_id)         REFERENCES stations_QA  (id) ON DELETE RESTRICT,
    CONSTRAINT fk_aoqa_admin   FOREIGN KEY (admin_operator_id)  REFERENCES operators_QA (id) ON DELETE RESTRICT,
    CONSTRAINT fk_aoqa_target  FOREIGN KEY (target_operator_id) REFERENCES operators_QA (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ──────────────────────────────────────────
-- client_heartbeats_QA
-- ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS client_heartbeats_QA (
    id                   CHAR(36)      NOT NULL,
    station_id           INT UNSIGNED  NOT NULL,
    sent_at_utc          DATETIME      NOT NULL,
    client_version       VARCHAR(32)   NOT NULL DEFAULT '',
    is_safe_mode         TINYINT(1)    NOT NULL DEFAULT 0,
    last_activity_at_utc DATETIME      NOT NULL,
    details_json         TEXT              NULL,
    PRIMARY KEY (id),
    KEY idx_chqa_station (station_id, sent_at_utc),
    CONSTRAINT fk_chqa_station FOREIGN KEY (station_id) REFERENCES stations_QA (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
