# Färg och logotyp

Öppna **Hjälp → IT-inställningar → Utseende – färg och logotyp**.

- Välj **Marinblå** (`#003B55`), grönt eller lila tema, eller en egen färg med färgväljaren eller `#RRGGBB`.
- Fyll valfritt i organisationens namn och en underrubrik.
- Välj en PNG-logotyp, högst 2 MB och 2048 × 2048 bildpunkter. Genomskinlig bakgrund stöds. Bilden kopieras till programmets datamapp och låser inte originalfilen.
- Granska förhandsvisningen och välj **Spara utseende**. Huvudfönstret ändras direkt. **Avbryt** lämnar sparade inställningar oförändrade.
- **Återställ** visar standardutseendet: marinblå färg utan organisationstext eller logotyp. Ändringen gäller först när du sparar.

Text på aktiva accentknappar och accentfärgade rubriker får automatiskt minst 4,5:1 kontrast. Varningsfärgerna påverkas inte. Förhandsvisningen visar den valda färgen och den anpassade textfärgen.

Inställningen ligger i `utseende.json` och logotypen i `branding/` under `%LOCALAPPDATA%\Tobtech\Omnikey-RCO-v2\`. Den gäller det aktuella Windows-kontot och finns kvar när programmappen byts. Vid lyckat logotypbyte tas den tidigare appkopian bort. Originalbilden ändras aldrig.

Utseendet och logotypen ingår inte i Git eller exporterade receptionspaket. Välj dem separat på en ny dator eller för ett annat Windows-konto. En saknad eller skadad lokal logotyp ska inte hindra att programmet startar.

Färgen ändrar inte hur kortnummer läses, formateras eller kopieras. Inställningar för uppdateringar finns separat under **Hjälp → IT-inställningar**.

Från version 3.4 är standardfärgen marinblå `#003B55`. En tidigare sparad färg ändras inte automatiskt. För att byta till marinblått utan att ta bort logotyp eller texter, välj **Marinblå** i färgtemalistan och spara. Använd **Återställ** bara när även namn och logotyp ska tas bort.
