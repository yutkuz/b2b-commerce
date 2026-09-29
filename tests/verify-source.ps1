$ErrorActionPreference = "Stop"

$violations = [System.Collections.Generic.List[string]]::new()
$sourceRoots = @(
    "src/U1.Business",
    "tests/U1.Business.Tests",
    "tests/U1.Business.BrowserTests"
)
$sourceFiles = Get-ChildItem $sourceRoots -Recurse -File |
    Where-Object { $_.Extension -in @(".cs", ".js") }

foreach ($file in $sourceFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)

    if ($text.Contains("`t")) {
        $violations.Add("$($file.FullName): tab character found")
    }

    if ([regex]::IsMatch($text, "[ `t]+(?=`r?$)", [Text.RegularExpressions.RegexOptions]::Multiline)) {
        $violations.Add("$($file.FullName): trailing whitespace found")
    }

    if (-not $text.EndsWith("`n")) {
        $violations.Add("$($file.FullName): missing final newline")
    }
}

$jsFiles = Get-ChildItem "src/U1.Business/wwwroot" -Filter *.js -Recurse -File
$importVersions = @{}
$importPattern = @'
(?m)\bfrom\s+["'](?<path>[^"']+\.js)\?v=(?<version>[A-Za-z0-9]+)["']
'@
$importRegex = [regex]::new($importPattern.Trim())

foreach ($file in $jsFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in $importRegex.Matches($text)) {
        $target = [System.IO.Path]::GetFullPath(
            (Join-Path $file.DirectoryName $match.Groups["path"].Value)
        ).ToLowerInvariant()
        $version = $match.Groups["version"].Value

        if ($importVersions.ContainsKey($target) -and $importVersions[$target] -ne $version) {
            $violations.Add(
                "$($file.FullName): inconsistent cache version for $target "
                + "($($importVersions[$target]) vs $version)"
            )
        }
        else {
            $importVersions[$target] = $version
        }
    }
}

$stateDefinitions = @(
    Select-String -Path $jsFiles.FullName -Pattern "\bexport\s+const\s+state\s*="
)
if ($stateDefinitions.Count -ne 1) {
    $violations.Add(
        "Expected exactly one exported shared state definition; found $($stateDefinitions.Count)."
    )
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    throw "Source convention checks failed."
}

Write-Host "Source convention checks passed for $($sourceFiles.Count) C#/JS files."
