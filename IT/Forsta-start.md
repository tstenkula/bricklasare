# Första starten på ny dator och ny läsare

1. Packa upp hela paketet och anslut OMNIKEY 5427 CK till USB utan någon bricka på läsaren.
2. Starta `Starta-Bricklasaren.cmd`. Välj passersystem och spara. Inget passersystem är förvalt.
3. Programmet ansluter och förbereder läsaren. Om anslutningen saknas visas **Anslut din läsare**. Försök ansluta igen eller välj **Hämta USB-drivrutin**.
4. Knappen öppnar tillverkarens officiella nedladdningssida. Läs krav och villkor, hämta rätt EEM-drivrutin och följ tillverkarens instruktioner. IT eller administratör kan behövas. Återvänd sedan till programmet och välj **Försök ansluta**. Inga drivrutinsfiler medföljer.
5. När läsaren är klar aktiveras **Läs bricka**. Ingen referensbricka eller läsarprofil behöver skapas.

Startguiden finns också via **Anslut läsare** i huvudfönstret och **Hjälp → Ny dator – anslut läsare**. Det går att välja **Senare** utan att förlora det sparade passersystemet. En redan fungerande drivrutin behöver inte installeras om.

Windows kontrollerar att anslutningen går genom läsarens identifierade USB-enhet och låser kommunikationen till den anslutningen. En adress på Wi-Fi, vanligt nätverk eller VPN räcker inte. Om läsaren inte kan identifieras entydigt stoppas anslutningen. Anslut en läsare åt gången och kontrollera USB-drivrutinen; programmet växlar inte till en annan nätverksväg.

## Standardformat och befintliga profiler

Programmet identifierar originalmodellen (boardType 06) och Gen2 (0A). Båda måste klara samma kontroller av inställningar, identitet, firmware och skydd mot tangentbordsinmatning innan läsning tillåts. Okända modeller avvisas.

Den inbyggda konventionen normaliserar MIFAREs råa PC/SC-UID genom bytevändning och behåller EM:s 40 PACS-bitar i mottagen ordning. RCO använder sedan sina vanliga två 16-bitars block av de sista fyra normaliserade bytena och visar 9 eller 10 siffror. Axema VAKA och Bewator behåller respektive befintlig EM-omvandling. Detta är ett fast standardformat, inte en uppmätt kalibrering av varje läsare. Ingen fil som påstår verifierad kalibrering skapas automatiskt.

En befintlig verifierad lokal profil eller en importerad profil för exakt samma läsare och firmware har företräde. Om en befintlig lokal profil är skadad eller gäller annan firmware stoppas läsningen tills IT importerat rätt profil eller gjort ett nytt frivilligt referensprov. Programmet växlar då inte tyst till standardformatet.

Ett exporterat paket innehåller bara en läsarprofil om en sådan faktiskt har verifierats. Standardpaket kan användas med en annan ny läsare. Inga referensbricknummer, skannade nummer, lokala utseendeval eller loggar exporteras.

## Avgränsningar och provning

Detta gäller läsning av UID och 40-bitars EM enligt programmets format. En annan RCO-konfiguration, till exempel linjär läsning eller blocklängd 7, kan ge ett annat nummer. Krypterade applikationsdata och slumpmässiga UID är inte samma sak som ett stabilt bricknummer och stöds inte som identifieringsmetod här. Att två taggar delar radiostandard garanterar inte att deras passersystem använder samma nummerformat.

Kontrollera drivrutinens stöd för datorns Windows-version och processor på tillverkarens sida. Administratörsrättigheter kan behövas en gång för drivrutinen. Avstängd EEM, ändrad läsaradress och lokala Windows-regler kan kräva IT-hjälp. Programmet ändrar inte Windows säkerhetspolicy eller återställer hela läsaren till fabriksinställningar.

Windows-tester simulerar ny läsare, normal skanning och återställning utan profil, båda modellernas identifiering samt skadade och äldre profiler. De kontrollerar också länkvalet utan att installera en drivrutin. Verklig USB-anslutning, drivrutinsinstallation och kortläsning måste fortfarande provas på fysisk hårdvara. Vid driftsättning: kontrollera ett resultat mot det avsedda passersystemet och prova omstart samt ur-/inkoppling. Det är en acceptanskontroll, inte en obligatorisk kalibrering i programmet.

## Tekniskt underlag

- [RCO: Blockläsning](https://support.rco.se/hc/sv/articles/27324908011666-RCO-Blockl%C3%A4sning) beskriver standardens blockberäkning och skillnaden mot andra läsarinställningar.
- [RCO: Mifare Serienummer (UID) Läsning](https://drop.mopp.se/f/RCO.MIFare.68af7fd.pdf), sidorna 2–4, visar byteordningen från vanligt UID till RCO-block.
- HID *OMNIKEY 5x27CK Software Developer Guide*, 5127-903 B.7, avsnitt 9.2.2, 9.4.1 och A.3.2 beskriver UID-kommandot, LF-svarets PACS-struktur och modellidentifieringen. Finns via [HID Developer Center](https://sdk.hidglobal.com/developer-center/omnikey-5x27ck-developer-center).
- [Microsoft: PnPUtil](https://learn.microsoft.com/windows-hardware/drivers/devtest/pnputil-command-syntax) beskriver installation av matchande drivrutinspaket.
