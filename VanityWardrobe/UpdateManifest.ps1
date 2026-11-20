param([string] $ManifestFile, [string] $Version)

# Writes the built version into manifest.json's version_number
$text = [System.IO.File]::ReadAllText($ManifestFile)
$text = [regex]::Replace($text, '"version_number"\s*:\s*"[^"]*"', '"version_number": "' + $Version + '"')
[System.IO.File]::WriteAllText($ManifestFile, $text)
