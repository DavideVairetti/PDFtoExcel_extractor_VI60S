# Estrattore Contatori Report

Applicazione console Windows in C#/.NET per estrarre automaticamente i contatori dai report PDF generati da macchina VI60S e salvarli in file Excel `.xlsx`.

Per ogni PDF elaborato viene creato un file Excel con lo stesso nome del report, in una sottocarltella di quella del PDF.

Esempio:

```text
C:\Report\Report.pdf
C:\Report\excel\Report.xlsx
```

## Funzionalità

- Lettura di un singolo PDF o di una cartella intera.
- Ricerca ricorsiva dei PDF nelle sottocartelle.
- Estrazione di:
  - sezione
  - nome contatore
  - valore
  - percentuale
  - pagina origine
- Estrazione dei principali metadati del report.
- Creazione Excel `.xlsx` senza necessità di Microsoft Excel installato.
- Log su file `estrazione_contatori.log` nella cartella principale di elaborazione.
- Gestione di file Excel già esistenti o bloccati tramite suffisso progressivo.
- Continuazione dell'elaborazione anche se un PDF fallisce.

## Requisiti per sviluppo

- Windows
- .NET SDK 10 o superiore
- Connessione internet solo in fase di `dotnet restore`, per scaricare i pacchetti NuGet

Pacchetti NuGet usati:

- `PdfPig` per la lettura dei PDF. Il namespace usato nel codice è `UglyToad.PdfPig`.
- `ClosedXML` per la creazione dei file Excel `.xlsx`.

Il programma pubblicato in modalità self-contained non richiede .NET installato sul PC di destinazione.

## Struttura progetto

```text
EstrattoreContatori/
    EstrattoreContatori.csproj
    Program.cs
    Models/
        CounterRecord.cs
        ReportMetadata.cs
        ExtractionResult.cs
    Services/
        PdfCounterExtractor.cs
        ExcelReportWriter.cs
        FileDiscoveryService.cs
        LogService.cs
    README.md
```

## Compilazione

Aprire un terminale nella cartella del progetto ed eseguire:

```bash
dotnet restore
dotnet build -c Release
```

## Esecuzione in sviluppo

Elaborare una cartella:

```bash
dotnet run -- "C:\Report"
```

Elaborare un singolo PDF:

```bash
dotnet run -- "C:\Report\Report.pdf"
```

## Pubblicazione eseguibile Windows singolo

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

L'eseguibile viene generato in una sottocartella simile a:

```text
bin\Release\net10.0\win-x64\publish\EstrattoreContatori.exe
```

È possibile copiare `EstrattoreContatori.exe` su un PC Windows ed eseguirlo senza installare .NET, Python, Microsoft Excel o altri componenti.

## Esempi d'uso

Avvio senza argomenti:

```bash
EstrattoreContatori.exe
```

Usa come cartella di lavoro la cartella in cui si trova l'eseguibile e cerca ricorsivamente tutti i PDF.

Avvio con path cartella:

```bash
EstrattoreContatori.exe "C:\Report"
```

Avvio con path file PDF:

```bash
EstrattoreContatori.exe "C:\Report\Buoni KIT_Report.pdf"
```

Avvio interattivo:

```bash
EstrattoreContatori.exe --ask
```

Ricerca non ricorsiva:

```bash
EstrattoreContatori.exe "C:\Report" --no-recursive
```

Sovrascrittura dei file Excel esistenti, se possibile:

```bash
EstrattoreContatori.exe "C:\Report" --overwrite
```

## File generati

Per ogni PDF viene creato un Excel `.xlsx` con almeno due fogli.

### Foglio `Contatori`

Colonne:

- Nome contatore
- Valore
- Percentuale

La percentuale viene salvata come numero leggibile, ad esempio `91.85`, non come `0.9185`.

### Foglio `Metadati`

Colonne:

- Campo
- Valore

Metadati estratti quando disponibili:

- Tipo di macchina
- Autore del lotto
- Autore del rapporto
- N. macchina
- Descrizione del lotto
- Fine del lotto
- Avvio del lotto
- Nome del lotto
- Modo della macchina
- Ricetta
- Campioni per ciclo
- Nome file PDF
- Percorso file PDF
- Data/ora elaborazione

## Gestione file già esistenti

Se il file Excel esiste già e non viene usato `--overwrite`, viene creato un nome alternativo:

```text
Buoni KIT_Report_estratto_1.xlsx
Buoni KIT_Report_estratto_2.xlsx
Buoni KIT_Report_estratto_3.xlsx
```

Se il file Excel è aperto o bloccato, il programma tenta automaticamente un nome alternativo.

## Log ed errori

Il programma crea un file:

```text
estrazione_contatori.log
```

nella cartella principale di elaborazione.

Il log contiene:

- data e ora di avvio
- path elaborato
- numero PDF trovati
- elenco PDF elaborati
- Excel creati
- numero contatori estratti per ogni PDF
- warning per PDF senza testo o senza contatori
- errori dettagliati per PDF non leggibili
- riepilogo finale

## Codici di uscita

- `0` = completato senza errori
- `1` = input non valido
- `2` = nessun PDF trovato
- `3` = uno o più PDF falliti
- `4` = errore imprevisto

## Note sul parser

Il parser riconosce le sezioni principali dei report KIT:

- Contatore globale
- Sensori espulsione
- Contatore di categorie
- Contatore di difetti
- Difetti singoli
- Difetti particelle
- Nome delle categorie

Le righe contatore riconosciute hanno forma:

```text
<nome contatore> <valore> <percentuale> %
```

oppure:

```text
<nome contatore> <valore>
```

Il parser ignora intestazioni, footer, righe pagina e metadati del report.
