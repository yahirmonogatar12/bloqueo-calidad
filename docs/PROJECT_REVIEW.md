# Revision tecnica de QualityLock

## Resumen

El proyecto tiene una base sana: solucion por capas, contratos compartidos, servicios de aplicacion claros, repositorios Dapper simples y pruebas unitarias para las reglas principales. Para una v1 interna se ve ordenado y mantenible.

El punto debil no es la estructura, sino el cierre operativo: offline, bypass y safe mode estan parcialmente implementados y la documentacion anterior los describia como completos. Antes de usarlo en una linea real conviene cerrar esas brechas y sacar secretos del repo.

## Evidencia revisada

- CodeGraph: 52 archivos fuente relevantes bajo `Bloqueo calidad`.
- Graphify detect: 154 archivos soportados, ~47,920 palabras, 142 archivos de codigo.
- Graphify AST: 5,548 nodos y 9,697 relaciones estructurales.
- Pruebas: `dotnet test QualityLock.slnx` paso con 11 pruebas exitosas.

## Lo que esta bien

- Separacion por capas consistente: API no habla directo con MySQL, usa Application/Infrastructure.
- DTOs y enums compartidos reducen divergencias entre API y cliente.
- Repositorios con SQL explicito facilitan auditar queries.
- El esquema MySQL tiene llaves foraneas e indices basicos para operadores, estaciones, sesiones y eventos.
- El cliente incluye setup de estacion, bootstrap de cache, lock fullscreen, hook de teclado, heartbeat y bypass HMAC.
- Las pruebas cubren decisiones de validacion y reglas basicas de sesion.

## Riesgos principales

1. Secretos en configuracion

`src/QualityLock.Api/appsettings.json` contiene una cadena MySQL real. Hay que moverla a secretos/variables de entorno y rotar esa credencial si ya fue compartida.

2. Offline incompleto

`LocalStateService` puede escribir `event-queue.jsonl`, pero no hay un proceso que lea esa cola y la mande a `/api/events`. Ademas, al relock de una sesion offline se intenta cerrar sesion contra API como si fuera online y se ignora el fallo.

3. Bypass local no desbloquea

`AdminPanelForm` valida `bypass.json` cuando no hay backend, pero solo muestra un mensaje. No cambia el estado de `LockForm`, no oculta la pantalla de bloqueo y no audita `BypassUsed`.

4. Safe mode incompleto

`SafeModeService` existe, pero no encontre llamadas a `RegisterCrash()` ni `ClearSafeMode()` en el flujo de arranque/cierre. En safe mode el UI cambia color/texto, pero el bloqueo agresivo sigue instalando hook y Task Manager lock.

5. Seguridad de administracion

El setup desde tray y la detencion del servicio usan PIN hardcodeado `admin1234`. Tambien falta autenticacion/autorizacion en la API; cualquiera que llegue a los endpoints podria registrar eventos, estaciones u overrides si no hay controles externos.

6. Consistencia de sesiones

La regla "una sesion abierta por estacion" se valida en aplicacion, pero no se refuerza en base de datos. Dos requests concurrentes podrian abrir sesiones duplicadas. Tambien `StartSession` crea sesion y luego evento en operaciones separadas, sin transaccion unica.

7. Auditoria JSON fragile

Algunos `DetailsJson` se construyen con interpolacion de strings. Si badge, reason o comentarios traen comillas u otros caracteres, el JSON puede quedar invalido.

8. Higiene del repo

No hay `.gitignore` local y existen carpetas `bin/`, `obj/` y `logs/` dentro del arbol. Eso aumenta ruido y riesgo de versionar binarios/logs.

## Brechas entre documentacion y codigo

| Tema | Documentacion previa | Estado observado |
|---|---|---|
| Cache por heartbeat | Dice que se actualiza en cada heartbeat | Solo se actualiza en startup y registro de estacion |
| Sync offline | Dice que sincroniza al reconectar | No hay drenado automatico de `event-queue.jsonl` |
| Safe mode | Dice que reduce bloqueo agresivo | Solo cambia UI; hook/Task Manager siguen activos |
| Bypass | Dice que permite recuperacion local | Se valida, pero no desbloquea el lock screen |
| Endpoints | No listaba registro de estacion | Existe `PUT /api/stations/{stationCode}` |

## Siguientes pasos recomendados

1. Sacar secretos de `appsettings.json`, agregar `.gitignore` y limpiar `bin/`, `obj/`, `logs/` del versionado.
2. Implementar sincronizador offline: guardar eventos con el shape de `StationEventRequest`, drenar cola al reconectar y cubrir cierre de sesion offline.
3. Corregir bypass para que desbloquee de forma controlada, registre `BypassUsed` y deje evidencia local/remota cuando vuelva la API.
4. Integrar safe mode real: registrar crashes, limpiar arranque sano y reducir bloqueo cuando `SafeMode = true`.
5. Agregar auth minima a API o aislarla detras de red confiable con TLS, firewall y controles de cliente.
6. Hacer transaccional `StartSession` + evento y reforzar una sesion abierta por estacion a nivel de BD.
7. Reemplazar interpolaciones JSON por `JsonSerializer.Serialize`.
8. Ampliar pruebas de API para validacion, sesiones, eventos, bootstrap, overrides, error middleware y flujos offline del cliente.

## Veredicto

Como base de producto esta bien planteado. Como sistema para correr en piso de produccion todavia le faltan controles de seguridad, recuperacion y consistencia. Yo lo trataria como prototipo avanzado o v1 interna, no como release final, hasta cerrar los puntos anteriores.
