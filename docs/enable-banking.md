# Připojení banky přes Enable Banking (Open Banking)

Flowlio umí kromě importu PDF výpisů a ručního zadávání i **automaticky stahovat transakce
přímo z banky** přes Open Banking (PSD2). Připojení zprostředkovává agregátor
[Enable Banking](https://enablebanking.com), který má potřebnou AISP licenci a pokrývá
2500+ bank ve 29 evropských zemích (včetně českých).

## Jak to funguje (a proč „vlastní přístupy")

Enable Banking nabízí **bezplatný „restricted" režim**, ve kterém aplikace vidí **jen účty,
které si její vlastník předem nalinkoval** v Enable Banking portálu. Proto ve Flowliu platí
model **„každý uživatel svoje přístupy" (BYO)**:

- Každý uživatel si zaregistruje **vlastní Enable Banking aplikaci** (zdarma) a nalinkuje si
  **svoje** bankovní účty.
- Application ID + privátní klíč vloží do Flowlia. Klíč se ukládá **šifrovaně** (ASP.NET Data
  Protection) a nikdy se nevrací do prohlížeče ani neloguje.
- Flowlio tak čte jen účty daného uživatele a samo **není placená AISP**.

> Čtení transakcí (AIS) je v restricted režimu **zdarma**. Platí se až produkční režim
> (kdyby si cizí uživatelé měli připojovat své banky ve velkém). Pro osobní/rodinné použití
> stačí free režim.

## Krok 1 – Vytvoř si účet u Enable Banking

1. Jdi na [enablebanking.com](https://enablebanking.com) a klikni na **Get Started**.
2. Vyplň registraci a počkej na potvrzovací e-mail (může chvíli trvat).
3. Po potvrzení se přihlas do **Enable Banking Control Panel**.

## Krok 2 – Vytvoř aplikaci

V Control Panelu otevři **API Applications → Add a new application**:

| Pole | Hodnota |
|------|---------|
| **Environment** | `Production` (pro reálná data; `Sandbox` jen pro testování) |
| **Private key generation** | nech výchozí „Generate in the browser… and export private key" |
| **Application Name** | např. `Flowlio import` |
| **Allowed Redirect URLs** | **callback URL Flowlia** (viz níže), např. `https://localhost:5443/bank-connections/callback` |
| **Privacy Policy / Terms URL** | v restricted režimu se nevaliduje – stačí URL Flowlia |
| **Email for data protection** | tvůj e-mail |

Po odeslání:

- Prohlížeč **stáhne PEM soubor s privátním klíčem** – ulož ho bezpečně.
- V Control Panelu si poznač **Application ID** (Client ID).

> **Callback URL** je pro celou instanci Flowlia stejná a najdeš ji ve Flowliu na stránce
> přístupů k bance (pole „Callback URL"). Odpovídá `EnableBanking:RedirectUrl` v konfiguraci
> serveru (výchozí `https://localhost:5443/bank-connections/callback`).

## Krok 3 – Nalinkuj si účty (nutné pro free režim)

V Control Panelu u své aplikace klikni na **Link accounts**, přihlas se do své banky a
autorizuj přístup ke svým účtům.

> **Tento krok nevynechávej.** V restricted režimu Enable Banking vrátí **jen předem
> nalinkované účty**. Bez něj Flowlio při připojení nedostane žádný účet.

## Krok 4 – Vlož přístupy do Flowlia

Ve Flowliu (sekce připojení banky) vlož:

- **Application ID** z kroku 2,
- obsah **PEM souboru** (celý, včetně řádků `-----BEGIN PRIVATE KEY-----` a `-----END…`).

Flowlio klíč ověří a uloží zašifrovaně. API: `PUT /api/bank-connections/credentials`.

## Krok 5 – Připoj banku a synchronizuj

1. Vyber zemi a banku ze seznamu (zobrazí se jen banky dostupné pro danou zemi).
2. Flowlio tě přesměruje do banky – projdeš **dvěma kroky autorizace** (souhlas Enable
   Banking + přihlášení do banky / SCA).
3. Po návratu na `…/bank-connections/callback` se připojení dokončí a naváže na tvůj
   Flowlio účet (podle IBANu, jinak na první dostupný).
4. Transakce se stáhnou a projdou stejnou pipeline jako import výpisu (deduplikace,
   automatická kategorizace podle pravidel, notifikace).

Další stahování běží **automaticky na pozadí** (přibližně každých 6 hodin) a dá se spustit
i ručně (`POST /api/bank-connections/{id}/sync`).

## Obnova souhlasu (~90 dní)

PSD2 souhlas má omezenou platnost (typicky ~90 dní). Po vypršení se připojení označí jako
**Expired** a je potřeba projít autorizaci znovu (Krok 5). Limit je daný regulací, ne
Flowliem.

## Časté problémy

- **Po autorizaci se nevrátil žádný účet** → nejspíš jsi nenalinkoval účty v EB portálu
  (Krok 3). V restricted režimu jsou dostupné jen předautorizované účty.
- **Neplatný klíč** → zkontroluj, že jsi vložil celý obsah PEM souboru včetně `BEGIN`/`END`
  řádků a že Application ID odpovídá portálu.
- **Autorizace selhává** → produkční režim vyžaduje HTTPS; ověř, že callback URL ve Flowliu
  i v EB portálu jsou shodné.

## Reference

- Enable Banking dokumentace: <https://enablebanking.com/docs/>
- Inspirace tokem: návod Firefly III Data Importer pro Enable Banking.
