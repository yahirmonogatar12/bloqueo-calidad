-- QualityLock v1 - Demo seed data  (tables suffixed _QA)
-- Run after 001_init.sql

SET NAMES utf8mb4;

-- ──────────────────────────────────────────
-- Stations
-- ──────────────────────────────────────────
INSERT INTO stations_QA (station_code, station_name, station_type, host_name, is_active) VALUES
    ('ICT-01', 'ICT Station 1', 'ICT',     'PCICT01',   1),
    ('ICT-02', 'ICT Station 2', 'ICT',     'PCICT02',   1),
    ('FCT-01', 'FCT Station 1', 'FCT',     'PCFCT01',   1),
    ('PKG-01', 'Packing Line 1','Packing', 'PCPKG01',   1)
ON DUPLICATE KEY UPDATE station_name = VALUES(station_name);

-- ──────────────────────────────────────────
-- Operators
-- ──────────────────────────────────────────
INSERT INTO operators_QA (badge_code, employee_number, display_name, is_active, is_admin) VALUES
    ('ADMIN001', 'A001', 'Admin User',     1, 1),
    ('EMP001',   'E001', 'Juan Lopez',     1, 0),
    ('EMP002',   'E002', 'Maria Garcia',   1, 0),
    ('EMP003',   'E003', 'Carlos Reyes',   1, 0),
    ('EMP004',   'E004', 'Ana Martinez',   0, 0)
ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), is_active = VALUES(is_active);

-- ──────────────────────────────────────────
-- Permissions
-- ──────────────────────────────────────────
-- Admin → all stations
INSERT INTO station_permissions_QA (operator_id, station_id, can_operate)
SELECT o.id, s.id, 1
FROM operators_QA o, stations_QA s
WHERE o.badge_code = 'ADMIN001'
ON DUPLICATE KEY UPDATE can_operate = 1;

-- EMP001 → ICT-01, ICT-02
INSERT INTO station_permissions_QA (operator_id, station_id, can_operate)
SELECT o.id, s.id, 1
FROM operators_QA o
JOIN stations_QA s ON s.station_code IN ('ICT-01','ICT-02')
WHERE o.badge_code = 'EMP001'
ON DUPLICATE KEY UPDATE can_operate = 1;

-- EMP002 → FCT-01
INSERT INTO station_permissions_QA (operator_id, station_id, can_operate)
SELECT o.id, s.id, 1
FROM operators_QA o
JOIN stations_QA s ON s.station_code IN ('FCT-01')
WHERE o.badge_code = 'EMP002'
ON DUPLICATE KEY UPDATE can_operate = 1;

-- EMP003 → ICT-01, FCT-01, PKG-01
INSERT INTO station_permissions_QA (operator_id, station_id, can_operate)
SELECT o.id, s.id, 1
FROM operators_QA o
JOIN stations_QA s ON s.station_code IN ('ICT-01', 'FCT-01', 'PKG-01')
WHERE o.badge_code = 'EMP003'
ON DUPLICATE KEY UPDATE can_operate = 1;
