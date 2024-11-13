# Dynamics Connection Manager v2.0

**Major improvements** have been implemented, starting at the core of the project: authentication. The project has been upgraded from Framework 4.8 to **.NET 8** and now uses the modern **Microsoft PowerPlatform Dataverse** instead of the deprecated Microsoft CRM SDK (which lacked MFA support).

## Authentication

**Authentication into MS Dynamics** is the foundation of the project. The first major change was implementing a thread-safe Singleton pattern:
- Ensures only one instance manages all Dynamics connections
- Prevents multiple authentication attempts across different threads

## Token-based Authentication

The latest PowerPlatform Dataverse library authenticates users through a valid access token. This token is stored locally on the machine:
- Persists across sessions for automatic authentication
- Managed and automatically refreshed by the Dynamics SDK
- Has its own expiration lifecycle
- Can auto-refresh when conditions permit

## Credential-based Authentication

If token authentication fails (either due to an invalid token or first-time usage), users must input their credentials:
- Credentials are securely stored using **Windows Credential Manager**
- Encrypted using Windows DPAPI
- Used only to obtain an authentication token
- Can be modified/removed through Control Panel > Credential Manager > Windows Credentials > "DynamicsConnection"

## Multi-Factor Authentication (MFA)

**Enhanced security** through mandatory MFA support:
- Interactive MFA prompts in the browser work alongside credentials/access tokens
- Cached MFA approval persists during active sessions

## Connection Management

### Throttling
- Limits reconnection attempts
- Reduces server load
- Implements connection handling best practices

### Verification (WhoAmIRequest)
- Performs lightweight operations
- Confirms service availability
- Validates user context and returns ID

## First-time Setup

1. User enters credentials when launching the application
2. Upon successful connection, credentials are stored in Windows Credentials
3. Connection token is acquired
4. MFA process triggers, opening a browser window for authentication
5. Main menu loads after successful authentication

### Subsequent Usage

- While the token remains valid, users only need to confirm authentication via browser prompt
- If token expires:
  1. System attempts to acquire new token using stored credentials
  2. User confirms login in web browser
  3. If credentials are invalid, system prompts for new credentials
