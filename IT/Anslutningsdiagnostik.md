# Kontrollera anslutningen i Windows

Stäng Brickläsaren och äldre versioner av programmet. Låt en läsare vara ansluten och ta bort brickan. Öppna `IT/Kontrollera-anslutning.cmd` i det uppackade paketet.

Kontrollen läser modell, firmware, identitet, skanningsinställningar och USB-läge samt granskar de befintliga lokala USB-filerna. Den ändrar inga läsarinställningar, startar inte om läsaren och återställer eller tar bort inga konfigurationsfiler. Den sparar bara rapporten `anslutningskontroll.txt` under `%LOCALAPPDATA%\Tobtech\Omnikey-RCO-v2`. Om säkerhetspolicyn stoppar starten används ordinarie IT-rutin.

Skicka rapportens innehåll till den som felsöker. Bricknummer, läsar-ID, IP-adresser, råsvar och användarsökvägar ingår inte i rapporten. Sökvägen till den sparade rapporten visas separat i terminalen.

| Kod | Vad som stoppades |
|---|---|
| `USB_MODE_RESPONSE` | Läsarens svar om USB-läge har okänd typ, längd eller värde. |
| `USB_BACKUP_INVALID` | Originalfilen har fel struktur eller gäller annan läsare/firmware. |
| `USB_BACKUP_MISSING` | En avbruten förberedelse saknar verifierbar originalfil. |
| `USB_PENDING_INVALID` | Filen för en avbruten förberedelse kan inte godkännas. |
| `MIB_REJECTED` | Läsaren avvisade ett kommando. Kommandogrupp, status och index visas. |
| `HTTP_…` | Svaret från USB-läsaren kunde inte godkännas. Koden skiljer bland annat format, längd och kodning. |

Metodnamnen i rapporten kommer från programmets källkod. De innehåller inga argument, fullständiga anropsstackar eller källfilssökvägar. Okända undantagsmeddelanden kopieras aldrig till rapporten.

Att version 3.1 fungerade bekräftar inte att senare USB-förberedelse har lyckats. Radera inte original- eller återställningsfiler och återställ inte läsaren till fabriksinställningar som första felsökningssteg. Rapporten hjälper oss att skilja en läsarkompatibilitet från ett lokalt filproblem.

Från 3.6.3 godkänner Windows både `00`/`FF` och heltalsrepresentationerna `0000`/`00FF` för USB-läget (av/på). Andra värden och fel datatyper stoppas fortfarande. En verklig läsare har rapporterat INTEGER med två byte; anslutningen med 3.6.3 har därefter bekräftats fungera på den läsaren. Själva brickläsningen och övriga hårdvaruprov behöver fortfarande bekräftas.

### Ljudformat och uppdateringsspärr (3.6.6)

Spara felrapport under Hjälp. Vid Gen1-formatfel anger `SOUND_GEN1_FORMAT` kontrollen `Length` eller `Flags`, tabellens byteantal och vid flaggfel en offentlig sekvens-/stegposition. Inga sekvensvärden delas.

Rapporten räknar även sparade återställningsfiler för läsning respektive USB, utan namn, läsar-ID eller innehåll. En sådan fil kan tillhöra en tidigare körning eller annan läsare även om den anslutna läsaren är klar. Uppdateringsspärren behålls och förklaras uttryckligen; inga journaler raderas eller kringgås. Använd Återställ läsaren bara om den knappen är tillgänglig för den anslutna läsaren. Om den redan är klar och uppdateringen ändå spärras behöver IT granska den lokala återställningen. Manuell uppackning i en separat programmapp bevarar AppData, där återställningen ligger.

### Uppdateringar med sparad återställning (3.6.7)

Spärren i 3.6.6 och äldre var för bred: den blockerade appuppdateringar när någon läsare hade sparad återställning, även om ingen åtgärd pågick. Från 3.6.7 får uppdateringen fortsätta när appen är ledig. Den vanliga läsningen kräver fortfarande rätt läsare och verifierad återställning.

Efter att gamla appen avslutats och installationshjälpen tagit ensamrätt kopieras alla lokala JSON-filer oförändrade till `updates/<jobb>/preserved-settings` i den privata datamappen. Originalen behålls. Filantal (256), filstorlek (64 KiB) och total storlek (2 MiB) begränsas; länkar och mappar med JSON-namn avvisas. Ingen JSON tolkas eller laddas upp. Tomma och ogiltiga filer kopieras också. Kopian och originalen jämförs före programbytet, och originalen kontrolleras även efteråt. Ändringar under installationen stoppar uppdateringen utan att externa ändringar skrivs över. Programmappen får inte omfatta datamappen eller tvärtom. Tidigare programmapp bevaras som tidigare.

3.6.6 och äldre kör den gamla spärren innan ny kod hämtas och använder sin egen installationshjälp. Därför behöver den första övergången från en redan spärrad installation göras manuellt till 3.6.7, i en separat programmapp. AppData ska behållas. Därefter kan kommande appuppdateringar installeras med sparad återställning kvar.
