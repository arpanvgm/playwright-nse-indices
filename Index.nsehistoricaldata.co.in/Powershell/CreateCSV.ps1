# This script is designed to read Index Price data downloaded from https://index.nsehistoricaldata.co.in/
# Open downloaded excel file 
# and convert it into A CSV file with comma seprator delimiter (UTF-8 encoding) option in save as dialog.
# Place it in the "SectorPrices" input folder. 
# This script will read all CSV files from that folder, extract all data, and create a new CSV file with the required format in the "MainCsvStorage" output folder. 
# If a file with the same name already exists in the output folder, it will skip processing that file to avoid overwriting existing data.

#==========================================
# To run this script : Go to the folder location in terminal and run the command : .\CreateCSV.ps1
# ==========================================

#==========================================
# CONFIGURATION
# ==========================================

$scriptDir = $PSScriptRoot
$inputFolder = Join-Path $scriptDir "..\InputData\SectoralPrices"
$outputFolder = Join-Path $scriptDir "..\MainCsvStorage"

# Create output folder if not exists
if (!(Test-Path $outputFolder)) {

    New-Item -ItemType Directory -Path $outputFolder | Out-Null
}

# ==========================================
# FUNCTION : Convert To Index Name
# ==========================================

function Convert-ToIndexName {

    param (
        [string]$text
    )

    # Remove everything after "_Historical"
    $clean = $text -replace "_Historical.*", ""

    # Replace underscore with hyphen
    $clean = $clean -replace "_", "-"

    return $clean
}

# ==========================================
# PROCESS ALL CSV FILES
# ==========================================

Get-ChildItem -Path $inputFolder -Filter *.csv | ForEach-Object {

    $file = $_
    $filePath = $file.FullName
    $fileName = $file.BaseName

    Write-Host ""
    Write-Host "Processing file: $($file.Name)"

    # ==========================================
    # DERIVE INDEX NAME
    # ==========================================

    $IndexName = Convert-ToIndexName $fileName

    Write-Host "IndexName: $IndexName"

    # ==========================================
    # OUTPUT FILE
    # ==========================================

    $outputFile = Join-Path $outputFolder ("Main_" + $IndexName + ".csv")

    # ==========================================
    # SKIP IF FILE EXISTS
    # ==========================================

    if (Test-Path $outputFile) {

        # This is most important to avoid overwriting existing files and losing data
        Write-Host "Skipped (already exists): $outputFile"
        return
    }

    # ==========================================
    # IMPORT CSV
    # ==========================================

    $csvData = Import-Csv -Path $filePath

    if ($csvData.Count -eq 0) {

        Write-Host "No rows found in file: $($file.Name)"
        return
    }

    # ==========================================
    # TRANSFORM DATA
    # ==========================================

    $finalData = foreach ($row in $csvData) {

        [PSCustomObject]@{

            # Index Metadata
            IndexName      = $IndexName

            # Historical Data
            Date           = $row.DATE
            Open           = $row.OPEN
            High           = $row.HIGH
            Low            = $row.LOW
            Close          = $row.CLOSE
            SharesTraded   = $row.'SHARES TRADED'
            TurnoverInrCr  = $row.'TURNOVER (INR CR)'

            # Placeholder Valuation Columns
            PE             = ""
            PB             = ""
            DividendYield  = ""
        }
    }

    # ==========================================
    # EXPORT CSV
    # ==========================================

    $finalData |
        Export-Csv `
            -Path $outputFile `
            -NoTypeInformation `
            -Encoding UTF8

    Write-Host "Generated: $outputFile"
}

Write-Host ""
Write-Host "All files processed successfully."
