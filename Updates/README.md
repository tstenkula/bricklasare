# Uppdateringar och publiceringsberedskap

Programmen hämtar en liten, signerad uppdateringsfil från `https://raw.githubusercontent.com/tstenkula/bricklasare/main/Updates/stable.feed` eller `preview.feed`. Där anges version, storlek, SHA-256 och en GitHub Release-adress för respektive plattform. Inga GitHub-token eller behörighetskoder lagras i programmen.

**Repot är fortfarande privat.** En vanlig användares uppdateringskontroll kommer därför inte åt källan. Programmet visar detta begripligt och fungerar i övrigt utan nätåtkomst. Repot måste uttryckligen göras publikt senare för att denna källa ska fungera utan inloggning. Tomma kanalposter betyder att ingen version har publicerats i den kanalen.

Vid start kan programmet söka efter en nyare version. Automatisk kontroll kan stängas av under Arbetsflöde. Hämtning och installation kräver användarens val. Ingen installation påbörjas under läsning eller med en sparad återställning som väntar.

## Kontroller och installation

- RSA-3072/SHA-256-signaturen verifieras mot en publik nyckel inbyggd i programmet innan uppdateringsinformationen används.
- Endast HTTPS och GitHubs nedladdningsservrar tillåts. Versioner jämförs numeriskt; samma eller äldre version installeras inte genom dialogen.
- Paketets storlek och SHA-256 kontrolleras. ZIP-sökvägar, länkar och orimliga storlekar avvisas.
- En separat hjälpprocess väntar tills programmet avslutats, låser installationen och testar den nya versionen innan programmappen ersätts. Windows använder fortsatt PowerShell, utan egen EXE och utan ändrad säkerhetspolicy.
- En `.previous-…`-kopia av föregående program behålls. Misslyckas själva mappbytet läggs den tillbaka. Det är ingen automatisk återställning efter alla tänkbara fel i en nystartad version; den sparade kopian kan startas manuellt.
- Personliga inställningar ligger utanför programmappen och bevaras. Mac-appar på en monterad DMG behöver först flyttas till en skrivbar Program-mapp. Windows programmapp och dess förälder behöver vara skrivbara; ingen automatisk administratörshöjning görs.
- Uppdateringsstatus sparas lokalt utan råa undantagsmeddelanden eller kortdata.

Detta är **paketverifiering**, inte Windows Authenticode eller Apple Developer ID/notarisering. Det tar inte bort operativsystemets varningar om utgivare.

## Släppa en version

1. Höj versionsnumret på båda plattformarna. Kör testerna och bygg Windows ZIP, Mac ZIP och DMG från samma verifierade källa. Publicera arkiven som en GitHub Release i `tstenkula/bricklasare`.
2. Signera kanalfilen lokalt med `tools/release.py`. Exempel:

   ```sh
   python3 tools/release.py --key /secure/path/update-signing.pem --channel preview --version 3.5.1 --tag v3.5.1-preview.1 --windows dist/Bricklasaren-3.5.1-Windows.zip --mac dist/Bricklasaren-3.5.1-macOS-AppleSilicon.zip --output Updates/preview.feed
   ```

3. Kontrollera att arkivens hash stämmer mot GitHub, uppdatera källfilernas kontrollsummor och skicka kanalfilen genom granskning till `main`. Flytta inte en befintlig release till annat innehåll efter publicering.
4. Ge ut en stabil kanal först efter avsedd acceptanskontroll med riktig läsare. Kanalval påverkar bara uppdateringar, aldrig nummerformat eller sparad profil.

Nyckeln skapades lokalt utanför arbetsmappen och ska säkerhetskopieras privat av projektägaren. Den får inte läggas i Git, releasearkiv, loggar eller ärenden. Förlorad nyckel kräver en särskild plan för nyckelbyte; det räcker inte att byta den publika nyckeln i en kanalfil. Konton som kan ändra utgåvor bör skyddas väl. En publik kontrollsumma ensam ersätter inte signaturen.
