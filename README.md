# Clippy.io — Enterprise URL Shortener, QR Engine & Real-Time Telemetry Platform

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor Interactive](https://img.shields.io/badge/Blazor-Interactive_Server_%26_WASM-512BD4?logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![EF Core 10](https://img.shields.io/badge/EF_Core-10.0_SQL_Server-CC2927?logo=microsoftsqlserver&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![Docker Ready](https://img.shields.io/badge/Docker-Multi--Stage_Ready-2496ED?logo=docker&logoColor=white)](./Dockerfile)
[![Architecture](https://img.shields.io/badge/Architecture-High_Concurrency_Enterprise-276E1F)](#system-architecture)

**Clippy.io** is a high-throughput, low-latency URL shortening, vector QR code generation, and GDPR-compliant click analytics platform built with **.NET 10**, **ASP.NET Core**, **Blazor**, and **Entity Framework Core 10**.

Designed to handle viral traffic spikes without database bottlenecks, Clippy combines **64-bit Snowflake-style ID generation**, **keyed `SemaphoreSlim` cache protection**, **non-blocking telemetry ingestion**, and **zero-trust identity** with passkeys, TOTP, HMAC OTP, and OAuth2/OIDC support.

---

## System Architecture

```mermaid
graph TB
    subgraph ClientTier["Client & Edge Tier"]
        Browser["Browser / Blazor UI<br/>(Interactive Server & WASM)"]
        Mobile["QR Scanner / External Visitor"]
        API["REST API Consumers"]
    end

    subgraph Pipeline["ASP.NET Core 10 Middleware Pipeline"]
        RL["IP Rate Limiter<br/>(Fixed / Sliding / TokenBucket)"]
        Auth["Authentication & Authorization<br/>(Cookies, Passkeys, OAuth2, OIDC)"]
    end

    subgraph Core["Clippy Core Engine"]
        RedirectEP["Redirect Endpoint<br/>GET /{shortCode}"]
        UrlEP["URL Shortener Service<br/>& Minimal APIs"]
        AuthEP["Identity & HMAC OTP<br/>Endpoints"]
        Scanner["UrlSafetyScanner<br/>(SSRF & Threat Heuristics)"]
        Snowflake["SnowFlakeBase62CodeGenerator<br/>(64-Bit Lock + stackalloc Base62)"]
        QR["QrCodeService<br/>(Vector SVG Data URI)"]
        L1Cache["UrlResolutionCache<br/>(IMemoryCache + Keyed SemaphoreSlim)"]
        ChannelQueue["ClickTelemetryQueue<br/>(BoundedChannel 50,000)"]
    end

    subgraph Workers["Background Hosted Services"]
        AnalyticsWorker["ClickAnalyticsBackgroundWorker<br/>(Batch Drain & GDPR SHA-256 Anonymizer)"]
        JanitorWorker["ExpiredUrlCleanupWorker<br/>(PeriodicTimer 15m Sweep)"]
    end

    subgraph DataTier["Persistence Tier (EF Core 10 / SQL Server)"]
        SQL[("SQL Server Database<br/>ShortUrls | UrlClickLogs | OtpRecord | AspNetUsers")]
    end

    Browser --> RL
    Mobile --> RL
    API --> RL
    RL --> Auth
    Auth --> RedirectEP
    Auth --> UrlEP
    Auth --> AuthEP

    UrlEP --> Scanner
    UrlEP --> Snowflake
    UrlEP --> QR
    UrlEP --> L1Cache
    UrlEP --> SQL

    RedirectEP --> L1Cache
    L1Cache -->|Cache miss - one thread only| SQL
    RedirectEP -->|Non-blocking TryWrite| ChannelQueue

    ChannelQueue -->|ReadAllAsync batch| AnalyticsWorker
    AnalyticsWorker -->|AddRangeAsync and ExecuteUpdateAsync| SQL
    JanitorWorker -->|ExecuteUpdateAsync and ExecuteDeleteAsync| SQL
```

---

## Key Features

- Collision-free short-code generation using a custom 64-bit Snowflake-style identifier
- Keyed `SemaphoreSlim` protection against cache stampedes during viral traffic spikes
- Non-blocking click telemetry with GDPR-safe anonymization
- SSRF and phishing prevention for submitted URLs
- Multi-algorithm IP rate limiting
- Zero-trust identity with passkeys, TOTP, OTP, and OAuth2/OIDC
- Self-healing janitor worker for expired links and stale audit records

---

## Solution Structure

```text
Clippy/
├── Clippy/                       # ASP.NET Core 10 server and Blazor host
│   ├── Components/              # UI and account pages
│   ├── Data/                    # EF Core context, migrations, and schema
│   ├── EndPoints/               # Auth, redirect, and URL API endpoints
│   ├── Entities/                # Domain entities
│   ├── Extensions/              # Auth, rate limiting, and health probes
│   ├── Services/                # Security, caching, analytics, QR, and shortening
│   ├── Tests/                   # Concurrency and security tests
│   └── wwwroot/                 # Static assets and CSS
├── Clippy.Client/               # Blazor client and shared DTOs
├── Dockerfile                   # Multi-stage .NET 10 container build
├── docker-compose.yml           # Full-stack orchestration
├── Clippy.slnx                 # Solution file
├── NuGet.Config                 # NuGet configuration
├── README.md                   # Project overview and setup
└── .gitignore                  # Git ignore rules
```

---

## Getting Started

### Option A: Docker Compose

```bash
docker compose up --build -d
```

- Web application: `http://clippy.runasp.net`
- Health probe: `http://clippy.runasp.net/healthz`

### Option B: Local .NET 10 CLI

1. Ensure **.NET 10 SDK** and **SQL Server LocalDB** (or SQL Server Express) are installed.
2. Apply EF Core migrations:

   ```bash
   dotnet ef database update --project Clippy/Clippy.csproj
   ```

3. Run the server:

   ```bash
   dotnet run --project Clippy/Clippy.csproj
   ```

4. Navigate to `https://clippy.runasp.net`.

---

## Verification

Clippy includes an engineering test suite under `Clippy/Tests/` for:

- Snowflake concurrency validation
- Cache stampede protection
- URL safety and SSRF checks
- OTP brute-force protections

This README issue is fixed by removing the Mermaid edge labels that GitHub rejects during parsing.
