# ⚽ MiMejenga — Directiva SM

App de uso exclusivo para la mejenga **Directiva SM**, con cuenta individual por Google y control de la directiva. **Proyecto en desarrollo, sin despliegue ni base de datos conectada.**

## Flujo aprobado

1. La **directiva** accede al panel de administración, crea una temporada y una mejenga **Programada**.
2. Copia el enlace único de esa semana y lo comparte por WhatsApp.
3. Cada jugador abre el enlace, **inicia sesión con Google** (se crea su cuenta automáticamente la primera vez) y se apunta como **Portero** (2 plazas) o **Jugador de campo** (12 plazas).
4. El registro en la lista no equivale a jugar ni a haber pagado; puede desapuntarse mientras esté Programada.
5. La directiva ve los apuntados, marca el partido como **Jugó** o **Cancelada**, confirma **asistencia real**, registra **SINPE/efectivo y monto** y marca **Lavada** cuando corresponda.
6. En la ruta **/mi**, el jugador ve su dashboard privado: partidos realmente jugados, historial de pagos, **total aportado al fondo de la fiesta**, créditos por pagos extra, lavadas y faltas por cobrar.

**No se importa el historial de 2026, ni ningún registro de Excel o WhatsApp.** Las cuentas y balances empiezan en cero. Para un jugador nuevo, las ausencias solo cuentan desde la mejenga en la que se inscribió por primera vez (no desde enero).

## Arquitectura

| Capa | Herramienta |
|---|---|
| Web/PWA | React + TypeScript + Vite |
| API | ASP.NET Core 10 + Npgsql + Google.Apis.Auth |
| DB | PostgreSQL / Neon |
| Hosting previsto | Vercel (web) + Azure App Service (API) |

## Variables necesarias

En la API configurar en el entorno del proceso:
- `DATABASE_URL` (cadena de PostgreSQL de proyecto nuevo)
- `ADMIN_PASSWORD` contraseña privada de directiva de al menos 12 caracteres
- `SESSION_SECRET` secreto aleatorio de al menos 32 caracteres
- `GOOGLE_CLIENT_ID` ID del OAuth 2.0 Web Client de Google (solo ID, no Client Secret)
- `WEB_ORIGIN` dominio web (en desarrollo `http://localhost:5173`)

En `web/.env.local`:
- `VITE_API_URL=http://localhost:5080`
- `VITE_GOOGLE_CLIENT_ID=<MISMO_WEB_CLIENT_ID_DE_GOOGLE>`

Los archivos `.env.example` son referencias: el API NO los carga automáticamente. **Nunca subir contraseñas, connection strings ni secretos a GitHub.**

## Google Cloud Console

Crear un proyecto y un OAuth Client ID de tipo **Web application**. Configurar la pantalla de consentimiento y los **Authorized JavaScript origins** (al menos `http://localhost:5173` y luego el dominio real de Vercel). El backend **verifica la firma y audiencia del ID token** recibido antes de crear la cuenta. La identidad real es el `sub` verificado de Google, no el correo escrito por un jugador. Google OAuth no está todavía configurado en tu cuenta.

## Migraciones PostgreSQL

Para una BD **vacía** ejecutar en orden:
1. `db/001_init.sql` — temporadas, jugadores, asistencias, pagos y cálculo de balance.
2. `db/002_self_service.sql` — enlaces por partido, cupos y lista semanal.
3. `db/003_google_accounts.sql` — usuarios Google; la antigua tabla para contraseñas no se utiliza.
4. `db/004_party_fund.sql` — fondo de fiesta independiente de los pagos de la mejenga.

No insertar datos históricos. La directiva crea la nueva temporada y el primer partido desde el panel.

## Correr localmente

1. `cd api && dotnet restore && dotnet run --urls http://localhost:5080`
2. `cd web && npm install && npm run dev`
3. Administrador: `/`; dashboard personal: `/mi`; enlace semanal: `/m/{token}`.

## Reglas de saldo

- Cuota habitual ₡1.500; cada ausencia en una mejenga **jugada** vale ₡500.
- Por cada ₡500 adicionales sobre la cuota habitual se crea un crédito (registrado por la directiva).
- Crédito ≠ asistencia real. La cifra “Partidos jugados” solo aumenta cuando la directiva marca la asistencia después del encuentro.
- El monto “Abonado para la fiesta” suma exclusivamente los asientos registrados por la directiva en **Aportes para la fiesta**; los ₡1.500 de cada mejenga se contabilizan por separado. No se descuenta ni aporta al fondo automáticamente hasta definir la distribución.
- Lavada se almacena aparte; no tiene un descuento automático no acordado.
- Cancelada o Programada no generan ausencias.
- Inscripción semanal protegida por transacción SQL y bloqueo de fila para controlar 2 + 12 cupos.

## Pendientes para uso real

- Neon creado (proyecto `weathered-frost-79011588`, base `mimejenga`), cuatro migraciones ejecutadas y temporada 2026 inicializada vacía. Falta configurar Google OAuth, conectar los servidores al proyecto, desplegar API/web y probar el flujo en varios celulares.
- Compilar y verificar la API y web (no se dispone de .NET SDK en el entorno de edición actual); probar concurrencia, permisos y límites.
- Verificación del dinero depositado y conciliación de SINPE; no hay integración con el banco.
- Endurecer autenticación, límites anti-abuso, sesiones, auditoría de pagos y backups. Actualmente la directiva usa una contraseña administrativa; se recomienda cambiarla por **roles de directiva ligados a Google** antes del despliegue definitivo.
- En la versión actual, el jugador solo puede apuntarse si abre la convocatoria; la directiva todavía tiene que marcar asistencia y pagos manualmente.

**Nota:** el repositorio GitHub se encuentra público; el acceso web del administrador es privado, pero eso no significa que el código sea privado.
