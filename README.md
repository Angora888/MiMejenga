# ⚽ MiMejenga — Directiva SM

MVP exclusivo para la mejenga de **Directiva SM**. Código en GitHub; **aún no desplegado ni probado integralmente**.

## Qué incluye

- Panel administrativo: temporadas, jugadores, apodos, fechas, asistencia real, lavado de uniformes, pagos y ranking.
- Cuentas individuales de los jugadores: nombre, correo y contraseña.
- Cada semana la directiva crea una **mejenga programada** y comparte el enlace único `https://TU-WEB/m/<token>` por WhatsApp.
- Los compas se apuntan solos, con **2 cupos de portero + 12 cupos de campo**.
- Quien se desapunta libera su espacio; puede cambiar de posición si hay cupo.
- La inscripción y asistencia son datos **distintos**; nadie recibe asistencia automática por anotarse.
- La capacidad se valida en una transacción PostgreSQL, bloqueando la fila de la mejenga para evitar inscripciones simultáneas que excedan el límite.
- Un jugador inscrito en la temporada puede ingresar con su cuenta en las siguientes semanas (no necesita registrarse de nuevo).

## Arquitectura

| Capa | Tecnología |
|---|---|
| Web administrador + inscripción pública | React + TypeScript + Vite |
| API | ASP.NET Core 10 + Npgsql |
| Persistencia | PostgreSQL / Neon |
| Hosting previsto | Vercel + Azure App Service |

## Instalación local (cuando configuremos Neon)

1. Crear base de datos **nueva** para MiMejenga (sin tocar otras aplicaciones).
2. Ejecutar, en este orden: `db/001_init.sql` y `db/002_self_service.sql`.
3. En la terminal del API configurar variables reales `DATABASE_URL`, `ADMIN_PASSWORD` (mínimo 12 caracteres), `SESSION_SECRET` (mínimo 32 caracteres aleatorios) y `WEB_ORIGIN=http://localhost:5173`. Usar como guía `api/.env.example`; el API no lee archivos `.env` automáticamente.
4. Ejecutar `cd api && dotnet restore && dotnet run --urls http://localhost:5080`.
5. Crear `web/.env.local` con `VITE_API_URL=http://localhost:5080`, ejecutar `cd web && npm install && npm run dev`.
6. Entrar al administrador; crear temporada (2026), jugadores y una fecha con estado **Programada**.
7. Seleccionar la fecha, copiar el enlace en la tarjeta **Enlace para apuntarse** y compartirlo por WhatsApp.
8. Un jugador abre el enlace, crea su usuario y elige **Portero** o **Jugador de campo**. Cuando lleguen 2 y 12, las plazas quedan completas.

## Reglas de dinero

- Cuota semanal: ₡1.500; multa por ausencia: ₡500 (configurable por temporada).
- Cada ₡500 sobre ₡1.500 genera un crédito, no una asistencia física.
- Una mejenga cancelada o programada NO genera ausencias.
- Las cuentas creadas por jugadores nuevos comienzan a computar ausencias desde la fecha de alta.
- SINPE/efectivo se guardan como pagos confirmados por la directiva; el texto “SINPE” no comprueba por sí solo la transferencia.
- **El dashboard distingue asistencia real de créditos**, así evita confundir quién jugó con quién pagó ausencias anteriores.

## Antes de ponerla en producción

- Falta enlazar con seguridad las cuentas nuevas con jugadores antiguos ya importados, verificando identidad ante la directiva; actualmente NO se pueden reclamar perfiles existentes automáticamente (evita suplantaciones).
- Falta recuperación/verificación del correo, protección ante fuerza bruta, auditoría, permisos más granulares y respaldos.
- No hay Stripe, SINPE automático ni cobros bancarios: el registro de pagos es manual.
- No se ha migrado ni conciliado el historial 2026 del WhatsApp; el último reporte incluye créditos por pagos extras que requieren separación.
- La sesión de miembros usa tokens firmados en sessionStorage, no cookies seguras; endurecer autenticación antes de uso general.
- Verificar build, API, migraciones y pruebas de concurrencia antes del despliegue.

El repositorio actual es **público**, aunque la app sea de uso privado; cambiar la visibilidad en GitHub si se desea código privado.
