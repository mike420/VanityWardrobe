param([string] $ReadmeFile, [string] $NexusFile)

# Turns the Thunderstore README (markdown) into BBCode for the Nexus page. Nexus has no tables, inline code or emoji,
# so each table row becomes a list item, inline code becomes bold, and emoji are dropped.

function Remove-Emoji([string] $text) {
    $emoji = '[' + [char]0xD800 + '-' + [char]0xDFFF + [char]0x2600 + '-' + [char]0x27BF + [char]0x2B00 + '-' + [char]0x2BFF + [char]0xFE0F + ']'
    return ([regex]::Replace([regex]::Replace($text, $emoji, ''), ' {2,}', ' ')).Trim()
}

function Convert-Inline([string] $text) {
    $text = [regex]::Replace($text, '<a href="([^"]+)">!\[[^\]]*\]\(([^)]+)\)</a>', '[url=$1][img]$2[/img][/url]')
    $text = [regex]::Replace($text, '`([^`]+)`', '[b]$1[/b]')
    $text = [regex]::Replace($text, '\*\*(.+?)\*\*', '[b]$1[/b]')
    return Remove-Emoji $text
}

$out = New-Object System.Collections.Generic.List[string]
$inList = $false
function Close-List { if ($script:inList) { $script:out.Add('[/list]'); $script:inList = $false } }
function Open-List { if (-not $script:inList) { $script:out.Add('[list]'); $script:inList = $true } }

foreach ($raw in [System.IO.File]::ReadAllLines($ReadmeFile)) {
    $line = $raw.Trim()
    if ($line -eq '' -or $line -eq '<br>' -or $line -match '^\|[-| ]+\|$') { continue }
    if ($line -match '^# (.+)$') { Close-List; $out.Add('[size=6][b]' + (Remove-Emoji $Matches[1]) + '[/b][/size]'); $out.Add(''); continue }
    if ($line -match '^## (.+)$') { Close-List; $out.Add(''); $out.Add('[size=5][b]' + (Remove-Emoji $Matches[1]).TrimEnd(':') + '[/b][/size]'); $out.Add(''); continue }
    if ($line -match '^\* +(.+)$') { Open-List; $out.Add('[*]' + (Convert-Inline $Matches[1])); continue }
    if ($line -match '^\|(.+)\|$') {
        $cells = $Matches[1].Split('|') | ForEach-Object { Convert-Inline $_.Trim() }
        if ($cells[0] -eq 'Setting') { continue }
        Open-List
        $out.Add('[*][b]' + $cells[0] + '[/b] (default ' + $cells[1] + '): ' + $cells[2])
        continue
    }
    Close-List
    $out.Add((Convert-Inline ($line -replace '<br\s*/?>$', '')))
}
Close-List
[System.IO.File]::WriteAllText($NexusFile, ($out -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($true)))
