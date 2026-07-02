# Split hosting: Pi orchestrator + PC compute (wake → run → sleep)

A two-node setup where a always-on **Raspberry Pi** runs the durable orchestrator (scheduling,
fetching, folding, delivery — all light) and a **GPU PC** runs the expensive part (the LLM). The Pi
wakes the PC, waits for its LLM server, runs the digest against it, and lets the PC fall back asleep on
its own idle timer. It's a small distributed system — the durable task hub is the coordination plane,
the Pi is the control node, the PC is a compute node behind an endpoint.

This is **not** Durable Functions "activity placement" (that doesn't exist — every worker runs every
function). Instead the orchestrator lives entirely on the Pi and the heavy activity simply *calls* the
PC's LLM over the LAN. Same result, robust design.

```
Pi (durable host, always on)                      PC (GPU, asleep until needed)
  MorningDigestStarter (timer)
    └─ DigestOrchestrator
         ├─ WakePcActivity      ── magic packet ──▶  NIC wakes the box
         ├─ poll PcReadyActivity ── GET /v1/models ─▶  LM Studio server (model loads on first call)
         ├─ gather → summarize  ── /v1/chat/... ────▶  LM Studio inference
         └─ deliver
                                                       (idle-sleep timer sleeps it again, unless a
                                                        user is now active)
```

## PC side

1. **Enable Wake-on-LAN**: in BIOS/UEFI (often "Power On By PCI-E / WoL") and in the OS NIC driver
   (Windows: Device Manager → NIC → Power Management → *Allow this device to wake the computer* + *Only
   allow a magic packet*). Use a **wired** connection — WoL over Wi-Fi is unreliable. Note the NIC's MAC.
2. **LM Studio**: start the server, enable *Serve on Local Network* so it binds the LAN IP (not just
   localhost), port `1234`. Note the PC's LAN IP.
3. **Sleep timer** = the OS power plan. Set *Sleep after N minutes* to a value comfortably longer than a
   digest run (e.g. 30 min). This is the whole "sleep" mechanism:
   - If a user sits down, their input resets the idle timer → the PC stays awake. Exactly the behavior
     you want; no forced remote suspend.
   - If it stays idle after the digest, it sleeps on its own.
   - ⚠️ The idle timer is driven by **user input**, not CPU/network. A long inference run with nobody at
     the keyboard can trip the sleep timer mid-digest. Keep the idle timeout > your longest run, or run a
     small keep-awake during inference (e.g. `powercfg /requestsoverride`, or `caffeine`). Most digests
     finish in minutes, so a 30-min timeout is usually enough.

## Pi side

- 64-bit OS (Raspberry Pi OS 64-bit / Ubuntu). .NET 8 runs on ARM64; a Pi 4/5 with 4–8 GB is plenty for
  the orchestrator + Azurite (the durable task hub's storage).
- Point the summarizer and the readiness check at the **PC's LAN IP**, not localhost:

```jsonc
// app.json
{
  "summarizers": {
    "openai": { "endpoint": "http://192.168.1.50:1234", "model": "your-model" }
  },
  "remoteCompute": {
    "enabled": true,
    "macAddress": "AA:BB:CC:DD:EE:FF",   // the PC NIC's MAC
    "broadcastAddress": "192.168.1.255", // your subnet's broadcast (Pi & PC same LAN)
    "wolPort": 9,
    "readinessUrl": "http://192.168.1.50:1234/v1/models",
    "maxWaitSeconds": 180,
    "pollSeconds": 5
  }
}
```

- **Playwright note**: the headed-Firefox trick (better against bot detection) needs a display. On a
  headless Pi you'd run it under `Xvfb`, or accept headless there. If scraping quality drops, keep the
  fetching on the PC too, or run the Pi with a virtual framebuffer.

## Behavior

- `remoteCompute` absent or `enabled:false` → single-machine mode, no wake/gate (unchanged).
- The wait loop uses **durable timers**, so if the Pi reboots while waiting for the PC, the orchestration
  resumes from where it left off instead of restarting the morning.
- If the PC never wakes within `maxWaitSeconds`, the run proceeds anyway: LLM-dependent sections render as
  unavailable (failure-isolated), local sections (weather, calendar) still deliver.
- Only the **durable** host does wake/gate — the plain timer host stays single-machine.
