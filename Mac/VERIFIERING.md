# Verifiering av Mac-versionen

Lokalt verifierad 2026-11-03 på Apple Silicon med macOS 27.2 och Swift 6.4.2. Byggmålet är arm64 och macOS 13 eller senare. Intel ingår inte.

- Byggd och lokalt ad-hoc-signerad `.app`, paketerad i komprimerad DMG. Signaturkontroll görs i en temporär byggmapp utanför synkade mappar. DMG-kontrollsumman verifieras av `hdiutil`.
- Offlinekontroller omfattar nummerformat, läsning, identitet, avbrott/återställning, inställningar, signerade uppdateringsfiler och ZIP-kontroller. Endast syntetiska nummer används.
- Resursprovet kör 100 livscykler med inställningsfönster och ombyggnad av huvudfönstret. Fönsterantal och minnesfotavtryck skrivs till testresultatet. Detta är en avgränsad regressionstest, inte bevis för att alla tänkbara läckor saknas.
- Uppdateringens filbyte provas i en tillfällig katalog med lyckat byte och framtvingat fel: föregående version och personliga filer ska finnas kvar.
- GitHub bygger på macOS 15; lokal utveckling sker på Apple Silicon med macOS 27.2. Aktuella resultat finns under repoets Actions.
- Källkod och paket ska inte innehålla lokala läsarprofiler, verkliga kortnummer, personliga inställningar, loggar eller privata nycklar.

Kvar för fysisk provkörning: en fabriksny och en tidigare använd OMNIKEY 5427 CK/Gen2, macOS tillbehörsgodkännande, CCID/escape-stöd i läsarens firmware, byte från tangentbord/Custom Report, MIFARE/EM-avläsning, frånkoppling under läsning, återställning och längre drift. Äldre stödda macOS-versioner behöver också verklig körning; ett lägsta byggmål ersätter inte dessa tester.
