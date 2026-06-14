# SNUS – Distributed Sensor Monitoring System

## Prerequisites

Install the following before running the system:

| Tool | Version | Link |
|---|---|---|
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download |
| Docker Desktop | latest | https://www.docker.com/products/docker-desktop |
| Git | any | https://git-scm.com |

### Optional (for Kubernetes – Student 3)
- `minikube` – https://minikube.sigs.k8s.io/docs/start/
- `kubectl` – bundled with Docker Desktop or install separately

---

## Quick Start (docker-compose)

```bash
# 1. Clone the repository
git clone https://github.com/IgorMaljik/Distributed-Sensor-Monitoring-System
cd Distributed-Sensor-Monitoring-System

# 2. Start all services + PostgreSQL
docker-compose up --build

# 3. Run the sensors (in a separate terminal)
cd SensorClient
dotnet run -- http://localhost:5050
```

Services are available at:
- **Ingress (single entry point):**   http://localhost:5050
- **IngestionService (direct):**      http://localhost:5001
- **NotificationService (direct):**   http://localhost:5002
- **Swagger (IngestionService):**     http://localhost:5001/swagger

---

## Kubernetes (Minikube)

```bash
# 1. Start Minikube
minikube start

# 2. Build Docker images (if not already built)
docker-compose build

# 3. Deploy to Minikube
chmod +x k8s/deploy.sh
./k8s/deploy.sh

# 4. Get the access URL
minikube service ingress --url
```

> **Note:** On macOS with Docker driver, keep the terminal running `minikube service` open while using the system.

---

## Local Development (without Docker)

Start PostgreSQL separately, then run each service in its own terminal:

```bash
# Terminal 1 – PostgreSQL (Docker, database only)
docker run --name snus-pg -e POSTGRES_DB=snusdb -e POSTGRES_USER=snus \
  -e POSTGRES_PASSWORD=snuspassword -p 5432:5432 postgres:16

# Terminal 2 – IngestionService
cd IngestionService
dotnet run

# Terminal 3 – ConsensusService
cd ConsensusService
dotnet run

# Terminal 4 – NotificationService
cd NotificationService
dotnet run

# Terminal 5 – Ingress
cd Ingress
dotnet run

# Terminal 6 – SensorClient
cd SensorClient
dotnet run -- http://localhost:5000 # macOS: use 5050 if port 5000 is unavailable
```

---

## EF Core Migrations

Migrations are applied automatically on IngestionService startup.
To create a new migration manually:

```bash
cd IngestionService
dotnet ef migrations add <MigrationName> --project ../Shared --context AppDbContext
dotnet ef database update
```

---

## Project Structure

```
Distributed-Sensor-Monitoring-System/
├── Shared/                  # DTOs, EF models, DbContext – shared across all services
│   ├── DTOs/
│   ├── Models/
│   ├── Enums/
│   └── Data/AppDbContext.cs
├── SensorClient/            # Console app – sensor simulator (Student 1)
├── IngestionService/        # ASP.NET Web API – data ingestion (Student 2)
├── ConsensusService/        # Worker Service – BFT consensus (Student 3)
├── NotificationService/     # ASP.NET + SignalR – alarm notifications (Student 3)
├── Ingress/                 # YARP reverse proxy (Student 3)
├── k8s/                     # Kubernetes YAML files (Student 3)
├── docker-compose.yml
└── snus.sln
```

---

## API Contract

### POST /api/ingest
Receives an encrypted, signed message from a sensor.
```json
{
  "sensorId": "SENSOR-01",
  "encryptedPayload": "<base64>",
  "signature": "<base64>",
  "messageId": 42,
  "sentAt": "2024-01-01T12:00:00Z"
}
```

### GET /api/reports/sensors
Returns the current status of all sensors.

### GET /api/reports/history/{sensorId}?limit=100
Returns the measurement history for a given sensor.

### GET /api/reports/consensus?limit=60
Returns the most recent consensus values.

### SignalR hub: /hubs/alarms
Clients connect here and listen for the `AlarmTriggered` event.

---
