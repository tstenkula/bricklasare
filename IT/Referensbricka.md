# Lokal referensbricka

Referensprov är frivilligt för IT. En ny läsare använder standardformatet utan detta steg. Ett referensprov behövs bara för en avvikande, oberoende verifierad byteordning eller felsökning; det krävs inte för varje bricka. Se [Första starten](Forsta-start.md).

Den ursprungliga leveransen innehöll identifierare för en fysisk referensbricka direkt i källkoden. I Git-versionen läses dessa i stället från en lokal IT-konfiguration. Vanliga skanningar fortsätter att använda den befintliga verifierade byteordningsprofilen.

**Vanliga taggar behöver inte registreras här.** Referensprovet verifierar läsarens byteordning. En redan verifierad profil fungerar för efterföljande stödda taggar utan nytt referensprov eller individuell konfiguration.

För att kalibrera en läsare med en ny referenskontroll:

1. Fastställ MIFARE-UID och EM-numrets fem byte för en känd kombibricka med en oberoende, verifierad metod. Värdena ska vara i den normaliserade byteordning som passersystemet förväntar sig. Använd inte en okontrollerad råavläsning från den läsare som ska kalibreras som facit.
2. Öppna **Hjälp → IT-inställningar → Ställ in referensbricka** och fyll i de verifierade värdena. Bekräfta att de fastställts oberoende och spara. Detta skriver `referensbricka.json` i `%LOCALAPPDATA%\Tobtech\Omnikey-RCO-v2\` för Windows-kontot som kör programmet. Fälten heter `MifareHex` och `EmHex` och innehåller hexadecimala strängar utan mellanslag. MIFARE ska vara 4, 7 eller 10 byte; EM ska vara 5 byte.
3. Välj **Hjälp → IT-inställningar → Kontrollera referensbricka** och använd den konfigurerade fysiska brickan i båda stegen.

Exemplet nedan innehåller enbart konstruerade testvärden. Ersätt dem med IT:s verifierade lokala värden; exemplet är inget facit för en verklig bricka.

```json
{
  "MifareHex": "01020304",
  "EmHex": "0102030405"
}
```

Formuläret ändrar inget i läsaren och startar inte en skanning. Underlaget är lokalt för IT.

Programmet avvisar saknade eller ogiltiga referensvärden före skanningens läsaråtkomst och inställningsändringar. En byteföljd som är identisk när den vänds går inte att använda för att fastställa byteordningen och avvisas också. Om avlästa värden inte matchar den konfigurerade referensen sparas ingen ny byteordning.

Konfigurationsfilen ignoreras av Git och kopieras inte när programmet skapar ett receptionspaket. Ett sådant paket innehåller däremot den verifierade lokala läsarprofilen och ska hållas utanför Git. Kalibreringens sparade profil innehåller läsaridentitet, firmware och byteordning; referensbrickans identifierare läggs inte till i den.
