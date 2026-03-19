$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..\..')
$sourceDocxPath = Join-Path $repoRoot '_source\EXample.docx'
$fallbackDocxPath = Join-Path $repoRoot 'Kor.Operations.App\GeneralTools\EXample.docx'
$docxPath = if (Test-Path $sourceDocxPath) { $sourceDocxPath } else { $fallbackDocxPath }
$paraMapPath = Join-Path $repoRoot '_example_docx_para_map.jsonl'
$relsPath = Join-Path $repoRoot '_document_rels.xml'
$outputJsonPath = Join-Path $scriptRoot 'Original.seed.json'
$assetsPath = Join-Path $scriptRoot 'OriginalAssets'
$extractPath = Join-Path $scriptRoot ('_docx_extract_' + [guid]::NewGuid().ToString('N'))

$overviewHeadings = @(
    'EXCELLENCE IN STRUCTURAL ENGINEERING',
    'EXPERIENCE',
    'SERVICES',
    'SYSTEMS AND ORGANIZATIONAL QUALITY MANAGEMENT',
    'TECHNOLOGY'
)

$projectSectionHeadings = @(
    'FEATURED LARGE MIXED-USE COMMERCIAL & RESIDENTIAL PROJECTS - CANADA',
    'LOW-RISE WOOD-FRAME RESIDENTIAL PROJECTS',
    'CROSS LAMINATED TIMBER (CLT) PROJECTS',
    'NAIL LAMINATED TIMBER (NLT) PROJECT',
    'FEATURED LARGE MIXED-USE COMMERCIAL & RESIDENTIAL PROJECTS - USA'
)

$allSectionHeadings = $overviewHeadings + @('PEOPLE') + $projectSectionHeadings + @('CLIENTS INCLUDE')

$personNames = @(
    'John Markulin',
    'Jim DesRoches',
    'Rory Beirne',
    'Kevin Wurmlinger',
    'Jason Stuart',
    'Conor Murtagh',
    'Omar Alcazar Pastrana',
    'John Bryson'
)

function Clean-Text {
    param(
        [AllowNull()]
        [string]$Text
    )

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    $cleaned = [System.Text.RegularExpressions.Regex]::Replace($Text, '<[^>]+>', '')
    $cleaned = $cleaned.Replace([char]0x00A0, ' ')
    $cleaned = [System.Text.RegularExpressions.Regex]::Replace($cleaned, '\s+', ' ')
    return $cleaned.Trim()
}

function Get-FirstRelId {
    param(
        [AllowNull()]
        [string]$RelIds
    )

    if ([string]::IsNullOrWhiteSpace($RelIds)) {
        return $null
    }

    return ($RelIds -split ',')[0].Trim()
}

function New-Photo {
    param(
        [string]$FilePath
    )

    return @{
        FilePath = $FilePath
        Caption = ''
    }
}

function Get-ImageTargetMap {
    param(
        [string]$Path
    )

    [xml]$relsXml = Get-Content $Path
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($relsXml.NameTable)
    $namespaceManager.AddNamespace('pr', 'http://schemas.openxmlformats.org/package/2006/relationships')

    $map = @{}
    foreach ($relationship in $relsXml.SelectNodes('//pr:Relationship', $namespaceManager)) {
        if ($relationship.Type -notlike '*image') {
            continue
        }

        $target = Split-Path -Leaf $relationship.Target
        $map[$relationship.Id] = $target
    }

    return $map
}

function Get-ParagraphEntries {
    param(
        [string]$Path
    )

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($line in Get-Content $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $entry = $line | ConvertFrom-Json
        $entries.Add([pscustomobject]@{
            Text = (Clean-Text $entry.Text)
            RelId = (Get-FirstRelId $entry.RelIds)
        })
    }

    return $entries
}

function Copy-DocxImages {
    param(
        [string]$DocxPath,
        [string]$DestinationPath,
        [string]$TempExtractPath
    )

    New-Item -ItemType Directory -Path $TempExtractPath | Out-Null
    if (-not (Test-Path $DestinationPath)) {
        New-Item -ItemType Directory -Path $DestinationPath | Out-Null
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($DocxPath, $TempExtractPath)

    Get-ChildItem (Join-Path $TempExtractPath 'word\media') -File |
        Where-Object { $_.Name -like 'image*.*' } |
        ForEach-Object {
            Copy-Item $_.FullName (Join-Path $DestinationPath $_.Name)
        }
}

function Find-EntryIndex {
    param(
        [System.Collections.Generic.List[object]]$Entries,
        [string]$Text
    )

    for ($i = 0; $i -lt $Entries.Count; $i++) {
        if ($Entries[$i].Text -eq $Text) {
            return $i
        }
    }

    return -1
}

function Is-ProjectTitleEntry {
    param(
        [System.Collections.Generic.List[object]]$Entries,
        [int]$Index,
        [int]$SectionEndIndex
    )

    $Text = $Entries[$Index].Text
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    if ($allSectionHeadings -contains $Text) {
        return $false
    }

    if ((Normalize-Label $Text) -in @('Client', 'Clients', 'Architect', 'Company')) {
        return $false
    }

    if ($Text.Length -lt 8) {
        return $false
    }

    $leadingSegment = ($Text -split ',')[0]
    $lettersOnly = [System.Text.RegularExpressions.Regex]::Replace($leadingSegment, '[^A-Za-z]', '')
    if ($lettersOnly.Length -lt 4) {
        return $false
    }

    $uppercaseCount = ([System.Text.RegularExpressions.Regex]::Matches($lettersOnly, '[A-Z]')).Count
    $uppercaseRatio = $uppercaseCount / $lettersOnly.Length
    $hasTitleLikeLead = $uppercaseRatio -ge 0.65
    $hasShortAddressLikeLine = $Text.Length -lt 90 -and $Text.Contains(',')
    if (-not $hasTitleLikeLead -and -not $hasShortAddressLikeLine) {
        return $false
    }

    $searchLimit = [Math]::Min($Index + 8, $SectionEndIndex - 1)
    for ($i = $Index + 1; $i -le $searchLimit; $i++) {
        $nextText = $Entries[$i].Text
        if ((Normalize-Label $nextText) -in @('Client', 'Clients')) {
            return $true
        }

        if ($allSectionHeadings -contains $nextText) {
            return $false
        }
    }

    return $false
}

function Resolve-AssetPath {
    param(
        [AllowNull()]
        [string]$RelId,
        [hashtable]$ImageTargetMap
    )

    if ([string]::IsNullOrWhiteSpace($RelId)) {
        return ''
    }

    $imageName = $ImageTargetMap[$RelId]
    if ([string]::IsNullOrWhiteSpace($imageName)) {
        return ''
    }

    return "GeneralTools\SeedData\OriginalAssets\$imageName"
}

function Normalize-Label {
    param(
        [string]$Text
    )

    return $Text.Trim().TrimEnd(':')
}

if (-not (Test-Path $docxPath)) {
    throw "Could not find EXample.docx."
}

Copy-DocxImages -DocxPath $docxPath -DestinationPath $assetsPath -TempExtractPath $extractPath
$imageTargetMap = Get-ImageTargetMap -Path $relsPath
$entries = Get-ParagraphEntries -Path $paraMapPath

$peopleIndex = Find-EntryIndex -Entries $entries -Text 'PEOPLE'
$clientsIndex = Find-EntryIndex -Entries $entries -Text 'CLIENTS INCLUDE'

if ($peopleIndex -lt 0 -or $clientsIndex -lt 0) {
    throw 'Could not locate key brochure section boundaries in the extracted paragraph map.'
}

$overviewSections = New-Object System.Collections.Generic.List[object]
for ($headingPosition = 0; $headingPosition -lt $overviewHeadings.Count; $headingPosition++) {
    $heading = $overviewHeadings[$headingPosition]
    $startIndex = Find-EntryIndex -Entries $entries -Text $heading
    if ($startIndex -lt 0) {
        continue
    }

    $endIndex = if ($headingPosition -lt $overviewHeadings.Count - 1) {
        Find-EntryIndex -Entries $entries -Text $overviewHeadings[$headingPosition + 1]
    }
    else {
        $peopleIndex
    }

    $bodyParts = New-Object System.Collections.Generic.List[string]
    for ($i = $startIndex + 1; $i -lt $endIndex; $i++) {
        $text = $entries[$i].Text
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $bodyParts.Add($text)
    }

    $overviewSections.Add(@{
        Heading = $heading
        Body = ($bodyParts -join "`r`n`r`n")
    })
}

$personPhotos = @{
    'John Markulin' = 'GeneralTools\SeedData\OriginalAssets\image8.jpeg'
    'Jim DesRoches' = 'GeneralTools\SeedData\OriginalAssets\image9.jpeg'
    'Rory Beirne' = 'GeneralTools\SeedData\OriginalAssets\image10.jpeg'
    'Kevin Wurmlinger' = 'GeneralTools\SeedData\OriginalAssets\image11.jpeg'
    'Jason Stuart' = 'GeneralTools\SeedData\OriginalAssets\image12.jpeg'
    'John Bryson' = 'GeneralTools\SeedData\OriginalAssets\image15.jpeg'
}

$personBlocks = New-Object System.Collections.Generic.List[object]
$personStartIndex = $peopleIndex + 1
while ($personStartIndex -lt $entries.Count -and [string]::IsNullOrWhiteSpace($entries[$personStartIndex].Text)) {
    $personStartIndex++
}

$personnelBlurb = $entries[$personStartIndex].Text

for ($i = $personStartIndex + 1; $i -lt $entries.Count; ) {
    $text = $entries[$i].Text
    if ($projectSectionHeadings -contains $text) {
        break
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        $i++
        continue
    }

    $matchingPersonName = $personNames | Where-Object { $text -eq $_ -or $text.StartsWith("$_,") } | Select-Object -First 1
    if (-not $matchingPersonName) {
        $i++
        continue
    }

        $credentials = if ($text.Length -gt $matchingPersonName.Length) {
            $text.Substring($matchingPersonName.Length).TrimStart(',').Trim()
        }
    else {
        ''
    }

    $bioParts = New-Object System.Collections.Generic.List[string]
    $j = $i + 1
    while ($j -lt $entries.Count) {
        $nextText = $entries[$j].Text
        if ($projectSectionHeadings -contains $nextText) {
            break
        }

        if ($personNames | Where-Object { $nextText -eq $_ -or $nextText.StartsWith("$_,") }) {
            break
        }

        if (-not [string]::IsNullOrWhiteSpace($nextText)) {
            $bioParts.Add($nextText)
        }

        $j++
    }

    $personBlocks.Add(@{
        Name = $matchingPersonName
        Credentials = $credentials
        Bio = ($bioParts -join "`r`n`r`n")
        PhotoPath = ($personPhotos[$matchingPersonName] ?? '')
    })

    $i = $j
}

$sectionBlurbs = @{
    'FEATURED LARGE MIXED-USE COMMERCIAL & RESIDENTIAL PROJECTS - CANADA' = 'We have extensive experience with large residential and mixed-use buildings and have completed 2,000+ projects of all types from concrete high-rise (over 65 stories), concrete mid and low-rise, structural steel, and wood frame projects. The following illustrates some examples of large mixed-use projects we have designed.'
}

$sectionBlocks = New-Object System.Collections.Generic.List[object]
foreach ($sectionHeading in $projectSectionHeadings) {
    $sectionIndex = Find-EntryIndex -Entries $entries -Text $sectionHeading
    if ($sectionIndex -lt 0) {
        continue
    }

    $nextSectionIndex = $clientsIndex
    foreach ($candidateSection in $projectSectionHeadings) {
        $candidateIndex = Find-EntryIndex -Entries $entries -Text $candidateSection
        if ($candidateIndex -gt $sectionIndex -and $candidateIndex -lt $nextSectionIndex) {
            $nextSectionIndex = $candidateIndex
        }
    }

    $cursor = $sectionIndex + 1
    $blurbParts = New-Object System.Collections.Generic.List[string]
    while ($cursor -lt $nextSectionIndex -and -not (Is-ProjectTitleEntry -Entries $entries -Index $cursor -SectionEndIndex $nextSectionIndex)) {
        if (-not [string]::IsNullOrWhiteSpace($entries[$cursor].Text)) {
            $blurbParts.Add($entries[$cursor].Text)
        }

        $cursor++
    }

    if ($sectionBlurbs.ContainsKey($sectionHeading)) {
        $blurbText = $sectionBlurbs[$sectionHeading]
    }
    else {
        $blurbText = ($blurbParts -join "`r`n`r`n")
    }

    $projects = New-Object System.Collections.Generic.List[object]
    $pendingImageRel = $null

    while ($cursor -lt $nextSectionIndex) {
        $entry = $entries[$cursor]
        $text = $entry.Text

        if ([string]::IsNullOrWhiteSpace($text)) {
            if ($entry.RelId) {
                $pendingImageRel = $entry.RelId
            }

            $cursor++
            continue
        }

        if (-not (Is-ProjectTitleEntry -Entries $entries -Index $cursor -SectionEndIndex $nextSectionIndex)) {
            $cursor++
            continue
        }

        $title = $text
        $projectImageRel = if ($entry.RelId) { $entry.RelId } else { $pendingImageRel }
        $pendingImageRel = $null
        $cursor++

        $descriptionParts = New-Object System.Collections.Generic.List[string]
        while ($cursor -lt $nextSectionIndex) {
            $currentText = $entries[$cursor].Text
            if ((Normalize-Label $currentText) -in @('Client', 'Clients')) {
                break
            }

            if ((Is-ProjectTitleEntry -Entries $entries -Index $cursor -SectionEndIndex $nextSectionIndex) -or ($projectSectionHeadings -contains $currentText)) {
                break
            }

            if (-not [string]::IsNullOrWhiteSpace($currentText)) {
                $descriptionParts.Add($currentText)
                if (-not $projectImageRel -and $entries[$cursor].RelId) {
                    $projectImageRel = $entries[$cursor].RelId
                }
            }
            elseif ($entries[$cursor].RelId) {
                $projectImageRel = $projectImageRel ?? $entries[$cursor].RelId
            }

            $cursor++
        }

        $client = ''
        if ($cursor -lt $nextSectionIndex -and (Normalize-Label $entries[$cursor].Text) -in @('Client', 'Clients')) {
            $cursor++
            while ($cursor -lt $nextSectionIndex -and [string]::IsNullOrWhiteSpace($entries[$cursor].Text)) {
                $cursor++
            }

            if ($cursor -lt $nextSectionIndex) {
                $client = $entries[$cursor].Text
                $cursor++
            }
        }

        while ($cursor -lt $nextSectionIndex -and [string]::IsNullOrWhiteSpace($entries[$cursor].Text)) {
            $cursor++
        }

        $architect = ''
        if ($cursor -lt $nextSectionIndex -and (Normalize-Label $entries[$cursor].Text) -eq 'Architect') {
            $cursor++
            while ($cursor -lt $nextSectionIndex -and [string]::IsNullOrWhiteSpace($entries[$cursor].Text)) {
                $cursor++
            }

            if ($cursor -lt $nextSectionIndex) {
                $architect = $entries[$cursor].Text
                $cursor++
            }
        }

        $photoPath = Resolve-AssetPath -RelId $projectImageRel -ImageTargetMap $imageTargetMap
        $photos = @()
        if (-not [string]::IsNullOrWhiteSpace($photoPath)) {
            $photos = @(New-Photo -FilePath $photoPath)
        }

        $projects.Add(@{
            SectionLabel = $sectionHeading
            ProjectName = $title
            ProjectDescription = (($descriptionParts -join "`r`n`r`n").Trim())
            Client = $client
            Architect = $architect
            Photos = $photos
        })
    }

    $sectionBlocks.Add(@{
        BlockType = 'Section'
        Section = @{
            Heading = $sectionHeading
            Blurb = $blurbText
            Projects = $projects
            PageBreakAfterProjectIndex = @()
        }
        People = @()
        PersonnelHeading = 'People'
        PersonnelBlurb = ''
        OverviewSections = @()
        PageBreakAfterOverviewIndex = @()
    })
}

$clientsBody = (Get-Content (Join-Path $repoRoot '_example_docx_text.txt') | Select-Object -Skip 631 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`r`n"

$proposal = [ordered]@{
    Id = '7b76b5d92c274ecaaab985a66d3b3b3b'
    Name = 'Original'
    CreatedAt = [DateTime]::UtcNow.ToString('O')
    ModifiedAt = [DateTime]::UtcNow.ToString('O')
    Content = [ordered]@{
        TemplateName = 'Corporate Profile'
        CoverTitle = 'KOR Portfolio'
        CoverPhotoPath = 'GeneralTools\SeedData\OriginalAssets\image16.jpeg'
        CoverPhotoOpacity = 0.85
        CoverYear = $null
        CompanyName = ''
        LogoPath = ''
        Blocks = @(
            @{
                BlockType = 'Contact'
                Section = $null
                People = @()
                PersonnelHeading = 'People'
                PersonnelBlurb = ''
                OverviewSections = @()
                PageBreakAfterOverviewIndex = @()
            },
            @{
                BlockType = 'CompanyOverview'
                Section = $null
                People = @()
                PersonnelHeading = 'People'
                PersonnelBlurb = ''
                OverviewSections = $overviewSections
                PageBreakAfterOverviewIndex = @()
            },
            @{
                BlockType = 'Personnel'
                Section = $null
                People = $personBlocks
                PersonnelHeading = 'People'
                PersonnelBlurb = $personnelBlurb
                OverviewSections = @()
                PageBreakAfterOverviewIndex = @()
            }
        ) + $sectionBlocks + @(
            @{
                BlockType = 'CompanyOverview'
                Section = $null
                People = @()
                PersonnelHeading = 'People'
                PersonnelBlurb = ''
                OverviewSections = @(
                    @{
                        Heading = 'CLIENTS INCLUDE'
                        Body = $clientsBody
                    }
                )
                PageBreakAfterOverviewIndex = @()
            },
            @{
                BlockType = 'Contact'
                Section = $null
                People = @()
                PersonnelHeading = 'People'
                PersonnelBlurb = ''
                OverviewSections = @()
                PageBreakAfterOverviewIndex = @()
            }
        )
    }
}

$proposal | ConvertTo-Json -Depth 12 | Set-Content $outputJsonPath

$sectionSummary = $sectionBlocks | ForEach-Object {
    '{0}: {1} projects' -f $_.Section.Heading, $_.Section.Projects.Count
}

Write-Host ('Wrote seed proposal to {0}' -f $outputJsonPath)
Write-Host ('Imported {0} overview sections, {1} people, {2} project sections.' -f $overviewSections.Count, $personBlocks.Count, $sectionBlocks.Count)
$sectionSummary | ForEach-Object { Write-Host $_ }
