# Uppdateringar och publiceringsberedskap

Programmen hämtar en liten, signerad uppdateringsfil från `https://raw.githubusercontent.com/tstenkula/bricklasare/main/Updates/stable.feed` eller `preview.feed`. Där anges version, storlek, SHA-256 och en GitHub Release-adress för respektive plattform. Inga GitHub-token eller behörighetskoder lagras i programmen.

**Repot är publikt.** Uppdateringsfiler och officiella paket kan hämtas utan inloggning. Programmet fungerar även utan nätåtkomst. Välj **Stabil** för stabila utgåvor eller **Förhandsversioner** för förhandskanalen. En tom kanalpost betyder att ingen version har publicerats i den kanalen. Bara högre versionsnummer erbjuds; 3.6.1 kan därför uppdatera en installerad 3.6.0. Hårdvaruprov med fysisk läsare återstår även för den första stabila utgåvan.

Vid start kan programmet söka efter en nyare version. Automatisk kontroll kan stängas av under Arbetsflöde. Hämtning och installation kräver användarens val. Ingen installation påbörjas under läsning eller med en sparad återställning som väntar.

## Kontroller och installation

- RSA-3072/SHA-256-signaturen verifieras mot en publik nyckel inbyggd i programmet innan uppdateringsinformationen används.
- Endast HTTPS och GitHubs nedladdningsservrar tillåts. Versioner jämförs numeriskt; samma eller äldre version installeras inte genom dialogen.
- Paketets storlek och SHA-256 kontrolleras. ZIP-sökvägar, länkar och orimliga storlekar avvisas.
- Från 3.5.2 sparas den ursprungliga signerade kanalfilen. Installationshjälpen verifierar signaturen igen och hämtar version och kontrollsumma ur det signerade innehållet. En ändrad lokal installationsplan kan inte ersätta dessa värden. Uppackning använder samma verifierade paketbytes som kontrollerades.
- En separat hjälpprocess väntar tills programmet avslutats, låser installationen och testar den nya versionen innan programmappen ersätts. Windows använder fortsatt PowerShell, utan egen EXE och utan ändrad säkerhetspolicy.
- En `.previous-…`-kopia av föregående program behålls. Misslyckas själva mappbytet läggs den tillbaka. Det är ingen automatisk återställning efter alla tänkbara fel i en nystartad version; den sparade kopian kan startas manuellt.
- Personliga inställningar ligger utanför programmappen och bevaras. Mac-appar på en monterad DMG behöver först flyttas till en skrivbar Program-mapp. Windows programmapp och dess förälder behöver vara skrivbara; ingen automatisk administratörshöjning görs.
- Uppdateringsstatus sparas lokalt utan råa undantagsmeddelanden eller kortdata.

Detta är **paketverifiering**, inte Windows Authenticode eller Apple Developer ID/notarisering. Det tar inte bort operativsystemets varningar om utgivare.

Övergången från 3.5.1 använder den redan installerade versionens installationshjälp. Den extra kontrollen i sista installationssteget gäller uppdateringar som startas från 3.5.2 och senare. En nyinstallation av 3.5.2 innehåller hela skyddet. Användarens skrivbehörighet, den ursprungliga appens äkthet och signeringsnyckelns skydd är fortfarande viktiga.

## Släppa en version

1. Höj versionsnumret på båda plattformarna och i Windows installationshjälp. Kör testerna och bygg Windows ZIP, Mac ZIP och DMG från samma verifierade källa. Skapa först en utgåva som utkast i `tstenkula/bricklasare`, ladda upp samtliga arkiv och kontrollsummor, verifiera innehållet och publicera sedan. Nya publicerade utgåvors filer och taggar är låsta genom GitHubs release immutability.

   Signeringsverktyget kontrollerar lokalt att nyckeln motsvarar apparnas inbyggda publika nyckel, att version/kanal/tagg stämmer och att ZIP-filerna har rätt namn och versionsuppgifter. Befintlig kanalfil lämnas orörd om kontrollen misslyckas. Det ersätter inte kontrollen av de uppladdade filerna.
2. Signera kanalfilen lokalt med `tools/release.py`. Exempel:

   ```sh
   python3 tools/release.py --key /secure/path/update-signing.pem --channel stable --version 3.6.2 --tag v3.6.2 --windows dist/Bricklasaren-3.6.2-Windows.zip --mac dist/Bricklasaren-3.6.2-macOS-AppleSilicon.zip --output Updates/stable.feed
   ```

3. Kontrollera att arkivens hash stämmer mot GitHub, uppdatera källfilernas kontrollsummor och skicka kanalfilen genom granskning till `main`. Flytta inte en befintlig release till annat innehåll efter publicering.
4. Dokumentera vilka acceptanskontroller som är genomförda och vilka som återstår innan projektägaren godkänner en stabil utgåva. För 3.6.1 återstår fysisk provkörning; detta ska stå i utgåvans information. Behåll förhandsutgåvan och skapa en separat stabil utgåva med samma verifierade paket om ingen kod ändrats. Om koden ändras, höj versionsnumret så att redan installerade förhandsversioner erbjuds uppdateringen. Kanalval påverkar bara uppdateringar, aldrig nummerformat eller sparad profil.

Nyckeln skapades lokalt utanför arbetsmappen och ska säkerhetskopieras privat av projektägaren. Den får inte läggas i Git, releasearkiv, loggar eller ärenden. Förlorad nyckel kräver en särskild plan för nyckelbyte; det räcker inte att byta den publika nyckeln i en kanalfil. Konton som kan ändra utgåvor bör skyddas väl. En publik kontrollsumma ensam ersätter inte signaturen.

## Prov av publicerade paket

GitHub Actions-flödet **Published update acceptance** provar tre verkliga uppdateringar: 3.5.2 → 3.6.0 (förhandskanalen), 3.6.0 → 3.6.1 och 3.6.1 → 3.6.2 (stabil). Varje fall körs separat på Windows och Mac med den tidigare publicerade installationshjälpen. Flödet körs manuellt eller vid ändringar i provets egna filer på en gren i detta repo. Historisk kanalfil och äldre paket är låsta till sina kontrollerade versioner. De stabila fallen använder de ursprungliga signerade 3.6.1- och 3.6.2-filerna som testdata, så att ett senare versionssläpp inte ändrar provet.

Provet använder tillfälliga Windows- och Apple Silicon-datorer hos GitHub, syntetiska inställningar och inga läsare. Det avvisar befintliga användardata och körs inte på personliga eller egna servrar. Installation, bevarade inställningar och tidigare programmapp kontrolleras; Mac kontrollerar även normal omstart. Hämtningen startas av provverktyget, så detta är inte ett klickprov av hela uppdateringsdialogen. Det ersätter inte hårdvaruproven i [IT/Verifiering.txt](../IT/Verifiering.txt).

### Installation validation and visible update action (3.6.8)

When the automatic check finds a newer version in the selected channel, the main reader window shows the version and an **Uppdatera** button. The button checks the signed feed again and asks for confirmation before downloading. It is disabled during reader work, remains available after clearing card results, and hides an offer from a different channel.

Installers through 3.6.7 invoked the full UI regression suite while validating the replacement, which could open and close many test windows. From 3.6.8, both the legacy installer arguments and the explicit installation-validation arguments perform quiet package validation. Full tests remain separate CI/IT commands. Windows CI observes actual child-process window events, including a positive control, to verify that successful and failed preflight create no application windows or user-data writes. This does not establish physical-reader acceptance.
