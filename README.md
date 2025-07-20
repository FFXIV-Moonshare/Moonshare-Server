🌙 Moonshare Server

Moonshare Server ist die Backend-Infrastruktur des Moonshare Dalamud-Plugins und ermöglicht sichere Peer-to-Peer-Kommunikation sowie Sitzungsverwaltung in FINAL FANTASY XIV.

Dieses Repository enthält die zentralen Server-Komponenten von Moonshare:

    AuthServer: Verarbeitet die Benutzer-Authentifizierung und erzeugt Session-Tokens

    PlayerServer: Verwaltet Echtzeit-Spielerverbindungen und Datenaustausch über WebSocket

    ApiGatewayServer: Bietet eine HTTP-API, über die z. B. Discord-Bots den aktuellen Serverstatus, Sessions und Logs abfragen können

    ⚠️ Dieses Projekt befindet sich in einer frühen Entwicklungsphase (ALPHA). Breaking Changes sind zu erwarten.

📁 Projektstruktur

/Moonshare-Server
├── ApiGatewayServer/   # HTTP-API Server für Abfragen von Sessions, Logs etc.
├── AuthServer/         # WebSocket-Server für UID-basierte Authentifizierung
├── PlayerServer/       # WebSocket-Server für Ingame-Daten-Sessions
├── Shared/             # Gemeinsame Typen und Sitzungslogik
├── Moonshare.Shared.csproj
└── README.md

🔒 AuthServer

Der AuthServer ist zuständig für:

    Verarbeitung der ersten Verbindungen von Dalamud-Clients

    Annahme der vom Nutzer bereitgestellten UID und Vergabe von Session-Tokens

    Validierung und Verwaltung aktiver Sessions im Speicher

Features

    Leichter WebSocket-Server basierend auf WebSocketSharp

    JSON-basierte Kommunikation

    Thread-sichere Speicherung von Sessions

🧩 PlayerServer

Der PlayerServer übernimmt:

    Verwaltung persistenter WebSocket-Sessions authentifizierter Nutzer

    Echtzeit-Kommunikation für Dateiaustausch oder andere Plugin-Funktionen

    Live-Sitzungsabfragen mit Tokens vom AuthServer

Features

    Bidirektionale Kommunikation für spielintegrierte Nachrichten

    Erweiterbare Struktur für zusätzliche Protokollfunktionen

    In-Memory Sitzungsverwaltung basierend auf AuthServer

🌐 ApiGatewayServer

Der ApiGatewayServer stellt eine RESTful HTTP-API bereit, über die externe Clients (z.B. Discord-Bots) den aktuellen Zustand der Server abrufen können:

    Aktive Sessions (vom AuthServer verwaltet)

    Verbundene Spieler (vom PlayerServer)

    Laufende Logs und Events

    Statistiken und Monitoring-Daten

Features

    Einfache HTTP-Endpunkte (z.B. /sessions, /players, /logs)

    JSON-Antworten für einfache Integration

    Aktualisierung in Echtzeit basierend auf den Daten der anderen Server

🛠️ Voraussetzungen

    .NET 8 SDK

    Offene Ports für WebSocket- (Standard: 8080 / 9090) und HTTP-Verbindungen (z.B. 5000)

    Optional: Reverse Proxy für den Produktivbetrieb (z.B. Nginx oder Caddy)

🚀 Erste Schritte
1. Repository klonen

git clone https://github.com/FFXIV-Moonshare/Moonshare-Server.git
cd Moonshare-Server

2. Server starten

cd ApiGatewayServer
dotnet run

cd ../AuthServer
dotnet run

cd ../PlayerServer
dotnet run
