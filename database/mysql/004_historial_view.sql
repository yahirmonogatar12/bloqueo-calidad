-- 004_historial_view.sql
-- Vista de historial de uso de estaciones (objetivo del proyecto QualityLock):
-- una fila por sesion, con fecha, hora de entrada/salida (hora local CDMX, UTC-6),
-- duracion, estacion/linea, tipo y quien estuvo.
--
-- Se apoya en station_sessions_QA + stations_QA + operators_QA. Las horas se almacenan
-- en UTC; aqui se convierten a -06:00 con CONVERT_TZ (offset fijo, no requiere tablas
-- de zonas horarias cargadas). Ajuste el offset si la planta esta en otra zona.

CREATE OR REPLACE VIEW historial_estaciones_QA AS
SELECT
    s.id                                                            AS session_id,
    st.station_code                                                 AS estacion,
    st.station_type                                                 AS tipo,
    st.station_name                                                 AS nombre_estacion,
    COALESCE(o.display_name, o.badge_code)                          AS usuario,
    o.badge_code                                                    AS username,
    DATE(CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00'))          AS fecha,
    TIME(CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00'))          AS hora_entrada,
    TIME(CONVERT_TZ(s.ended_at_utc,   '+00:00', '-06:00'))          AS hora_salida,
    CASE WHEN s.ended_at_utc IS NULL THEN NULL
         ELSE TIMESTAMPDIFF(SECOND, s.started_at_utc, s.ended_at_utc)
    END                                                             AS duracion_seg,
    CASE WHEN s.ended_at_utc IS NULL THEN 'En curso'
         ELSE SEC_TO_TIME(TIMESTAMPDIFF(SECOND, s.started_at_utc, s.ended_at_utc))
    END                                                             AS duracion,
    s.status                                                        AS estado,
    s.started_online                                                AS inicio_online,
    s.ended_online                                                  AS fin_online,
    CONVERT_TZ(s.started_at_utc, '+00:00', '-06:00')                AS inicio_local,
    CONVERT_TZ(s.ended_at_utc,   '+00:00', '-06:00')                AS fin_local
FROM station_sessions_QA s
JOIN stations_QA   st ON st.id = s.station_id
LEFT JOIN operators_QA o ON o.id = s.operator_id;

-- Consulta tipica del historial:
--   SELECT fecha, estacion, tipo, usuario, hora_entrada, hora_salida, duracion, estado
--   FROM historial_estaciones_QA
--   ORDER BY inicio_local DESC;
