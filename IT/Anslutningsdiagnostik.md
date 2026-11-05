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
