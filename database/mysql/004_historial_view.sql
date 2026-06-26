-- 004_historial_view.sql
-- Vista de historial de uso de estaciones (objetivo del proyecto QualityLock):
-- una fila por sesión (tanto sesiones estándar como ventanas de ajuste cerradas),
-- con fecha, hora de entrada/salida (hora local CDMX, UTC-6), duración,
-- estación/línea, tipo y operador asociado.
--
-- Se compone de:
-- 1. Sesiones estándar de station_sessions_QA (Open / Closed / etc.)
-- 2. Eventos de ajuste de ventana 'WindowClosed' en station_events_QA, donde la duración
--    se extrae dinámicamente del campo JSON 'details_json' (propiedad 'OpenSeconds').
--
-- Las horas se almacenan en UTC y se convierten a -06:00 (CDMX) con CONVERT_TZ.

CREATE OR REPLACE ALGORITHM = UNDEFINED VIEW historial_estaciones_QA AS
SELECT
    s.id                                                            AS session_id,
    st.station_code                                                 AS estacion,
    st.host_name                                                    AS linea,
    st.station_type                                                 AS tipo,
    st.station_name                                                 AS nombre_estacion,
    COALESCE(o.display_name, o.badge_code)                          AS usuario,
    o.badge_code                                                    AS username,
    CAST(CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00') AS DATE)  AS fecha,
    CAST(CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00') AS TIME)  AS hora_entrada,
    CAST(CONVERT_TZ(s.ended_at_utc,   '+00:00', '-06:00') AS TIME)  AS hora_salida,
    CASE 
        WHEN s.ended_at_utc IS NULL THEN NULL
        ELSE TIMESTAMPDIFF(SECOND, s.started_at_utc, s.ended_at_utc)
    END                                                             AS duracion_seg,
    CASE 
        WHEN s.ended_at_utc IS NULL THEN 'En curso'
        ELSE SEC_TO_TIME(TIMESTAMPDIFF(SECOND, s.started_at_utc, s.ended_at_utc))
    END                                                             AS duracion,
    s.status                                                        AS estado,
    s.started_online                                                AS inicio_online,
    s.ended_online                                                  AS fin_online,
    CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00')                AS inicio_local,
    CONVERT_TZ(s.ended_at_utc,   '+00:00', '-06:00')                AS fin_local
FROM station_sessions_QA s
JOIN stations_QA   st ON st.id = s.station_id
LEFT JOIN operators_QA o ON o.id = s.operator_id

UNION ALL

SELECT
    e.id                                                            AS session_id,
    st.station_code                                                 AS estacion,
    st.host_name                                                    AS linea,
    st.station_type                                                 AS tipo,
    st.station_name                                                 AS nombre_estacion,
    COALESCE(o.display_name, o.badge_code)                          AS usuario,
    o.badge_code                                                    AS username,
    CAST(CONVERT_TZ((e.event_at_utc - INTERVAL os.secs SECOND), '+00:00', '-06:00') AS DATE) AS fecha,
    CAST(CONVERT_TZ((e.event_at_utc - INTERVAL os.secs SECOND), '+00:00', '-06:00') AS TIME) AS hora_entrada,
    CAST(CONVERT_TZ(e.event_at_utc, '+00:00', '-06:00') AS TIME)    AS hora_salida,
    os.secs                                                         AS duracion_seg,
    SEC_TO_TIME(os.secs)                                            AS duracion,
    'Ajuste'                                                        AS estado,
    NULL                                                            AS inicio_online,
    NULL                                                            AS fin_online,
    CONVERT_TZ((e.event_at_utc - INTERVAL os.secs SECOND), '+00:00', '-06:00') AS inicio_local,
    CONVERT_TZ(e.event_at_utc, '+00:00', '-06:00')                  AS fin_local
FROM station_events_QA e
JOIN stations_QA st ON st.id = e.station_id
LEFT JOIN operators_QA o ON o.id = e.operator_id
JOIN JSON_TABLE(e.details_json, '$' COLUMNS (secs INT PATH '$.OpenSeconds')) os ON os.secs IS NOT NULL
WHERE e.event_type = 'WindowClosed';
