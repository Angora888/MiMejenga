# ⚽ MiMejenga — Directiva SM

MVP privado para administrar mejengas, jugadores, asistencia real y créditos por pagos adicionales.

## Estructura
- `api/`: ASP.NET Core 10 + Npgsql.
- `web/`: React + Vite.
- `db/`: esquema PostgreSQL.

## Requisitos
.NET 10, Node.js 22+, PostgreSQL (o Neon).

## Preparación
1. Crear una **nueva** base de datos PostgreSQL para MiMejenga; no reutilizar la de otras aplicaciones.
2. Ejecutar `db/001_init.sql`.
3. Configurar `DATABASE_URL`, `ADMIN_PASSWORD` (12+ caracteres), `SESSION_SECRET` (32+ caracteres), `WEB_ORIGIN=http://localhost:5173` en el entorno del API.
4. Ejecutar `cd api && dotnet run --urls http://localhost:5080`.
5. Crear `web/.env.local` con `VITE_API_URL=http://localhost:5080`; ejecutar `cd web && npm install && npm run dev`.

## Reglas
- Solo los partidos con estado **played** generan ausencias.
- Un jugador cuenta como presente una sola vez por partido.
- Pago normal ₡1.500; por cada ₡500 por encima de ese importe, un **crédito**, no una asistencia física.
- Deuda = max(0, ausencias - créditos) × ₡500.
- Los jugadores nuevos comienzan a contar desde su fecha de incorporación.
- Los apodos se conservan en una tabla separada.
- La lavada de uniformes se registra de forma independiente.
- Un pago SINPE sin monto debe registrarse solo después de confirmar el importe.

**Estado:** base inicial en desarrollo. No desplegado, sin datos reales importados ni pruebas completas. Faltan auditoría, recuperación de cuenta, pagos de multas separados y políticas de respaldo antes de producción.
