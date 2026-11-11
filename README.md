# Plumbline

Accessible escrow for freelance work. A freelancer uploads the finished asset, the client sees only a protected preview, pays through PayPal Sandbox, and gets 24 hours to review the full asset. If the client disputes, Claude Fable 5.2 audits the delivery against the original requirements and the API either releases the funds or refunds the PayPal payment.

```
backend/    .NET 8 Minimal API   (Program.cs, Services.cs, Models.cs)   http://localhost:5080
frontend/   Fable + Feliz + Elmish (src/App.fs, src/interop.js)         http://localhost:5173
```

## Prerequisites

- .NET 8 SDK
- Node.js 18 or newer
- A PayPal Sandbox REST app (developer.paypal.com > Apps & Credentials) and a sandbox *personal* account to pay with
- An Anthropic API key

## 1. Set environment variables

Set these in the terminal that will run the backend.

**Windows PowerShell**
```powershell
$env:PAYPAL_CLIENT_ID = "your-sandbox-client-id"
$env:PAYPAL_SECRET = "your-sandbox-secret"
$env:ANTHROPIC_API_KEY = "sk-ant-..."
```

**Windows cmd**
```bat
set PAYPAL_CLIENT_ID=your-sandbox-client-id
set PAYPAL_SECRET=your-sandbox-secret
set ANTHROPIC_API_KEY=sk-ant-...
```

**macOS / Linux**
```bash
export PAYPAL_CLIENT_ID="your-sandbox-client-id"
export PAYPAL_SECRET="your-sandbox-secret"
export ANTHROPIC_API_KEY="sk-ant-..."
```

## 2. Run the backend (terminal 1)

```bash
cd backend
dotnet run
```

The API listens on `http://localhost:5080`.

## 3. Run the frontend (terminal 2)

```bash
cd frontend
dotnet tool restore
npm install
npm start
```

Open `http://localhost:5173`. The dev server proxies `/api` to the backend.

If Fable reports a missing .NET runtime, allow it to run on .NET 8 and retry:
PowerShell `$env:DOTNET_ROLL_FORWARD = "Major"`, cmd `set DOTNET_ROLL_FORWARD=Major`, bash `export DOTNET_ROLL_FORWARD=Major`.

## What you get

- **Website**: the landing page at `http://localhost:5173` with the tagline, how it works, why it is fair and what it is built with.
- **Dashboard app** (press *Open the dashboard*): a sidebar with Overview, Projects, New project, Review and pay, Disputes, Activity, and Settings and status. Overview shows the money awaiting payment, held in escrow, released and refunded, plus the newest projects and activity. Projects has search and a status filter.

## Demo script

All three steps are in the dashboard app's sidebar.

1. **New project**: enter the client's requirements, a price, and the asset (pasted text/code or a file). Copy the project ID.
2. **Review and pay**: load the project. You see Claude's bulleted summary and a masked or blurred preview. Pay with a sandbox personal account. The status turns to `ESCROW_HELD` and the full asset appears.
3. **Disputes**: enter a complaint. Claude returns `{ match_score, verdict, reason }`. `RELEASE` sets the project to `COMPLETED`; `REFUND` calls PayPal's refund API and sets it to `REFUNDED`.

The page opens on a deep navy theme; the header switch changes it to PayPal blue on white, and the choice is remembered in the browser. The two typefaces load from Google Fonts and fall back to system fonts when offline.

Three animated components come from [React Bits](https://reactbits.dev) and live in `frontend/src/reactbits/` with their licence (MIT plus Commons Clause): `BlurText` reveals the tagline, `CountUp` runs the match score up to its value, and `DecryptedText` unscrambles the "Protected preview" watermark. They need the `motion` package, so run `npm install` again after pulling this version. All three stay still when the system asks for reduced motion.

Payment confirmations and verdicts are spoken aloud (toggle in the header), announced through an ARIA live region, and shown as green / red / amber banners that each start with a word (Done, Problem, Working), so nothing depends on colour or sound.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/projects/upload` | multipart form: `title`, `requirementsText`, `priceUsd`, `assetText` or `assetFile`, optional `previewImage` |
| GET | `/api/projects/{id}` | preview if unpaid, full asset if `ESCROW_HELD` or `COMPLETED`, plus the Claude summary (`effort: low`) |
| POST | `/api/payments/create-order/{projectId}` | creates a PayPal `CAPTURE` order |
| POST | `/api/payments/capture-order/{projectId}/{orderId}` | captures payment, sets `ESCROW_HELD`, starts the 24-hour window |
| POST | `/api/disputes/resolve/{projectId}` | JSON `{ "complaintReason": "..." }`; runs the arbitrator (`effort: high`) |
| GET | `/api/dashboard` | totals by escrow state, all projects, the activity log and integration status (no asset content) |
| GET | `/api/config` | PayPal client ID and which keys are configured |

## Recreating the projects from scratch

These are the commands that produce the same skeleton; the repository already contains the result.

```bash
# backend
dotnet new web -n Plumbline.Api -o backend -f net8.0

# frontend
mkdir frontend && cd frontend
dotnet new tool-manifest
dotnet tool install fable --version 4.24.0
dotnet new classlib -lang "F#" -n App -o src -f net8.0
dotnet add src package Fable.Core --version 4.3.0
dotnet add src package Fable.Elmish.React --version 4.0.0
dotnet add src package Feliz --version 2.9.0
npm init -y
npm install react@18 react-dom@18
npm install --save-dev vite@5
```

## How it behaves, and its limits

- **Storage** is an in-memory `ConcurrentDictionary`; restarting the API clears every project.
- **Text previews** show the first quarter of the asset (400 characters at most) and mask the rest on the server. **Image previews** are blurred and watermarked in the freelancer's browser at upload; the server stores them next to the original and falls back to a locked placeholder if none is sent.
- **Escrow** is a platform-side hold: the payment is captured into the sandbox merchant account that owns the client ID. `COMPLETED` marks the funds as released; paying a separate freelancer account would need PayPal Payouts, which is not included.
- **Review window**: if 24 hours pass without a dispute, the next read of the project marks it `COMPLETED`.
- **Failures never move money**: if Claude returns no valid verdict, or PayPal rejects the refund, the project stays `ESCROW_HELD` and the dispute can be retried.
- **Claude calls** send `model: claude-fable-5-2` with `output_config.effort` and no `temperature`, `top_p`, `top_k` or `tool_choice`. Requirements, asset and complaint are passed as tagged evidence, and the arbitrator is told to ignore instructions inside them.
- **No login**: the dashboard lists every project and anyone can open them. Add authentication before using this beyond a demo.
