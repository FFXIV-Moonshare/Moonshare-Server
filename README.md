# 🌙 Moonshare Server

**Moonshare Server** is the backend infrastructure for the Moonshare Dalamud plugin, enabling secure peer-to-peer communication and session management in **FINAL FANTASY XIV**.

This repository contains the core server-side components of Moonshare, including:

- `AuthServer`: Handles user authentication and session token generation
- `PlayerServer`: Manages real-time player connections and data exchange via WebSocket

> ⚠️ This project is in early development (ALPHA). Breaking changes are likely.

---

## 📁 Structure

/Moonshare-Server
├── AuthServer/ # WebSocket server for UID-based authentication
├── PlayerServer/ # WebSocket server for in-game data sessions
├── Shared/ # Shared types and session logic
├── Moonshare.Shared.csproj
└── README.md


---

## 🔒 AuthServer

The `AuthServer` component is responsible for:

- Handling initial connections from Dalamud clients
- Accepting user-provided UID and issuing session tokens
- Validating and storing active sessions in memory

### Features

- Lightweight WebSocket server using `WebSocketSharp`
- JSON-based communication
- Thread-safe session storage

---

## 🧩 PlayerServer

The `PlayerServer` handles:

- Persistent WebSocket sessions between authenticated users
- Real-time message exchange for file sharing or other plugin logic
- Live session lookups using tokens from `AuthServer`

### Features

- Bidirectional communication for game-integrated messaging
- Plug-and-play structure for extending protocol
- In-memory session resolution from `AuthServer`

---

## 🛠️ Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)  
- Open port(s) for WebSocket (default: 8080 / 9090)  
- Optional reverse proxy for production (e.g., Nginx or Caddy)

---

## 🚀 Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/FFXIV-Moonshare/Moonshare-Server.git
cd Moonshare-Server

2. Build and run both servers

cd AuthServer
dotnet run

cd ../PlayerServer
dotnet run
