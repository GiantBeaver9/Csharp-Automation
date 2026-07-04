# Outlook email digest (Microsoft Graph)

Reads your **personal** Outlook mailbox (outlook.com / hotmail / live) over Microsoft Graph and
folds the last _N_ hours of mail into one digest section, summarized like any other section. This is
the reusable read piece; the same `Mail.ReadWrite` credential will back the future spam auto-deleter.

Basic-auth IMAP with an app password no longer works for Outlook (Microsoft retired it), so this uses
**delegated device-code auth**: you sign in interactively once, then every scheduled run refreshes
silently from a cached refresh token. No client secret is involved.

## One-time Azure app registration (free, ~3 minutes)

1. Azure Portal → **App registrations** → **New registration**.
2. **Supported account types:** choose an option that includes *personal Microsoft accounts*
   (e.g. "Personal Microsoft accounts only", or "…and personal Microsoft accounts").
3. Leave Redirect URI blank. Register.
4. **Authentication** → enable **Allow public client flows = Yes** (required for device code).
5. **API permissions** → Add → **Microsoft Graph** → **Delegated permissions** → add **Mail.ReadWrite**.
   (Personal accounts consent at sign-in — no admin consent button needed. `offline_access` is added
   automatically so refresh tokens are issued.)
6. Copy the **Application (client) ID** — it goes in `settings.clientId` below. It is not a secret.

## app.json section

Drop this into a digest's `sections` array (bump `order` so it's unique):

```jsonc
{
  "type": "outlook",
  "heading": "Inbox — last 24h",
  "order": 24,
  "summarizer": "openai",           // your LM Studio summarizer
  "timeoutSeconds": 300,
  "prompt": "Summarize the last day of email. Group by theme, call out anything that needs a reply or looks time-sensitive, and flag likely spam/junk. Be terse.",
  "settings": {
    "clientId": "<APPLICATION (CLIENT) ID>",
    "tenantId": "consumers",        // personal accounts
    "folder": "inbox",              // well-known name or a folder id; e.g. "junkemail"
    "lookbackHours": 24,
    "unreadOnly": false,
    "maxMessages": 50
  }
}
```

## First run

On the first run the host logs a device-code prompt like:

```
To sign in, use a web browser to open the page https://microsoft.com/devicelogin and enter the code XXXX-XXXX to authenticate.
```

Open that page, enter the code, sign in, and consent. The account identity is saved to
`.outlook-auth.json` and the refresh token to the OS token cache (`DailySummary.Outlook`), so **every
run after that is silent** — no prompt. Delete `.outlook-auth.json` to force a re-login (e.g. different
account, or the refresh token expired after long inactivity).

## Notes

- The gatherer sends `Prefer: outlook.body-content-type="text"`, so Graph returns plain-text bodies —
  no HTML stripping.
- Failure is isolated: if auth or Graph fails, only this section renders as unavailable; the rest of
  the digest is unaffected.
- `.outlook-auth.json` and the token cache hold account tokens — they are git-ignored; never commit them.
