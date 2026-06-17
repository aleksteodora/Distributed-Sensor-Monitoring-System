# SNUS – Distributed Sensor Monitoring System

## Prerequisites

Install the following before running the system:

| Tool | Version | Link |
|---|---|---|
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download |
| Docker Desktop | latest | https://www.docker.com/products/docker-desktop |
| Git | any | https://git-scm.com |

### Optional (for Kubernetes)
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

PostgreSQL must be running locally. On macOS with Homebrew:

```bash
brew services start postgresql@18
# Create user and database (first time only):
psql postgres -c "CREATE USER snus WITH PASSWORD 'snuspassword';"
psql postgres -c "CREATE DATABASE snusdb OWNER snus;"
```

Then run each service in its own terminal:

```bash
# Terminal 1
cd IngestionService && dotnet run

# Terminal 2
cd ConsensusService && dotnet run

# Terminal 3
cd NotificationService && dotnet run

# Terminal 4
cd Ingress && dotnet run

# Terminal 5
cd SensorClient && dotnet run -- http://localhost:5050
```

> **Troubleshooting:** If sensors are rejected with `Replay rejected`, reset MessageId counters:
> ```bash
> psql -U snus -d snusdb -c 'UPDATE "Sensors" SET "LastMessageId" = 0;'
> ```

---

## Multi-Machine Demo (LAN)

To run the system across multiple laptops on the same network (e.g. for a demo or defense):

**Server laptop** – runs all services:
```bash
docker-compose up --build
```

Find its local IP address:
```bash
# macOS/Linux
ipconfig getifaddr en0   # or: ip a
# Windows
ipconfig
```

**Client laptop(s)** – run only SensorClient, pointing to the server IP:
```bash
cd SensorClient
dotnet run -- http://<SERVER_IP>:5050
```

No additional configuration is needed – Ingress listens on `0.0.0.0:5050` inside Docker, which is exposed to the LAN.

> **Note:** Make sure the server laptop's firewall allows inbound connections on port 5050.
> On macOS this is usually not an issue. On Windows, allow the port in Windows Defender Firewall.

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

### POST /api/notifications/alarm
Internal endpoint – called by IngestionService to forward alarm events to NotificationService.

### SignalR hub: /hubs/alarms
Clients connect here and listen for the `AlarmTriggered` event.

---

## Security

Communication between SensorClient and the server is secured at the message level, independently of the transport layer. This means security guarantees hold even when sensors communicate over a local network instead of localhost.

### Encryption – AES-256-CBC

Each sensor payload is encrypted using AES-256-CBC before transmission. The symmetric key is shared between the sensor and IngestionService via configuration (`appsettings.json`, excluded from version control). An attacker intercepting network traffic cannot read the sensor values without the key.

### Digital Signatures – ECDSA P-256

Every message is signed using the sensor's private ECDSA P-256 key. IngestionService verifies the signature using the corresponding public key before accepting any data. This prevents:
- **Spoofing** – an attacker cannot impersonate a sensor without its private key.
- **Tampering** – any modification of the message in transit invalidates the signature.

### Replay Attack Protection

Each message includes a monotonically increasing `MessageId` and a `SentAt` timestamp. IngestionService rejects any message whose `MessageId` is not strictly greater than the last accepted ID for that sensor. This prevents an attacker from recording and replaying a valid encrypted+signed message.

### DoS Protection – Rate Limiting

If any sensor ID sends more than 10 requests per second, IngestionService returns HTTP 429 and temporarily blocks further requests from that sensor. This protects the server from flooding by a compromised or malfunctioning sensor.

### Risk Analysis

| Threat | Mitigation |
|---|---|
| Eavesdropping on LAN | AES-256-CBC payload encryption |
| Sensor impersonation | ECDSA signature verification |
| Message replay | Monotonic MessageId per sensor |
| DoS / flooding | Rate limiting (>10 req/s → 429 + block) |
| Malicious sensor data | BFT consensus excludes statistical outliers |
| Key compromise | Keys stored outside version control (`appsettings.json` is git-ignored) |