# Läsarens pip och appens ljud

De två högtalarknapparna **Läsare** och **App** i huvudfönstret har separata funktioner. Överstruken symbol betyder av. Håll pekaren över en knapp för aktuell status och vad ett klick gör.

**Läsare** ändrar pipet i OMNIKEY. Datorns volym påverkar inte detta ljud. Inställningen sparas i läsaren och läses tillbaka vid anslutning. Appen sparar det ursprungliga pipmönstret lokalt för rätt läsare och firmware innan den tystar det. Lysdiodernas färger och tider bevaras. Vid vanlig frånkoppling behövs ingen återställning av ljudvalet.

**App** slår på eller av datorljuden för att lägga på brickan, lyfta bort den och avsluta läsningen. Valet sparas för det aktuella användarkontot. Dessa ljud spelas via datorn och följer datorns volym. Inställningen har flyttats från Arbetsflöde till huvudfönstret.

Läsarknappen är inaktiv när läsaren inte är ansluten, när en annan åtgärd pågår eller när ljudläget inte kan verifieras. Ett okänt läge visas inte som en bekräftad avstängning. Appknappen kan användas även utan läsare.

## Läsarvarianter och återställning

Styrningen följer HID 5127-903 B.7, A.5.18 och B.2.6. Den ändrar summerbiten i de tre offentliga indikatorsekvenserna i den dokumenterade tabellen med 130 byte. Interna sekvenser lämnas orörda; firmware kan ha ytterligare intern felindikering. Okända format avvisas utan gissade kommandon. Fysisk kontroll av pipet på den aktuella läsaren återstår.

Om läsaren redan är tyst och saknar lokalt sparat pipmönster kan appen aktivera ett pip i den befintliga kortsekvensen, förutsatt att den har en giltig avslutande fas. Om det skulle kräva ändrade ljus- eller tidsinställningar avvisas åtgärden och IT behöver konfigurera ett lämpligt mönster i läsarens webbgränssnitt.

En avbruten ändring återställs till sitt föregående läge innan appen visar framgång. Vid frånkoppling behålls en återställningspost i `lasarljud-<läsar-id>.json` tills samma läsare och firmware kan kontrolleras igen. Filerna ligger i appens lokala datamapp och följer inte med i Git eller installationspaket.

## Symboler

Knapparna använder Lucides färdiga **Volume 2** och **Volume Off**, med samma bildyta för båda lägena och plattformarna. Original, källversion och licens finns i `Assets/Lucide/`. Symbolerna får användas kostnadsfritt även kommersiellt enligt den medföljande ISC-licensen.

Vid första anslutning och återanslutning försöker appen läsa läsarens faktiska ljudläge. Om det inte går visas en neutral väntesymbol i stället för ett gissat på/av-läge, och orsaken sparas i felrapporten. Appens egen ljudinställning fungerar fortfarande separat.

På en verklig läsare med huvudfirmware **04.02.0100** har kommandot för ljudtabellen svarat `noSuchName` (MIB-status 2). Den läsarens webbgränssnitt visar det äldre upplägget USB Ready, Card Access, No USB och Keyboard Wedge. Den nuvarande ljudtabellsmetoden är därför inte verifierad eller tillgänglig där. Appen ändrar inte sekvenser när de inte kan läsas och säkerhetskopieras. Nyare kompatibla tabeller provas med simulerade svar; fysisk av/på-verifiering återstår.

## Gen1: underlag för kompatibilitetsarbetet

Artikelnummer R54270001 hör till Gen1. Den rapporterade revision E med firmware 04.02.0100 har det äldre webbgränssnittet med fyra händelser. Att ljudtabellens MIB saknas betyder inte att läsaren saknar ljudinställningar. Den exakta säkra läs-/skrivvägen för den varianten återstår att verifiera; inga gissade Gen1-kommandon har införts.

Det separata Windows-verktyget i `tools/reader-ui-inspection/` hämtar startsidan och uttryckligen angivna script-/frame-/iframe-resurser från läsarens USB-webbgränssnitt. Det kör inte hämtad kod, skickar inga APDU-kommandon och följer inga formulär eller operationslänkar. Andra filreferenser listas enbart för manuell granskning. Skript sparas som textfiler under användarens lokala datamapp; de kan innehålla lokala uppgifter och ska granskas före delning. Ingen automatisk uppladdning sker. Verktyget kan köras när fysisk läsare åter finns tillgänglig; lyckad insamling är inte ett godkänt prov av ljudstyrning.

För utvecklare: `tools/test-reader-ui-inspection.ps1` kör helt frikopplade tester. `tools/build-reader-ui-inspection.ps1` bygger det separata paketet från en explicit fillista. Verktyget ingår inte i appens vanliga paket eller startflöde. Mac-appen använder CCID; Windows-verktyget ger inte i sig verifierat Mac-stöd för Gen1-ljud.

Vid återställning av en avbruten ljudändring jämför appen dessutom aktuella LED-värden, tider och repetitionsantal med den sparade återställningsposten. Om de har ändrats externt stoppas återställningen utan skrivning och posten behålls, så att senare ljusinställningar inte skrivs över.

## Val av kommandometod

Windows och Mac skapar en ny kompatibilitetsprofil vid varje anslutning. Modellkoden skiljer Gen1 från Gen2. Firmware behålls exakt som fyra byte; visningen 04.02.0100 innebär inte att godtyckliga versioner kan rangordnas eller att högre versionsnummer automatiskt ger fler funktioner.

Den observerade kombinationen Gen1 + 04020100 använder äldre ljudinställningar vars läs-/skrivmetod ännu inte är införd. Den får en förklaring i ljudknappens hjälptext utan att den moderna ljudfrågan skickas. Andra kombinationer börjar med okänt stöd: en ren läsning måste ge den validerade 130-bytetabellen innan sekvensmetoden får skriva. Ett uttryckligt noSuchName-svar spärrar metoden under den anslutningen. Transportfel och okända tabellformat räknas inte som verifierat stöd. Återanslutning eller ny firmware kräver en ny profil och kontroll.

Läsning av brickor och USB-förberedelse använder fortfarande sina gemensamma, svarskontrollerade protokoll. Ingen alternativ kommandoföljd för äldre ljud eller egen firmware har gissats fram. Insamlingen behövs för att fastställa och införa just den alternativa Gen1-ljudmetoden.

### Insamlingsverktyg 2

Verktyget skriver `kontroll.txt` före nätverksläsning och sparar arbetssteg, HTTP-status och antal byte löpande. Det sparar inga råsvar eller undantagsmeddelanden i kontrollrapporten. `rapport.txt` sparas även om inga skript hittas. Fel i en extra resurs stoppar inte andra resurser. PowerShell skriver dessutom en separat typ-/stegrapport och försöker skapa ZIP även vid misslyckad insamling. Skript som hunnit hämtas kan fortfarande innehålla lokala uppgifter; hela underlaget ska granskas före delning.

Tillåtna statiska HTTP-omdirigeringar följs högst tre gånger per resurs, till samma IP, port och verifierade USB-adapter. Inga externa mål, parametrar, autentiseringsuppgifter eller kommandosökvägar tillåts. Relativa skriptlänkar beräknas från den slutliga filsökvägen. Den totala gränsen 32 HTTP-förfrågningar omfattar även omdirigeringar; tids- och storleksgränser kvarstår. Appens striktare HTTP-parser och nätverksbeteende ändras inte. Verktyget återanvänder appens sparade adress i `reception.json` om den finns, utan att ändra filen.

En fungerande APDU-anslutning i appen bevisar inte att webbgränssnittets GET-resurser kan hämtas. Ett misslyckande visas därför som insamlingsfel, utan att påstå att USB-drivrutinen saknas. Den tidigare felrapporten med enbart InvalidDataException fastställer inte grundorsaken; fysisk verifiering av verktyg 2 återstår.

### Insamlingsverktyg 3: Gen1:s skriptlänk

Kontroll 2 från fysisk läsare visar fungerande USB-anslutning och HTTP 200 med 433 byte från startsidan. Den manuellt tillhandahållna HTML-strukturen innehåller `webserver.js` följt av frågetecken och en 32 tecken lång hexmarkör. Det generella förbudet mot parametrar gjorde att skriptet hoppades över och gav NO_SCRIPTS.

Referensupplösningen tar nu bort just denna form av cachemarkör för .js-filer innan den ordinarie kontrollen av värd, port och statisk sökväg. Varken markören eller andra parametrar skickas till läsaren. Frågeparametrar med nyckel/värde, andra filtyper, otillåtna mål och felaktiga markörer avvisas fortfarande. En reproduktion av sidans struktur med syntetisk markör verifierar att den centrala skriptfilen nu efterfrågas. Den faktiska markören och övriga lokala uppgifter har inte lagts i källkoden. Fysisk hämtning av skriptet återstår att prova.

### Insamlingsverktyg 4: skriptets HTTP-innehåll

Fysisk kontroll 3 bekräftar att även `/webserver.js` svarar HTTP 200, men verktyget avvisar innehållet före TEXT_BYTES. Den exakta felorsaken för en extra fil tappades bort och ersattes i slutrapporten av NO_SCRIPTS. Varje resursfel får nu en fast kod i både rapport och kontrollfil. Numerisk Content-Length och en känd Content-Encoding-klass sparas också, utan övriga header-värden.

Verktyget hanterar gzip och RFC1950-deflate med storleksbegränsad uppackning. CRC32/ISIZE respektive Adler32 kontrolleras även uttryckligen, och ogiltiga eller avklippta data avvisas. Rå deflate utan zlib-ram och okända eller staplade kodningar avvisas. Uppackad binärdata sparas inte som skript.

Den fristående verktygsgränsen blir 2 MiB per fil i både överförd och uppackad form; insamlingens totala gräns för behandlat underlag är fortsatt 2 MiB. Appens gräns är fortsatt 16 KiB och dess läsarsvar får fortfarande inte vara HTTP-komprimerade. Den gemensamma privata HTTP-hjälparens tillåtna maxparameter höjs för verktyget; appens anrop och gräns ändras inte.

Större eller komprimerat skript är möjliga orsaker till det fysiska stoppet, ännu inte bekräftade sådana. Den nya versionen och de säkra felkoderna ska hantera dessa fall och göra ett kvarvarande fel tydligt utan fler gissningar. Fysisk verifiering av version 4 återstår när läsaren åter finns tillgänglig.

Formatunderlag: RFC1950 (https://www.rfc-editor.org/rfc/rfc1950) och RFC1952 (https://www.rfc-editor.org/rfc/rfc1952). Framework DeflateStream använder rå RFC1951, varför zlib-ram och Adler32 hanteras separat i verktyget (Microsofts referenskod: https://github.com/microsoft/referencesource/blob/main/System/InternalApis/NDP_FX/inc/ZLibNative.cs). Tester använder oberoende Python-genererade vektorer, en expanderande testfil och korrupta/avklippta kontroller. Ingen kod från läsaren körs.

## Windows 3.6.5: Gen1-ljud via webbgränssnittet

Det insamlade skriptet analyserades som text utan att köras. Båda uppladdade kopiorna var identiska och behålls utanför repot. Råskriptet och lokala uppgifter distribueras inte med appen.

Funktionen Wc läser en rå MIB-PDU via POST `/ajax`; den använder inte APDU-omslaget på `/cjax`. LED-vyn begär tabellen `02020104010100` samt booleska `04010900`. Tabellposterna är 13 byte. De användarvalbara sekvens-ID:na är 0 (USB Ready), 1 (Card Access), 2 (No USB) och 10 (Keyboard Wedge). Den fjärde händelsen har alltså ID 10, inte ID 3.

Windows väljer denna backend endast för identifierad Gen1 med exakt firmware 04020100. Webbfrågan tillåter bara dessa två MIB:ar och bygger alltid en ren läs-PDU. Samma verifierade USB-adapter och IP-bindning som ordinarie trafik används, med samma 16 KiB-svarsgräns. Identitet och firmware kontrolleras före och efter läsning. Andra kombinationer behåller APDU-metoden.

En Gen1-tabell måste ha kompletta 13-byteposter och rymma ID 10 inom GUI:ts entydigt avkodningsbara BER-längd: 11–19 poster. Bara de fyra offentliga posternas flaggor och repetitionsfält valideras och redigeras. Övriga bytes bevaras. Flaggan för äldre tangentbordsindikering måste vara läsbar och av. Aktiv eller okänd flagga ger ingen ljudändring; appen ändrar aldrig själva flaggan.

Schema 2 för lokala Gen1-ljudkopior binder de fyra packade posterna till läsar-ID, firmware, protokoll och tabellängd. Äldre schema 1 med tre poster återtolkas inte till Gen1. Journalen flushas före första skrivningen. Varje skrivsteg kontrollerar aktuell identitet och oförändrade ljus-/tidsfält, lagrar enbart ID 0/1/2/10 och återställer automatisk indikering utan provpip eller rensning. Gen1 använder GUI:ts direkta lagringskommando och skickar ingen separat Apply som kan verkställa andra väntande MIB-ändringar. Hela tabellen läses tillbaka och jämförs.

Även omedelbar rollback kontrollerar att ljus, tider och repetitionsantal inte ändrats externt. Annars behålls journalen och de aktuella inställningarna. Återställning bevarar aktuell intern tabelldata. Moderna schema 1-kopior fortsätter fungera. Undvik samtidig redigering i webbgränssnittet när appen ändrar ljudet; läsaren erbjuder ingen atomisk jämför-och-skriv-operation.

Protokollvägen och indexen är belagda i den insamlade koden. Den verkliga tabellens format och av/på-resultat måste fortfarande provas med fysisk läsare. Denna version förbereds därför som förhandsversion. Mac har ännu ingen verifierad läsväg för denna Gen1-tabell via CCID, så stödet aktiveras inte där.
