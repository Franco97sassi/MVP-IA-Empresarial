# BibliotecaApp.Etapa8 (Proyecto completo base)

Esta etapa ya incluye una base funcional de microservicios con:
- Arquitectura de microservicios
- RabbitMQ + MassTransit
- Redis
- Event-driven architecture
- Docker Compose para levantar todo local

## Servicios incluidos
- `Catalog.Api` (`/api/catalog/books`): gestiona libros y stock.
- `Loans.Api` (`/api/loans`): crea y devuelve préstamos.
- `Users.Api` (`/api/users`): crea usuarios y permite suspensión.
- `Notifications.Worker`: consumidor de eventos para notificaciones/log.
- `Contracts`: eventos de integración compartidos.

## Eventos implementados
- `BookRegistered`
- `BookAvailabilityChanged`
- `LoanCreated`
- `LoanReturned`
- `UserSuspended`

## Estructura
- `BibliotecaApp.Etapa8.sln`
- `src/BuildingBlocks/BibliotecaApp.Etapa8.Contracts`
- `src/Services/Catalog/Catalog.Api`
- `src/Services/Loans/Loans.Api`
- `src/Services/Users/Users.Api`
- `src/Services/Notifications/Notifications.Worker`

## Levantar con Docker
```bash
cd BibliotecaApp.Etapa8
docker compose up --build
```

Puertos:
- RabbitMQ UI: `http://localhost:15672` (guest/guest)
- Catalog API: `http://localhost:8081/swagger`
- Loans API: `http://localhost:8082/swagger`
- Users API: `http://localhost:8083/swagger`

## Flujo E2E ejemplo
1. Crear usuario en `Users.Api`.
2. Crear libro en `Catalog.Api`.
3. Crear préstamo en `Loans.Api`.
4. Ver logs en `Notifications.Worker`.
5. Suspender usuario y validar que Loans bloquea nuevos préstamos.

## Próximos pasos recomendados
- Persistencia real EF Core + migrations por servicio.
- Outbox + idempotencia en consumidores.
- AuthN/AuthZ JWT.
- API Gateway (YARP/Ocelot).
- Observabilidad (OpenTelemetry + Seq/Grafana).
