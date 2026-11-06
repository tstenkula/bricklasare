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
