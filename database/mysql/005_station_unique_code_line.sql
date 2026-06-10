-- 005_station_unique_code_line.sql
-- Permite que el mismo codigo de estacion exista en lineas distintas.
-- Antes: station_code era UNICO (uq_stqa_code) -> ICT-01 solo podia existir una vez.
-- Ahora: la unicidad es la COMBINACION (station_code, host_name) -> ICT-01 puede estar
-- en M1 y en M2 como estaciones separadas. host_name = linea de produccion.
--
-- IMPORTANTE: ejecutar con la tabla sin duplicados previos de (station_code, host_name).

ALTER TABLE stations_QA DROP INDEX uq_stqa_code;

ALTER TABLE stations_QA
    ADD UNIQUE KEY uq_stqa_code_line (station_code, host_name);
