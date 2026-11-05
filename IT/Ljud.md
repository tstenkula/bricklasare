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
