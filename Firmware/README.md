# Inbyggd uppdatering av läsarens firmware – utvecklingsutkast

Detta är en separat grund för den begärda firmwarefunktionen, inte en färdig funktion i appen. Den ingår inte i Windows-paketet eller Mac-appen. Version 3.6.4 uppdaterar endast själva appen.

## Det som går att verifiera utan läsare

`Lab/Firmware.cs` och `Lab/Firmware.swift` innehåller samma begränsade flöde för huvudfirmware: kontroll av en i förväg granskad fil, exakt hårdvarumodell och tillåtna utgångsversioner, start, överföring i högst 255 byte per kommando, avslutning, återanslutning och kontroll av resultatet. Inga kommandon hämtas ur en fil. WBO, CFG, BLE, SDR och SE/SAM-uppdateringar omfattas inte.

En beständig säkerhetskopia och journal måste vara klara före första skrivkommandot. Förloppet når 100 procent först när samma läsare har rätt firmware, godkänt uppdateringsresultat och verifierat skydd mot tangentbordsinmatning. Ett avbrott lämnar journalen kvar. Skrivkommandon återförsöks aldrig automatiskt. Avbryt tillåts före överföringen; därefter måste appen hållas öppen tills resultatet är känt.

Tester använder endast en låtsasläsare och syntetiska filer. De är inte ett återhämtningstest av fysisk hårdvara och kan inte godkänna någon firmware för distribution. Ingen anslutning till USB, läsarens webbserver eller internet görs av testerna.

## Spärrar före inkoppling i appen

Ingen firmware är godkänd. Det finns ingen produktionsadapter som kan skriva till en läsare. Följande krävs innan utkastet kan kopplas till IT-inställningarna:

1. Ett officiellt HID-paket för exakt generation, med bekräftad övergång från den installerade huvudfirmwaren. Firmwareversion ensam får inte användas för att gissa generation.
2. Verifierad officiell hämtningsadress, filformat, längd och SHA-256 samt tillåtelse att automatisera hämtningen. Ingen HID-firmware ska kopieras till repot utan tillstånd. En signerad, separat firmwarekatalog ska knyta dessa uppgifter till modellen; appens uppdateringsfeed får inte återanvändas som firmwarekatalog.
3. Produktionsadaptrar för Windows EEM och Mac CCID, bundna till en enda fysisk läsare med exklusiv åtkomst och begränsade väntetider. Bekräfta att kommandoflödet gäller respektive paket och transport. HTTP/APDU-kommandon som skriver får inte automatiskt återförsökas efter osäkert svar.
4. Export och återläsningskontroll av en fullständig krypterad läsarkonfiguration, privat lokal lagring och en beständig journal före varje riskfylld fas. Testa att fel vid lagring stoppar överföringen. En backup av några MIB-värden räcker inte.
5. Verifierad återhämtningsmetod från HID och praktiskt avbrotts-/återhämtningstest på en testläsare för den exakta uppdateringsvägen. Journal och konfigurationsbackup kan inte återställa trasig huvudfirmware.
6. Ny kontroll av Custom Report efter omstart. Gamla kalibrerings- och USB-/ljudbackuper får inte automatiskt märkas om till den nya firmwaren. Verifiera nummersystem, ljud och återanslutning efter uppdateringen.
7. En gemensam spärr mot skanning, appuppdatering, stängning och viloläge under firmwareöverföringen, samt ett användarflöde med modell, från-/tillversion, förloppsindikering och tydlig slutkontroll. Ingen automatisk firmwareuppdatering vid appstart.

## Källor och vad de faktiskt styrker

- HID:s [aktuella nedladdningssida för OMNIKEY 5427CK](https://www.hidglobal.com/drivers?f%5B0%5D=product%3AHID%20OMNIKEY%205427CK) och [Workbench 2.7](https://www.hidglobal.com/drivers/49398), kontrollerade 2026-11-05. Den offentliga produktlistan gav drivrutiner och verktyg, ingen verifierad firmwarefil för den rapporterade läsaren.
- Den officiella Workbench-nedladdningen innehåller *PLT-04346 A.4, mars 2026*, avsnitt 1.1 och 3.2. Där gäller firmwareuppladdning bland annat 5427 CK **Gen2**. Firmwarepaket hänvisas till HID Developer Center. Guiden varnar för skador om ström eller anslutning bryts under uppdatering och hänvisar till HID-support om återställning inte hjälper. Detta är ingen garanti om automatisk återhämtning.
- HID *5127-903 B.7*, avsnitt 4.5.3–4.5.5, beskriver huvudfirmware över EEM och standardläsarens APDU-flöde. Lägre funktioner för SE/SAM, BLE och Mini använder andra paket/kommandon. Konfigurationens dubbla flashlagring får inte förväxlas med återställningsbar huvudfirmware.

Tillverkarfiler, hela manualer, kundbilder, serienummer och konfigurationsbackuper finns inte i detta repo. Ingen betald tjänst, HID-licens eller nytt utvecklarkonto har beställts.
