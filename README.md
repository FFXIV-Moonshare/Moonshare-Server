🌙 Moonshare Server

Moonshare Server is the backend infrastructure for the Moonshare Dalamud plugin, enabling secure peer-to-peer communication and session management in FINAL FANTASY XIV.

This repository contains the core server-side components of Moonshare:

    AuthServer: Handles user authentication and session token generation

    PlayerServer: Manages real-time player connections and data exchange via WebSocket

    ApiGatewayServer: Provides a RESTful HTTP API for external clients (e.g., Discord bots) to query current server status, sessions, logs, and stats

    ⚠️ This project is in early development (ALPHA). Breaking changes are likely.

📁 Structure

/Moonshare-Server
├── ApiGatewayServer/    # HTTP API server for querying sessions, logs, and more
├── AuthServer/          # WebSocket server for UID-based authentication
├── PlayerServer/        # WebSocket server for in-game data sessions
├── Shared/              # Shared types and session logic
├── Moonshare.Shared.csproj
└── README.md

🔒 AuthServer

The AuthServer component is responsible for:

    Handling initial connections from Dalamud clients

    Accepting user-provided UIDs and issuing session tokens

    Validating and storing active sessions in memory

Features

    Lightweight WebSocket server using WebSocketSharp

    JSON-based communication

    Thread-safe session storage

🧩 PlayerServer

The PlayerServer handles:

    Persistent WebSocket sessions between authenticated users

    Real-time message exchange for file sharing or other plugin logic

    Live session lookups using tokens from the AuthServer

Features

    Bidirectional communication for game-integrated messaging

    Plug-and-play structure for extending the protocol

    In-memory session resolution from the AuthServer

🌐 ApiGatewayServer

The ApiGatewayServer provides a RESTful HTTP API that external clients (such as Discord bots) can use to retrieve the current state of the servers, including:

    Active sessions managed by the AuthServer

    Connected players managed by the PlayerServer

    Live logs and events

    Statistics and monitoring data

Features

    Simple HTTP endpoints (e.g., /sessions, /players, /logs)

    JSON responses for easy integration

    Real-time updates based on data from other servers

🛠️ Requirements

    .NET 8 SDK

    Open ports for WebSocket (default: 8080 / 9090) and HTTP connections (e.g., 5000)

    Optional reverse proxy for production (e.g., Nginx or Caddy)

🚀 Getting Started
1. Clone the repository

git clone https://github.com/FFXIV-Moonshare/Moonshare-Server.git
cd Moonshare-Server

2. Run the servers

cd ApiGatewayServer
dotnet run

cd ../AuthServer
dotnet run

cd ../PlayerServer
dotnet run
